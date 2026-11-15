using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Instructions;
using AutoFlow.Core.Scripting.Rules;
using Newtonsoft.Json;
using Xunit;

namespace AutoFlow.Core.Tests;

public sealed class GameTaskScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AutoFlowTaskTests_" + Guid.NewGuid().ToString("N"));

    public GameTaskScriptTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    private string Write(string name, string content)
    {
        var path = Path.Combine(_root, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Schema\":\"autoflow/script\",\"FormatVersion\":2,\"Rules\":[{}],\"OuterWidth\":750,\"OuterHeight\":1040}")]
    [InlineData("{\"Schema\":\"autoflow/rule-script\",\"FormatVersion\":1,\"Rules\":[{}],\"OuterWidth\":750,\"OuterHeight\":1040}")]
    [InlineData("{\"Schema\":\"autoflow/rule-script\",\"FormatVersion\":2,\"Rules\":[],\"OuterWidth\":750,\"OuterHeight\":1040}")]
    [InlineData("{\"Schema\":\"autoflow/rule-script\",\"FormatVersion\":2,\"Rules\":[{}],\"OuterWidth\":0,\"OuterHeight\":1040}")]
    public void RejectsIncompleteOrIncompatiblePreset(string json)
        => Assert.Throws<InvalidOperationException>(() => GameTaskScript.Load(Write("invalid.rules.json", json)));

    [Fact]
    public void CompleteScriptRoundTripAndCopiesAreIndependent()
    {
        var script = new RuleScript
        {
            IdleTickMs = 777, OuterWidth = 750, OuterHeight = 1040,
            Metadata = new() { Name = "多人游戏", Description = "完整流程", ScriptVersion = "3.2" },
            Rules = [new Rule { Name = "兜底", Actions = [new Instruction
            {
                CommandType = CommandType.CallSkill, SkillName = "LastCloudia.MultiPlayerCore",
                SkillParameters = new() { ["is_room_owner"] = false }
            }] }]
        };
        var source = Write("source.rules.json", JsonConvert.SerializeObject(script));
        var loaded = GameTaskScript.Load(source);
        var edited = GameTaskScript.Copy(loaded);
        var running = GameTaskScript.Copy(loaded);
        edited.Rules[0].Actions[0].SkillParameters!["is_room_owner"] = true;
        edited.Rules[0].Enabled = false;
        edited.Metadata.Description = "edited";
        Assert.True(running.Rules[0].Enabled);
        Assert.Equal(false, running.Rules[0].Actions[0].SkillParameters!["is_room_owner"]);
        Assert.Equal("完整流程", running.Metadata.Description);
        var exported = Path.Combine(_root, "elsewhere.rules.json");
        new RuleScriptService().Save(loaded, exported);
        Assert.Equal(JsonConvert.SerializeObject(loaded), JsonConvert.SerializeObject(GameTaskScript.Load(exported)));
        Assert.Equal(777, loaded.IdleTickMs);
        Assert.Equal(JsonConvert.SerializeObject(script), JsonConvert.SerializeObject(GameTaskScript.Load(source)));
    }

    [Fact]
    public void ConditionSnapshotRetainsResolvedPathAndSurvivesEditorMutation()
    {
        var image = Write("LastCloudia/Conditions/pixel.png", "fixture");
        Write("LastCloudia/Conditions/pixel.condition.json", JsonConvert.SerializeObject(new VisualConditionSpec
        { Id = "LastCloudia.Pixel", TemplatePath = "pixel.png", MatchThreshold = 0.9 }));
        var library = new ConditionLibrary(_root);
        Assert.Empty(library.LoadGame("LastCloudia"));
        var snapshot = library.Snapshot();
        library.Require("LastCloudia.Pixel", "test", "rule").MatchThreshold = 0.2;
        library.Clear();
        var condition = snapshot.Require("LastCloudia.Pixel", "test", "rule");
        Assert.Equal(0.9, condition.MatchThreshold);
        Assert.Equal(Path.GetFullPath(image), condition.EffectiveTemplatePath);
        var script = new RuleScript { Rules = [new Rule { ConditionRef = condition.Id }] };
        GameTaskScript.Validate(script, snapshot, new ActionBlockLibrary(_root), _root);
        File.Delete(image);
        Assert.Throws<FileNotFoundException>(() => GameTaskScript.Validate(script, snapshot, new ActionBlockLibrary(_root), _root));
    }

    [Fact]
    public void ActionSnapshotRetainsNestedParametersAfterEditorClear()
    {
        Write("LastCloudia/Actions/wait.actionblock.json", JsonConvert.SerializeObject(new ActionBlock
        { Id = "Wait", Steps = [new ActionSpec { Type = CommandType.WaitTime, Parameters = new() { ["WaitTimeMilliseconds"] = 1234 } }] }));
        var library = new ActionBlockLibrary(_root);
        Assert.Empty(library.LoadGame("LastCloudia"));
        var snapshot = library.Snapshot();
        library.Require("Wait", "test").Steps[0].Parameters["WaitTimeMilliseconds"] = 1;
        library.Clear();
        Assert.Equal("1234", snapshot.Require("Wait", "test").Steps[0].Parameters["WaitTimeMilliseconds"]?.ToString());
    }

    [Fact]
    public void RejectsMissingReferencesSkillsAndSkillImages()
    {
        var script = new RuleScript { Rules = [new Rule { ConditionRef = "missing" }] };
        var conditions = new ConditionLibrary(_root);
        var blocks = new ActionBlockLibrary(_root);
        Assert.Throws<ConditionResolutionException>(() => GameTaskScript.Validate(script, conditions, blocks, _root));
        script.Rules[0].ConditionRef = null;
        script.Rules[0].Steps = [ScriptStep.FromBlock("missing")];
        Assert.Throws<ActionBlockResolutionException>(() => GameTaskScript.Validate(script, conditions, blocks, _root));
        script.Rules[0].Steps.Clear();
        script.Rules[0].Actions = [new Instruction { CommandType = CommandType.CallSkill, SkillName = "unknown" }];
        Assert.Throws<InvalidOperationException>(() => GameTaskScript.Validate(script, conditions, blocks, _root));
        script.Rules[0].Actions[0].SkillName = "LastCloudia.MultiPlayerCore";
        var ex = Assert.Throws<FileNotFoundException>(() => GameTaskScript.Validate(script, conditions, blocks, _root));
        Assert.Contains("技能图片缺失", ex.Message);
    }

    [Fact]
    public void GateHasOneOwnerUntilCleanupReleasesIt()
    {
        var gate = new ExecutionGate();
        var winners = 0;
        Parallel.For(0, 100, _ => { if (gate.TryEnter()) Interlocked.Increment(ref winners); });
        Assert.Equal(1, winners);
        Assert.False(gate.TryEnter());
        gate.Exit();
        Assert.True(gate.TryEnter());
    }
}
