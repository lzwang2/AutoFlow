using AutoFlow.Core.Scripting.Rules;
using Newtonsoft.Json;
using Xunit;

namespace AutoFlow.Core.Tests;

/// <summary>
/// 针对仓库内真实脚本资产的基线测试。
///
/// Phase 6 迁移后更新：模板文件现在位于 Rules/ 子目录，规则使用 ConditionRef 而非内联
/// TemplatePath，格式版本现在是 2。
/// </summary>
public class RuleScriptAssetTests
{
    /// <summary>
    /// 从测试程序集位置向上找仓库根（含 AutoFlow.sln 的目录）。
    /// 不硬编码绝对路径，避免重现 test.rules.json 曾有的机器相关路径问题。
    /// </summary>
    private static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AutoFlow.sln")))
                dir = dir.Parent;

            Assert.NotNull(dir);
            return dir!.FullName;
        }
    }

    private static string TemplateDir => Path.Combine(RepoRoot, "RuleTemplates", "LastCloudia", "Rules");

    public static TheoryData<string> RuleTemplateFiles
    {
        get
        {
            var data = new TheoryData<string>();
            var dir = TemplateDir;
            if (!Directory.Exists(dir))
                return data;

            foreach (var path in Directory.GetFiles(dir, "*.rules.json"))
                data.Add(Path.GetFileName(path));
            return data;
        }
    }

    private static RuleScript Load(string fileName)
    {
        var json = File.ReadAllText(Path.Combine(TemplateDir, fileName));
        var script = JsonConvert.DeserializeObject<RuleScript>(json);
        Assert.NotNull(script);
        return script!;
    }

    [Fact]
    public void TemplateLibrary_HasExpectedFileCount()
    {
        // Phase 6 后：11 份规则模板，现在位于 LastCloudia/Rules/ 子目录。
        if (!Directory.Exists(TemplateDir))
        {
            Assert.Fail($"规则模板目录不存在: {TemplateDir}");
        }

        Assert.Equal(11, Directory.GetFiles(TemplateDir, "*.rules.json").Length);
    }

    [Theory]
    [MemberData(nameof(RuleTemplateFiles))]
    public void EveryTemplate_UsesCanonicalSchema(string fileName)
    {
        // 曾存在两种 Schema 值（rule-template.schema.json / autoflow/rule-script），
        // 版本闸门依赖此值稳定，故加断言防回归。
        Assert.Equal("autoflow/rule-script", Load(fileName).Schema);
    }

    [Theory]
    [MemberData(nameof(RuleTemplateFiles))]
    public void EveryTemplate_DeclaresOuterSize(string fileName)
    {
        var script = Load(fileName);

        // OuterWidth/Height 是 FR-10 参照空间校验的比较基准，不可缺省。
        Assert.True(script.OuterWidth > 0, $"{fileName}: OuterWidth 未设置");
        Assert.True(script.OuterHeight > 0, $"{fileName}: OuterHeight 未设置");
    }

    [Theory]
    [MemberData(nameof(RuleTemplateFiles))]
    public void EveryTemplate_UsesConditionReferences(string fileName)
    {
        // Phase 6 后：规则通过 ConditionRef 引用共享条件，不再有内联 TemplatePath。
        var script = Load(fileName);
        Assert.NotEmpty(script.Rules);
        Assert.Equal(2, script.FormatVersion);

        foreach (var rule in script.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.ConditionRef))
            {
                // 只有无条件 catch-all 规则允许没有 ConditionRef，MultiPlayerGame 有一条。
                Assert.True(
                    string.IsNullOrWhiteSpace(rule.TemplatePath),
                    $"{fileName} / {rule.Name}: 既无 ConditionRef 也非兜底规则");
                continue;
            }

            // 迁移后不应再有内联条件字段。
            Assert.Null(rule.TemplatePath);
            Assert.Equal(-1, rule.RegionX1);
            Assert.Equal(-1, rule.RegionY1);

            // ConditionRef 必须是 LastCloudia.* 形式。
            Assert.StartsWith("LastCloudia.", rule.ConditionRef);
        }
    }

    [Fact]
    public void TestScripts_UseConditionReferences()
    {
        // TestScripts/ 下的两份脚本也已迁移。
        foreach (var fileName in new[] { "test.rules.json", "MultiPlayerGame.rules.json" })
        {
            var path = Path.Combine(RepoRoot, "TestScripts", fileName);
            var script = JsonConvert.DeserializeObject<RuleScript>(File.ReadAllText(path));
            Assert.NotNull(script);
            Assert.Equal(2, script!.FormatVersion);

            foreach (var rule in script.Rules)
            {
                if (string.IsNullOrWhiteSpace(rule.ConditionRef))
                    continue; // catch-all

                Assert.StartsWith("LastCloudia.", rule.ConditionRef);
                Assert.Null(rule.TemplatePath);
            }
        }
    }
}
