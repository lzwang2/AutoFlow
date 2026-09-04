using System.Diagnostics;
using System.Text;
using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Migration;
using Xunit;
using Xunit.Abstractions;

namespace AutoFlow.Core.Tests;

/// <summary>
/// Plan 建议测试项 9 / Spec AC-06: the 13 migrated scripts must decide the same thing and run the
/// same actions as before the migration.
///
/// <para>The reference input is the v1 text from git history rather than a copy checked in beside
/// the test. A copy would be a snapshot someone has to remember to keep honest; the commit is the
/// actual pre-migration state and cannot drift. This is also why the migration tooling was kept
/// rather than deleted in Phase 7 — <see cref="MigrationEquivalenceChecker"/> is the comparison,
/// so the test cannot disagree with what the migration considered equivalent.</para>
///
/// <para>Skips when the source tree or git is unavailable, so a packaged build still passes.</para>
/// </summary>
public class MigrationStructuralEquivalenceTests
{
    /// <summary>The commit holding the pre-migration (FormatVersion 1) scripts.</summary>
    private const string LegacyCommit = "e8183ad";

    private const string Game = "LastCloudia";

    private readonly ITestOutputHelper _output;

    public MigrationStructuralEquivalenceTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Every migrated script, as (v1 path in <see cref="LegacyCommit"/>, current v2 path).
    /// The template scripts moved into <c>Rules/</c> during Phase 6; the two under
    /// <c>TestScripts/</c> stayed where they were.
    /// </summary>
    private static IEnumerable<(string Legacy, string Current)> ScriptPairs()
    {
        string[] templates =
        [
            "abandon_task", "first_clear_reward", "first_meeting", "multi_clear_reward",
            "multi_game_refight", "network_error", "refight_denied",
            "server_connection_fail_multiplayer", "server_connection_fail_retreat",
            "server_connection_fail_return", "unable_to_join"
        ];

        foreach (var name in templates)
        {
            yield return (
                $"RuleTemplates/{Game}/{name}.rules.json",
                $"RuleTemplates/{Game}/{TemplateLayout.RulesDirectoryName}/{name}.rules.json");
        }

        yield return ("TestScripts/MultiPlayerGame.rules.json", "TestScripts/MultiPlayerGame.rules.json");
        yield return ("TestScripts/test.rules.json", "TestScripts/test.rules.json");
    }

    /// <summary>Walks up from the test assembly to the directory containing AutoFlow.sln.</summary>
    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "AutoFlow.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>Reads a file's content at <see cref="LegacyCommit"/>, or null if git failed.</summary>
    private static string? ReadFromGit(string repoRoot, string repoRelativePath)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            // Git emits UTF-8; the default console encoding would mangle the Chinese rule names
            // into a difference that has nothing to do with the migration.
            StandardOutputEncoding = new UTF8Encoding(false)
        };
        startInfo.ArgumentList.Add("show");
        startInfo.ArgumentList.Add($"{LegacyCommit}:{repoRelativePath}");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null) return null;

            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            return process.ExitCode == 0 ? stdout : null;
        }
        catch (Exception)
        {
            // git missing, or not a repository: treated as "cannot verify", not as a failure.
            return null;
        }
    }

    [Fact]
    public void EveryMigratedScriptKeepsItsBehaviour()
    {
        var repoRoot = FindRepositoryRoot();
        if (repoRoot is null)
        {
            _output.WriteLine("未找到仓库根目录（AutoFlow.sln），跳过。");
            return;
        }

        var library = new ConditionLibrary(Path.Combine(repoRoot, "RuleTemplates"));
        var warnings = library.LoadGame(Game);
        foreach (var warning in warnings) _output.WriteLine("条件库警告: " + warning);

        var conditions = library.All.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase);
        Assert.NotEmpty(conditions);

        var differences = new List<string>();
        var compared = 0;

        foreach (var (legacyPath, currentPath) in ScriptPairs())
        {
            var legacyJson = ReadFromGit(repoRoot, legacyPath);
            if (legacyJson is null)
            {
                _output.WriteLine($"取不到 {LegacyCommit}:{legacyPath}，跳过该脚本。");
                continue;
            }

            var currentFile = Path.Combine(repoRoot, currentPath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(currentFile), $"迁移后的脚本不存在: {currentPath}");

            var before = MigrationEquivalenceChecker.ReadLegacy(legacyJson);
            var after = MigrationEquivalenceChecker.ReadMigrated(File.ReadAllText(currentFile), conditions);

            differences.AddRange(MigrationEquivalenceChecker.Compare(currentPath, before, after));
            compared++;

            _output.WriteLine($"{currentPath}: {before.Count} 条规则已比对");
        }

        // 13 份全部比对到，才算真的覆盖 AC-06；少一份就是漏检而不是通过。
        Assert.Equal(13, compared);
        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
    }

    [Fact]
    public void MigratedScriptsAreAllAtTheCurrentFormatVersion()
    {
        var repoRoot = FindRepositoryRoot();
        if (repoRoot is null) return;

        foreach (var (_, currentPath) in ScriptPairs())
        {
            var file = Path.Combine(repoRoot, currentPath.Replace('/', Path.DirectorySeparatorChar));
            var root = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));

            Assert.Equal(
                ScriptFormatVersion.Current,
                root.Value<int>(nameof(AutoFlow.Core.Scripting.Rules.RuleScript.FormatVersion)));

            // Phase 7 删掉了内联条件字段，仓库资产里也不应再留下任何一个。
            foreach (var rule in root["Rules"]!.Cast<Newtonsoft.Json.Linq.JObject>())
            {
                foreach (var field in LegacyRuleFields.All)
                {
                    Assert.False(rule.ContainsKey(field), $"{currentPath} 仍带内联字段 '{field}'");
                }
            }
        }
    }

    [Fact]
    public void CatchAllRuleStaysUnconditionalAfterMigration()
    {
        // 项 12 的资产侧对照：MultiPlayerGame 的兜底规则在 v1 里是 TemplatePath: null，
        // 迁移后必须是"没有 ConditionRef"，而不是指向某个凑出来的条件。
        var repoRoot = FindRepositoryRoot();
        if (repoRoot is null) return;

        var legacyJson = ReadFromGit(repoRoot, "TestScripts/MultiPlayerGame.rules.json");
        if (legacyJson is null) return;

        var before = MigrationEquivalenceChecker.ReadLegacy(legacyJson);
        var library = new ConditionLibrary(Path.Combine(repoRoot, "RuleTemplates"));
        library.LoadGame(Game);

        var after = MigrationEquivalenceChecker.ReadMigrated(
            File.ReadAllText(Path.Combine(repoRoot, "TestScripts", "MultiPlayerGame.rules.json")),
            library.All.ToDictionary(c => c.Id, StringComparer.OrdinalIgnoreCase));

        Assert.Single(before, r => r.IsCatchAll);
        Assert.Single(after, r => r.IsCatchAll);

        // 兜底规则必须还是最后一条：提前一位就会吃掉后面所有规则。
        Assert.True(before[^1].IsCatchAll);
        Assert.True(after[^1].IsCatchAll);
    }
}
