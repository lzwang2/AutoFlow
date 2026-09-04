using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Instructions;
using AutoFlow.Core.Scripting.Rules;
using Newtonsoft.Json;
using Xunit;

namespace AutoFlow.Core.Tests;

/// <summary>
/// Phase 5: rules referencing shared conditions and action blocks. The dispatch loop itself
/// needs a live capture source, so these cover the model and the resolution rules — the parts
/// that decide whether a rule matches at all.
/// </summary>
public class RuleConditionRefTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "af_rulecond_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string WriteCondition(string game, string id, Action<VisualConditionSpec>? tweak = null)
    {
        var spec = new VisualConditionSpec
        {
            Id = id,
            Name = id,
            TemplatePath = $"{game}/images/x.png",
            MatchThreshold = 0.9
        };
        tweak?.Invoke(spec);
        return new ConditionLibrary(_root).Save(spec, game);
    }

    [Fact]
    public void RuleWithoutConditionRefIsUnconditional()
    {
        // 没有 ConditionRef 的规则就是兜底规则——内联条件字段已经不存在了，
        // 一条规则要么引用共享条件，要么无条件命中。
        var rule = new Rule { Name = "兜底" };

        Assert.False(rule.UsesSharedCondition);
    }

    [Fact]
    public void RuleWithConditionRefReportsSharedCondition()
    {
        var rule = new Rule { Name = "共享", ConditionRef = "LastCloudia.UnableToJoin" };

        Assert.True(rule.UsesSharedCondition);
    }

    [Fact]
    public void BlankConditionRefIsNotASharedCondition()
    {
        // 空白引用必须等同于"没有引用"，否则会被当成一个 Id 去查库然后报缺失。
        Assert.False(new Rule { ConditionRef = "   " }.UsesSharedCondition);
        Assert.False(new Rule { ConditionRef = "" }.UsesSharedCondition);
    }

    [Fact]
    public void CatchAllRuleHasNoConditionRef()
    {
        // MultiPlayerGame.rules.json 依赖这种规则永远命中。
        var rule = new Rule { Name = "兜底" };

        Assert.False(rule.UsesSharedCondition);
        Assert.Null(rule.ConditionRef);
    }

    [Fact]
    public void ConditionRefResolvesFromLibrary()
    {
        WriteCondition("LastCloudia", "LastCloudia.UnableToJoin");
        var library = new ConditionLibrary(_root);
        library.LoadGame("LastCloudia");

        var resolved = library.Require("LastCloudia.UnableToJoin", "脚本", "规则 #0");

        Assert.Equal(0.9, resolved.MatchThreshold);
    }

    [Fact]
    public void MissingConditionRefNamesScriptAndRule()
    {
        var library = new ConditionLibrary(_root);
        library.LoadGame("LastCloudia");

        var ex = Assert.Throws<ConditionResolutionException>(
            () => library.Require("LastCloudia.Nope", "MultiPlayerGame", "规则 #3 [重试]"));

        Assert.Contains("LastCloudia.Nope", ex.Message);
        Assert.Contains("MultiPlayerGame", ex.Message);
        Assert.Contains("规则 #3", ex.Message);
    }

    [Fact]
    public void RuleStepsExpandActionBlocksInPlace()
    {
        var blockDir = Path.Combine(_root, "LastCloudia", "Actions");
        Directory.CreateDirectory(blockDir);
        var block = new ActionBlock
        {
            Id = "LastCloudia.Retry",
            Name = "重试",
            Steps =
            {
                new ActionSpec { Type = CommandType.Click },
                new ActionSpec { Type = CommandType.WaitTime }
            }
        };
        File.WriteAllText(
            Path.Combine(blockDir, "LastCloudia_Retry.actionblock.json"),
            JsonConvert.SerializeObject(block));

        var blocks = new ActionBlockLibrary(_root);
        blocks.LoadGame("LastCloudia");

        var steps = new List<ScriptStep>
        {
            ScriptStep.FromAction(new ActionSpec { Type = CommandType.Comment }),
            ScriptStep.FromBlock("LastCloudia.Retry"),
            ScriptStep.FromAction(new ActionSpec { Type = CommandType.WaitTime })
        };

        var expanded = new ScriptStepExpander(null, blocks).Expand(steps, "脚本", "规则 #0 [测试]");

        // 顺序必须是 Comment, Click, WaitTime(块内), WaitTime(块后)
        Assert.Equal(4, expanded.Count);
        Assert.Equal(CommandType.Comment, expanded[0].Instruction.CommandType);
        Assert.Equal(CommandType.Click, expanded[1].Instruction.CommandType);
        Assert.Equal(CommandType.WaitTime, expanded[2].Instruction.CommandType);
        Assert.Equal(CommandType.WaitTime, expanded[3].Instruction.CommandType);
        Assert.Contains("动作块", expanded[1].Origin);
        Assert.Contains("规则 #0", expanded[0].Origin);
    }

    [Fact]
    public void MissingActionBlockNamesTheRule()
    {
        var blocks = new ActionBlockLibrary(_root);
        blocks.LoadGame("LastCloudia");

        var steps = new List<ScriptStep> { ScriptStep.FromBlock("LastCloudia.Gone") };

        var ex = Assert.Throws<ActionBlockResolutionException>(
            () => new ScriptStepExpander(null, blocks).Expand(steps, "脚本", "规则 #2 [撤退]"));

        Assert.Contains("LastCloudia.Gone", ex.Message);
        Assert.Contains("规则 #2", ex.Message);
    }

    [Fact]
    public void RuleRoundTripsConditionRefAndSteps()
    {
        var rule = new Rule
        {
            Id = "LastCloudia.RetryRule",
            Name = "重试",
            ConditionRef = "LastCloudia.UnableToJoin",
            Steps = { ScriptStep.FromBlock("LastCloudia.Retry") }
        };
        var script = new RuleScript { Rules = { rule } };

        var restored = JsonConvert.DeserializeObject<RuleScript>(JsonConvert.SerializeObject(script))!;

        Assert.Equal("LastCloudia.RetryRule", restored.Rules[0].Id);
        Assert.Equal("LastCloudia.UnableToJoin", restored.Rules[0].ConditionRef);
        Assert.True(restored.Rules[0].UsesSharedCondition);
        Assert.Single(restored.Rules[0].Steps);
        Assert.Equal("LastCloudia.Retry", restored.Rules[0].Steps[0].BlockRef!.BlockId);
    }

    [Fact]
    public void LegacyInlineConditionFieldsAreIgnoredNotFatal()
    {
        // v1 文件的内联条件字段现在是未知属性。反序列化必须安静地忽略它们，把"这是旧格式"
        // 的判断交给版本闸门（ScriptFormatVersion）——否则用户看到的是 JSON 解析异常，
        // 而不是"请先迁移"这句有用的提示。
        const string json = """
        {
          "Schema": "autoflow/rule-script",
          "FormatVersion": 1,
          "Rules": [
            { "Name": "无法加入房间", "TemplatePath": "LastCloudia/images/unable_to_join.png",
              "MatchThreshold": 0.9, "RegionX1": 285, "RegionY1": 320,
              "Actions": [ { "CommandType": "Click", "X": 10, "Y": 20 } ] }
          ]
        }
        """;

        var script = JsonConvert.DeserializeObject<RuleScript>(json)!;
        var rule = script.Rules[0];

        Assert.Null(rule.Id);
        Assert.Null(rule.ConditionRef);
        Assert.False(rule.UsesSharedCondition);
        Assert.Empty(rule.Steps);
        Assert.Single(rule.Actions);
        Assert.Equal("无法加入房间", rule.Name);
    }

    [Fact]
    public void LegacyFormatVersionIsRejectedWithMigrationHint()
    {
        // 上一个测试保证 v1 文件不会炸在 JSON 层，这个保证它确实被拦住了——
        // 两者合起来才是"安静忽略字段，但明确拒绝加载"。
        var ex = Assert.Throws<UnsupportedScriptVersionException>(
            () => ScriptFormatVersion.Validate(1, "MultiPlayerGame.rules.json"));

        Assert.Equal(1, ex.ActualVersion);
        Assert.Contains("MultiPlayerGame.rules.json", ex.Message);
        Assert.Contains("迁移", ex.Message);
    }
}
