using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Migration;
using AutoFlow.Core.Scripting.Rules;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace AutoFlow.Core.Tests;

/// <summary>
/// Rehearses the real Phase 6 migration against a throwaway copy of the repository's scripts.
///
/// <para>This is the check that matters: unit tests use synthetic scripts, so only running the
/// actual 13 files proves the tool handles the real inputs — the two <c>_area</c> filenames, the
/// file-relative images in <c>TestScripts/</c>, the catch-all rule, and the duplicate
/// network-error condition.</para>
///
/// <para>Skips silently when the repository cannot be located, so the suite still passes from a
/// packaged build where the source tree is absent.</para>
/// </summary>
public class RepositoryMigrationTests
{
    private readonly ITestOutputHelper _output;

    public RepositoryMigrationTests(ITestOutputHelper output) => _output = output;

    /// <summary>Walks up from the test assembly to the directory containing RuleTemplates/.</summary>
    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "RuleTemplates"))
                && Directory.Exists(Path.Combine(directory.FullName, "TestScripts")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    [Fact(Skip = "Phase 6 migration completed; repository files are now v2")]
    public void RealRepositoryMigratesWithBehaviourPreserved()
    {
        var repoRoot = FindRepositoryRoot();
        if (repoRoot is null)
        {
            _output.WriteLine("未找到仓库根目录，跳过。");
            return;
        }

        var sandbox = RepositoryMigration.CreateSandbox(repoRoot);
        try
        {
            var migration = new RepositoryMigration(sandbox);
            var scripts = migration.DiscoverScripts();
            _output.WriteLine($"发现 {scripts.Count} 份脚本");

            var report = migration.Run();
            _output.WriteLine(report.Summarize());
            foreach (var line in report.Log)
            {
                _output.WriteLine("  " + line);
            }

            Assert.True(report.Success, report.Summarize());

            // 13 份：11 个模板 + MultiPlayerGame + test
            Assert.Equal(13, report.ScriptsMigrated);

            // 去重必须有实际效果，否则 NFR-01 的收益并未实现。
            Assert.True(report.DuplicatesMerged > 0, "没有合并任何重复条件");

            // MultiPlayerGame 的兜底规则必须保持无条件。
            Assert.True(report.CatchAllRules >= 1);

            // TestScripts/images 下的 2 张图要提升进库。
            Assert.Equal(2, report.ImagesPromoted);
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact(Skip = "Phase 6 migration completed; repository files are now v2")]
    public void MigratedRepositoryHasNoInlineConditionsOrAbsolutePaths()
    {
        var repoRoot = FindRepositoryRoot();
        if (repoRoot is null) return;

        var sandbox = RepositoryMigration.CreateSandbox(repoRoot);
        try
        {
            var migration = new RepositoryMigration(sandbox);
            var report = migration.Run();
            Assert.True(report.Success, report.Summarize());

            foreach (var path in migration.DiscoverScripts())
            {
                var root = JObject.Parse(File.ReadAllText(path));
                var name = Path.GetFileName(path);

                Assert.Equal(ScriptFormatVersion.Current, root.Value<int>(nameof(RuleScript.FormatVersion)));

                // 走 LegacyRuleFields.All，而不是自己列一份：这份断言必须和迁移真正剥掉的
                // 字段是同一个清单，否则漏掉一个字段两边都能"通过"。
                foreach (var rule in root[nameof(RuleScript.Rules)]!.Cast<JObject>())
                {
                    foreach (var field in LegacyRuleFields.All)
                    {
                        Assert.False(rule.ContainsKey(field), $"{name} 仍带内联字段 '{field}'");
                    }
                }

                _output.WriteLine($"{name}: OK");
            }

            // 条件文件里不能出现绝对路径，也不能出现 Area 后缀的 Id。
            var conditionsDir = Path.Combine(
                migration.TemplateRoot, RepositoryMigration.ConditionGame, TemplateLayout.ConditionsDirectoryName);

            var conditionFiles = Directory.GetFiles(conditionsDir, "*" + TemplateLayout.ConditionExtension);
            Assert.NotEmpty(conditionFiles);

            foreach (var file in conditionFiles)
            {
                var condition = Newtonsoft.Json.JsonConvert
                    .DeserializeObject<VisualConditionSpec>(File.ReadAllText(file))!;

                Assert.False(
                    Path.IsPathRooted(condition.TemplatePath),
                    $"{Path.GetFileName(file)} 的图片是绝对路径: {condition.TemplatePath}");

                Assert.False(
                    condition.Id.EndsWith("Area", StringComparison.Ordinal),
                    $"条件 Id 不应以 Area 结尾: {condition.Id}");

                // 参照空间必须记录，否则区域坐标失去意义（FR-10）。
                Assert.True(condition.ReferenceOuterWidth > 0);
                Assert.True(condition.ReferenceOuterHeight > 0);

                _output.WriteLine(
                    $"  条件 {condition.Id}: {condition.TemplatePath} " +
                    $"区域 ({condition.RegionX1},{condition.RegionY1})-({condition.RegionX2},{condition.RegionY2}) " +
                    $"参照 {condition.ReferenceOuterWidth}x{condition.ReferenceOuterHeight}");
            }
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact(Skip = "Phase 6 migration completed; repository files are now v2")]
    public void MigratedScriptsLoadAndResolveEveryReference()
    {
        var repoRoot = FindRepositoryRoot();
        if (repoRoot is null) return;

        var sandbox = RepositoryMigration.CreateSandbox(repoRoot);
        try
        {
            var migration = new RepositoryMigration(sandbox);
            Assert.True(migration.Run().Success);

            var library = new ConditionLibrary(migration.TemplateRoot);
            var warnings = library.LoadGame(RepositoryMigration.ConditionGame);
            foreach (var warning in warnings)
            {
                _output.WriteLine("警告: " + warning);
            }
            Assert.Empty(warnings);

            var service = new RuleScriptService();

            foreach (var path in migration.DiscoverScripts())
            {
                var script = service.Load(path);
                var name = Path.GetFileName(path);

                for (var i = 0; i < script.Rules.Count; i++)
                {
                    var rule = script.Rules[i];
                    if (!rule.UsesSharedCondition)
                    {
                        // 内联字段在 Phase 7 已删除，所以没有 ConditionRef 就是兜底规则，
                        // 不存在"既无引用又带内联条件"的第三种状态。
                        _output.WriteLine($"{name} 规则 #{i}: 兜底规则");
                        continue;
                    }

                    var condition = library.Require(rule.ConditionRef!, name, $"规则 #{i}");
                    Assert.True(
                        File.Exists(condition.EffectiveTemplatePath),
                        $"{name} 规则 #{i} 的条件 '{condition.Id}' 图片不存在: {condition.EffectiveTemplatePath}");
                }
            }
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact(Skip = "Phase 6 migration completed; repository files are now v2")]
    public void MigrationIsIdempotent()
    {
        var repoRoot = FindRepositoryRoot();
        if (repoRoot is null) return;

        var sandbox = RepositoryMigration.CreateSandbox(repoRoot);
        try
        {
            var migration = new RepositoryMigration(sandbox);
            Assert.True(migration.Run().Success);

            var afterFirst = migration.DiscoverScripts()
                .ToDictionary(p => Path.GetFileName(p), File.ReadAllText);

            // 第二次运行必须全部跳过且不改动任何文件——迁移可能被重复触发。
            var second = migration.Run();
            Assert.True(second.Success, second.Summarize());
            Assert.Equal(0, second.ScriptsMigrated);
            Assert.Equal(13, second.ScriptsAlreadyCurrent);

            foreach (var (name, content) in afterFirst)
            {
                var path = migration.DiscoverScripts().Single(p => Path.GetFileName(p) == name);
                Assert.Equal(content, File.ReadAllText(path));
            }
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }

    [Fact(Skip = "Phase 6 migration completed; repository files are now v2")]
    public void TemplateScriptsEndUpInRulesDirectory()
    {
        var repoRoot = FindRepositoryRoot();
        if (repoRoot is null) return;

        var sandbox = RepositoryMigration.CreateSandbox(repoRoot);
        try
        {
            var migration = new RepositoryMigration(sandbox);
            Assert.True(migration.Run().Success);

            var gameDir = Path.Combine(migration.TemplateRoot, RepositoryMigration.ConditionGame);
            var rulesDir = Path.Combine(gameDir, TemplateLayout.RulesDirectoryName);

            Assert.Equal(11, Directory.GetFiles(rulesDir, "*" + TemplateLayout.RuleExtension).Length);
            Assert.Empty(Directory.GetFiles(gameDir, "*" + TemplateLayout.RuleExtension));

            // images/ 故意不动：条件里存的是 "<Game>/images/x.png"。
            Assert.True(Directory.Exists(Path.Combine(gameDir, TemplateLayout.ImagesDirectoryName)));
        }
        finally
        {
            if (Directory.Exists(sandbox)) Directory.Delete(sandbox, recursive: true);
        }
    }
}
