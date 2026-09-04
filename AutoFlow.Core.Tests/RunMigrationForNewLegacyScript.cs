using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Migration;
using Xunit;
using Xunit.Abstractions;

namespace AutoFlow.Core.Tests;

/// <summary>
/// **Migration runner for v1 scripts authored after Phase 6.**
///
/// <para>Phase 6 migrated the 13 scripts that existed then. A script written by an older build
/// afterwards — or hand-made in an output directory and never committed — is still v1, and the
/// version gate now refuses to load it. This runner migrates whatever is left, using the same
/// <see cref="RuleScriptMigrator"/> Phase 6 used, so such a file gets exactly the same treatment
/// rather than a one-off hand edit. This is why the migration tooling was kept.</para>
///
/// <para>The rehearsal test is safe to run any time: it works on a throwaway copy. The applying
/// test modifies the repository and is skipped by default — remove the Skip to run it.</para>
/// </summary>
public class RunMigrationForNewLegacyScript
{
    private readonly ITestOutputHelper _output;

    public RunMigrationForNewLegacyScript(ITestOutputHelper output) => _output = output;

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

    /// <summary>Every .rules.json under the repo that still declares a pre-current version.</summary>
    private static List<string> FindLegacyScripts(string root)
    {
        var found = new List<string>();

        foreach (var directory in new[] { "RuleTemplates", "TestScripts" })
        {
            var path = Path.Combine(root, directory);
            if (!Directory.Exists(path)) continue;

            foreach (var file in Directory.GetFiles(
                         path, "*" + TemplateLayout.RuleExtension, SearchOption.AllDirectories))
            {
                var version = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file))
                    .Value<int?>("FormatVersion") ?? 1;

                if (version < ScriptFormatVersion.Current)
                    found.Add(file);
            }
        }

        return found;
    }

    private void Report(MigrationReport report)
    {
        _output.WriteLine(report.Summarize());
        _output.WriteLine("");

        foreach (var line in report.Log) _output.WriteLine("  " + line);
        foreach (var error in report.Errors) _output.WriteLine("  错误: " + error);
    }

    [Fact]
    public void RehearseInSandbox()
    {
        var repoRoot = FindRepositoryRoot();
        if (repoRoot is null)
        {
            _output.WriteLine("未找到仓库根目录，跳过。");
            return;
        }

        var legacy = FindLegacyScripts(repoRoot);
        _output.WriteLine($"仓库中仍为旧版本的脚本: {legacy.Count}");
        foreach (var file in legacy)
        {
            _output.WriteLine("  " + Path.GetRelativePath(repoRoot, file));
        }

        if (legacy.Count == 0)
        {
            _output.WriteLine("没有需要迁移的脚本。");
            return;
        }

        var sandbox = RepositoryMigration.CreateSandbox(repoRoot);
        try
        {
            var migration = new RepositoryMigration(sandbox);

            // Templates are already in Rules/; moving again would be a no-op at best, so the
            // move step is left off to keep the rehearsal focused on the content rewrite.
            var report = migration.Run(moveTemplatesIntoRulesDirectory: false);
            Report(report);

            Assert.True(report.Success, "沙箱预演失败，不要应用到仓库。");
            Assert.Equal(legacy.Count, report.ScriptsMigrated);

            // 等价性校验必须真的跑过，否则"成功"只是说没抛异常。
            Assert.Contains(report.Log, line => line.Contains("结构等价性校验"));
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact(Skip = "Applied 2026-09-04 for network_error_game_download; remove Skip to migrate new v1 scripts")]
    public void ApplyToRepository()
    {
        var repoRoot = FindRepositoryRoot() ?? throw new InvalidOperationException("未找到仓库根目录。");

        var legacy = FindLegacyScripts(repoRoot);
        if (legacy.Count == 0)
        {
            _output.WriteLine("没有需要迁移的脚本。");
            return;
        }

        var migration = new RepositoryMigration(repoRoot);
        var report = migration.Run(moveTemplatesIntoRulesDirectory: true);
        Report(report);

        Assert.True(report.Success, "迁移失败，详见上方日志。");
        _output.WriteLine("迁移成功。请检查 git diff 后提交。");
    }
}
