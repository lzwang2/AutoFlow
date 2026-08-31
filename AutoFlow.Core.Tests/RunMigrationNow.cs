using AutoFlow.Core.Scripting.Migration;
using Xunit;
using Xunit.Abstractions;

namespace AutoFlow.Core.Tests;

/// <summary>
/// **One-time migration runner.** Executes Phase 6 work items 1–3: extract inline conditions
/// into the shared library, deduplicate them, rewrite all 13 scripts to references, verify
/// structural equivalence, move template scripts into Rules/, bump FormatVersion.
///
/// <para>Runs against the real repository when invoked. The test framework provides output
/// capture and a familiar run environment, but this is the actual migration — it modifies files
/// on disk when it succeeds.</para>
/// </summary>
public class RunMigrationNow
{
    private readonly ITestOutputHelper _output;

    public RunMigrationNow(ITestOutputHelper output) => _output = output;

    private static string FindRepositoryRoot()
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

        throw new InvalidOperationException("未找到仓库根目录（从测试程序集向上搜索 RuleTemplates/）。");
    }

    [Fact(Skip = "One-time migration completed in Phase 6")]
    public void ExecutePhase6Migration()
    {
        var repoRoot = FindRepositoryRoot();
        _output.WriteLine($"仓库根目录: {repoRoot}");
        _output.WriteLine("");

        var migration = new RepositoryMigration(repoRoot);
        var scripts = migration.DiscoverScripts();

        _output.WriteLine($"发现 {scripts.Count} 份脚本:");
        foreach (var path in scripts)
        {
            _output.WriteLine($"  {Path.GetFileName(path)}");
        }
        _output.WriteLine("");

        var report = migration.Run();

        _output.WriteLine(report.Summarize());
        _output.WriteLine("");

        if (report.Log.Count > 0)
        {
            _output.WriteLine("日志:");
            foreach (var line in report.Log)
            {
                _output.WriteLine($"  {line}");
            }
            _output.WriteLine("");
        }

        if (report.Errors.Count > 0)
        {
            _output.WriteLine("错误:");
            foreach (var error in report.Errors)
            {
                _output.WriteLine($"  {error}");
            }
            _output.WriteLine("");
        }

        Assert.True(report.Success, "迁移失败，详见上方日志。");

        _output.WriteLine("迁移成功。检查 git diff 确认改动符合预期，然后提交。");
    }
}
