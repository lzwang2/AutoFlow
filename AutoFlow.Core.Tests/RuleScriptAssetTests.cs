using AutoFlow.Core.Scripting.Rules;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        // Phase 6 后：规则模板位于 LastCloudia/Rules/ 子目录。
        // Phase 6 迁移了 11 份，之后 network_error_game_download 补迁进来，共 12 份。
        if (!Directory.Exists(TemplateDir))
        {
            Assert.Fail($"规则模板目录不存在: {TemplateDir}");
        }

        Assert.Equal(12, Directory.GetFiles(TemplateDir, "*.rules.json").Length);
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
                continue; // 无条件 catch-all，MultiPlayerGame 有一条

            // ConditionRef 必须是 LastCloudia.* 形式。
            Assert.StartsWith("LastCloudia.", rule.ConditionRef);
        }

        // 内联字段必须查原始 JSON：模型已经不声明这些属性了，反序列化会静默丢掉它们，
        // 所以走 Rule 对象永远"通过"，查不出文件里到底还剩什么。
        AssertNoInlineConditionFields(
            File.ReadAllText(Path.Combine(TemplateDir, fileName)), fileName);
    }

    /// <summary>
    /// 断言脚本 JSON 里没有任何 v1 内联条件字段。留一个就意味着这份文件没迁完，
    /// 而这是加载时看不出来的——版本号已经是 2，字段却被忽略掉。
    /// </summary>
    private static void AssertNoInlineConditionFields(string json, string fileName)
    {
        var root = JObject.Parse(json);

        if (root["Rules"] is not JArray rules)
            return;

        for (var i = 0; i < rules.Count; i++)
        {
            if (rules[i] is not JObject rule)
                continue;

            foreach (var field in LegacyInlineFields)
            {
                Assert.False(
                    rule.ContainsKey(field),
                    $"{fileName} 规则 #{i} 仍带内联条件字段 '{field}'，该字段应已移入共享条件。");
            }
        }
    }

    private static readonly string[] LegacyInlineFields =
    [
        "TemplatePath", "MatchThreshold", "MeanDiffThreshold",
        "RegionX1", "RegionY1", "RegionX2", "RegionY2"
    ];

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
            }

            AssertNoInlineConditionFields(File.ReadAllText(path), fileName);
        }
    }
}
