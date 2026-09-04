using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Rules;
using Newtonsoft.Json;
using Xunit;

namespace AutoFlow.Core.Tests;

/// <summary>
/// The version gate on the <i>template import</i> path (FR-11).
///
/// <para>Template import is the second way a rule script enters the editor, and it had no gate:
/// <see cref="RuleScriptService.Load"/> checked the version, <see cref="RuleTemplateService"/>
/// did not. The existing version tests all went through the former, so the hole was invisible.
/// These tests pin the import path specifically.</para>
///
/// <para>What the missing gate actually produced is worth stating, because "it threw no error"
/// was the whole problem: a v1 rule's inline condition fields no longer exist on the model, so
/// they are silently dropped on deserialize, leaving <c>ConditionRef</c> empty — which in v2
/// semantics means <i>unconditional catch-all</i>. The rule imported "successfully" and then
/// matched on every tick instead of checking its image.</para>
/// </summary>
public class TemplateImportVersionGateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "AutoFlowTests_" + Guid.NewGuid().ToString("N"));

    public TemplateImportVersionGateTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private const string Game = "TestGame";

    /// <summary>Writes a rule template into &lt;root&gt;/&lt;Game&gt;/Rules/ and returns its path.</summary>
    private string WriteTemplate(string fileName, object payload)
    {
        var directory = TemplateLayout.DirectoryFor(_root, Game, TemplateKind.Rule);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, fileName + TemplateLayout.RuleExtension);
        File.WriteAllText(path, JsonConvert.SerializeObject(payload, Formatting.Indented));
        return path;
    }

    /// <summary>
    /// A v1 rule template, shaped exactly like the real one that exposed this: inline condition
    /// fields, region, thresholds, and no ConditionRef.
    /// </summary>
    private static object LegacyPayload() => new
    {
        Schema = "autoflow/rule-script",
        FormatVersion = 1,
        Metadata = new { Name = "网络错误-游戏更新" },
        Rules = new[]
        {
            new
            {
                Name = "网络错误-游戏更新",
                Enabled = true,
                TemplatePath = "TestGame/images/download.png",
                MatchThreshold = 0.85,
                MeanDiffThreshold = 30.0,
                RegionX1 = 277,
                RegionY1 = 444,
                RegionX2 = 446,
                RegionY2 = 510,
                Actions = new[] { new { CommandType = "Click", PositionX = 359, PositionY = 638 } }
            }
        },
        IdleTickMs = 500
    };

    private static object CurrentPayload() => new
    {
        Schema = "autoflow/rule-script",
        FormatVersion = 2,
        Metadata = new { Name = "网络错误-游戏更新" },
        Rules = new[]
        {
            new
            {
                Name = "网络错误-游戏更新",
                Enabled = true,
                ConditionRef = "TestGame.Download",
                Actions = new[] { new { CommandType = "Click", PositionX = 359, PositionY = 638 } }
            }
        },
        IdleTickMs = 500
    };

    [Fact]
    public void ImportingLegacyTemplateIsRejectedWithPathAndVersion()
    {
        var path = WriteTemplate("legacy", LegacyPayload());
        var service = new RuleTemplateService(_root);

        var ex = Assert.Throws<UnsupportedScriptVersionException>(
            () => service.LoadRulesFromTemplate(path));

        Assert.Equal(1, ex.ActualVersion);
        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void ImportingCurrentTemplateKeepsTheConditionReference()
    {
        var path = WriteTemplate("current", CurrentPayload());
        var service = new RuleTemplateService(_root);

        var rules = service.LoadRulesFromTemplate(path);

        var rule = Assert.Single(rules);
        Assert.Equal("TestGame.Download", rule.ConditionRef);
        Assert.True(rule.UsesSharedCondition);
    }

    [Fact]
    public void LegacyTemplateWouldHaveBecomeAnUnconditionalRuleWithoutTheGate()
    {
        // Documents the failure the gate prevents, by doing exactly what the ungated code did.
        // If this ever stops being true, the gate's justification has changed and the comment
        // above needs revisiting.
        var script = JsonConvert.DeserializeObject<RuleScript>(
            JsonConvert.SerializeObject(LegacyPayload()))!;

        var rule = Assert.Single(script.Rules);

        // 内联字段无处可去，被静默丢弃；剩下一条"无条件"规则——每个 tick 都命中。
        Assert.Null(rule.ConditionRef);
        Assert.False(rule.UsesSharedCondition);
        Assert.Equal(1, script.FormatVersion);
    }

    [Fact]
    public void TemplateIndexStillListsLegacyTemplatesButMarksThemUnusable()
    {
        // 索引故意不跑闸门：资源管理器里看得见的文件如果在库里凭空消失，只会更难排查。
        // 代价是必须当场标明用不了，否则它看起来可用、点了才失败。
        WriteTemplate("legacy", LegacyPayload());
        WriteTemplate("current", CurrentPayload());

        var entries = new TemplateIndex(_root).Build(Game, TemplateKind.Rule, out _);

        Assert.Equal(2, entries.Count);

        var legacy = entries.Single(e => e.FileName == "legacy" + TemplateLayout.RuleExtension);
        Assert.Equal(1, legacy.FormatVersion);
        Assert.False(legacy.IsSupportedVersion);
        Assert.Contains("旧格式", legacy.UnsupportedReason);

        var current = entries.Single(e => e.FileName == "current" + TemplateLayout.RuleExtension);
        Assert.True(current.IsSupportedVersion);
        Assert.Equal("", current.UnsupportedReason);
    }

    [Fact]
    public void IndexEntryExposesTheFileNameSeparatelyFromTheDisplayName()
    {
        // 显示名是中文，文件名是 ASCII slug，两者对不上，所以文件名必须能单独取到。
        var path = WriteTemplate("network_error_game_download", CurrentPayload());

        var entry = Assert.Single(new TemplateIndex(_root).Build(Game, TemplateKind.Rule, out _));

        Assert.Equal("network_error_game_download" + TemplateLayout.RuleExtension, entry.FileName);
        Assert.Equal(path, entry.FilePath);
        Assert.Equal("网络错误-游戏更新", entry.Name);
    }

    [Fact]
    public void ListTemplatesExposesTheFileNameToo()
    {
        WriteTemplate("network_error_game_download", CurrentPayload());

        var info = Assert.Single(new RuleTemplateService(_root).ListTemplates());

        Assert.Equal("network_error_game_download" + TemplateLayout.RuleExtension, info.FileName);
        Assert.Equal("网络错误-游戏更新", info.Name);
    }

    [Fact]
    public void ListTemplatesStillListsLegacyFilesSoTheyCanBeFoundAndFixed()
    {
        // ListTemplates 只读元数据、不加载规则，所以它必须继续列出旧文件——
        // 否则用户既导不进来、也在库里找不到它，无从下手。
        WriteTemplate("legacy", LegacyPayload());

        var info = Assert.Single(new RuleTemplateService(_root).ListTemplates());

        Assert.Equal("legacy" + TemplateLayout.RuleExtension, info.FileName);
    }
}
