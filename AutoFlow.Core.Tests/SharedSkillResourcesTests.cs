using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Handlers;
using AutoFlow.Core.Scripting.Instructions;
using AutoFlow.Core.Scripting.Rules;
using Newtonsoft.Json;
using Xunit;
using AutoFlow.Core.Skills;
using AutoFlow.Core.Skills.LastCloudia.MultiPlayerCore;

namespace AutoFlow.Core.Tests;

public sealed class SharedSkillResourcesTests : IDisposable
{
    private const string BlockId = "LastCloudia.ConfirmMultiClearReward";
    private const string SkillName = "LastCloudia.MultiPlayerCore";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "AutoFlowShared_" + Guid.NewGuid().ToString("N"));

    private static ScriptExecutionContext Context() => new()
    { Capture = null!, Input = null!, CoordinateTransform = null!, WindowInfo = null!, TemplateMatch = null! };

    private sealed class RecordingClick(Action<Instruction> record) : IInstructionHandler
    {
        public CommandType CommandType => CommandType.Click;
        public Task ExecuteAsync(Instruction instruction, ScriptExecutionContext context, CancellationToken ct)
        { record(instruction); return Task.CompletedTask; }
    }

    public SharedSkillResourcesTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    private static string TemplateRoot => Path.Combine(AppContext.BaseDirectory, "RuleTemplates");
    private static readonly ExpandedStep[] SkillCall = [new()
    { Instruction = new() { CommandType = CommandType.CallSkill, SkillName = SkillName }, Origin = "多人游戏" }];

    [Fact]
    public void PresetAndSkillResolveTheSameActionAndSnapshotsSurviveEditing()
    {
        var conditions = new ConditionLibrary(TemplateRoot);
        var blocks = new ActionBlockLibrary(TemplateRoot);
        Assert.Empty(conditions.LoadGame("LastCloudia"));
        Assert.Empty(blocks.LoadGame("LastCloudia"));
        var resources = new SharedScriptResources(conditions, blocks, InstructionHandlerRegistry.CreateDefault());
        resources.ValidateSkillDependencies(SkillCall);
        var script = new RuleScriptService().Load(Path.Combine(TemplateRoot, "LastCloudia", "Rules", "multi_clear_reward.rules.json"));
        var rule = Assert.Single(script.Rules);
        Assert.Empty(rule.Actions);
        Assert.Equal("LastCloudia.MultiClearReward", rule.ConditionRef);
        Assert.Equal(BlockId, Assert.Single(rule.Steps).BlockRef!.BlockId);
        var fromRule = Assert.Single(new ScriptStepExpander(conditions, blocks).Expand(rule.Steps, "test", rule.Name)).Instruction;
        var fromSkill = Assert.Single(resources.ResolveBlock(BlockId, SkillName)).Instruction;
        Assert.Equal(JsonConvert.SerializeObject(fromRule), JsonConvert.SerializeObject(fromSkill));
        Assert.Equal(381, fromSkill.PositionX);
        Assert.Equal(940, fromSkill.PositionY);
        Assert.Equal(800, fromSkill.IntervalToNextInstructionMs);

        blocks.Require(BlockId, "editor").Steps[0].Parameters["PositionX"] = 400;
        blocks.Require(BlockId, "editor").Steps[0].Parameters["IntervalToNextInstructionMs"] = 900;
        var nextRun = new SharedScriptResources(conditions, blocks, InstructionHandlerRegistry.CreateDefault());
        Assert.Equal(381, Assert.Single(resources.ResolveBlock(BlockId, SkillName)).Instruction.PositionX);
        var changed = Assert.Single(nextRun.ResolveBlock(BlockId, SkillName)).Instruction;
        var changedRule = Assert.Single(new ScriptStepExpander(conditions, blocks).Expand(rule.Steps, "test", rule.Name)).Instruction;
        Assert.Equal(400, changed.PositionX);
        Assert.Equal(900, changed.IntervalToNextInstructionMs);
        Assert.Equal(JsonConvert.SerializeObject(changedRule), JsonConvert.SerializeObject(changed));
    }

    [Fact]
    public async Task ClickRunsImmediatelyAndItsFollowingDelayCanBeCancelled()
    {
        var blocks = new ActionBlockLibrary(_root);
        blocks.Save(new ActionBlock { Id = BlockId, Steps = [new ActionSpec
        { Type = CommandType.Click, Parameters = new() { ["PositionX"] = 381, ["PositionY"] = 940, ["IntervalToNextInstructionMs"] = 800 } }] }, "LastCloudia");
        using var cancellation = new CancellationTokenSource();
        var clicks = 0;
        var registry = new InstructionHandlerRegistry();
        registry.Register(new RecordingClick(instruction => { clicks++; cancellation.Cancel(); }));
        var context = Context();
        context.JumpToIndex = 7;
        var resources = new SharedScriptResources(null, blocks, registry);
        var running = resources.ExecuteBlockAsync(BlockId, SkillName, context, cancellation.Token);
        // No pre-click await: the handler ran synchronously, and cancellation reaches the post-click delay.
        Assert.Equal(1, clicks);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Null(context.CurrentCondition);
        Assert.Equal(7, context.JumpToIndex);
    }

    public static IEnumerable<object[]> SharedBranches => new MultiPlayerCoreSkill().SharedResources
        .Select(b => new object[] { b.ConditionId, b.ActionBlockId });

    [Theory]
    [MemberData(nameof(SharedBranches))]
    public void EveryMissingSkillDependencyIsReported(string conditionId, string blockId)
    {
        var conditions = new ConditionLibrary(_root);
        var blocks = new ActionBlockLibrary(_root);
        foreach (var branch in new MultiPlayerCoreSkill().SharedResources)
        {
            var image = Path.Combine(_root, branch.ConditionId + ".png");
            File.WriteAllText(image, "fixture");
            conditions.Save(new VisualConditionSpec { Id = branch.ConditionId, TemplatePath = image }, "LastCloudia");
            blocks.Save(new ActionBlock { Id = branch.ActionBlockId, Steps = [new ActionSpec { Type = CommandType.Click }] }, "LastCloudia");
        }
        foreach (var dependency in new MultiPlayerCoreSkill().SharedConditions)
        {
            var image = Path.Combine(_root, dependency.ConditionId + ".png");
            File.WriteAllText(image, "fixture");
            conditions.Save(new VisualConditionSpec { Id = dependency.ConditionId, TemplatePath = image }, "LastCloudia");
        }
        SharedScriptResources Resources() => new(conditions, blocks, InstructionHandlerRegistry.CreateDefault());
        Resources().ValidateSkillDependencies(SkillCall);
        var condition = conditions.Require(conditionId, "test", "branch");
        conditions.Delete(conditionId);
        var missingCondition = Assert.Throws<ConditionResolutionException>(() => Resources().ValidateSkillDependencies(SkillCall));
        Assert.Contains(SkillName, missingCondition.Message);
        Assert.Contains(conditionId, missingCondition.Message);
        conditions.Save(condition, "LastCloudia");
        var block = blocks.Require(blockId, "test");
        blocks.Delete(blockId);
        var missingBlock = Assert.Throws<ActionBlockResolutionException>(() => Resources().ValidateSkillDependencies(SkillCall));
        Assert.Contains(SkillName, missingBlock.Message);
        Assert.Contains(blockId, missingBlock.Message);
        blocks.Save(block, "LastCloudia");
        File.Delete(condition.TemplatePath!);
        var missingImage = Assert.Throws<FileNotFoundException>(() => Resources().ValidateSkillDependencies(SkillCall));
        Assert.Contains(SkillName, missingImage.Message);
        Assert.Contains(conditionId, missingImage.Message);
    }

    [Fact]
    public void AllMigratedRulesUseTheSkillsSharedDefinitions()
    {
        var skill = new MultiPlayerCoreSkill();
        Assert.Equal(11, skill.SharedResources.Count);
        var conditions = new ConditionLibrary(TemplateRoot);
        var blocks = new ActionBlockLibrary(TemplateRoot);
        Assert.Empty(conditions.LoadGame("LastCloudia"));
        Assert.Empty(blocks.LoadGame("LastCloudia"));
        var resources = new SharedScriptResources(conditions, blocks, InstructionHandlerRegistry.CreateDefault());
        resources.ValidateSkillDependencies(SkillCall);
        var rules = Directory.GetFiles(Path.Combine(TemplateRoot, "LastCloudia", "Rules"), "*.rules.json")
            .SelectMany(p => new RuleScriptService().Load(p).Rules).ToList();
        foreach (var branch in skill.SharedResources)
        {
            var rule = Assert.Single(rules, r => r.ConditionRef == branch.ConditionId);
            Assert.Empty(rule.Actions);
            Assert.Equal(branch.ActionBlockId, Assert.Single(rule.Steps).BlockRef!.BlockId);
            var ruleActions = new ScriptStepExpander(conditions, blocks).Expand(rule.Steps, "test", rule.Name);
            var skillActions = resources.ResolveBlock(branch.ActionBlockId, SkillName);
            Assert.Equal(ruleActions.Select(s => JsonConvert.SerializeObject(s.Instruction)),
                skillActions.Select(s => JsonConvert.SerializeObject(s.Instruction)));
        }
    }

    [Theory]
    [InlineData("LastCloudia.Emoji", "LastCloudia/images/emoji.png", 481, 849, 606, 956)]
    [InlineData("LastCloudia.BattleLastResult", "LastCloudia/images/battle_last_result.png", 100, 900, 625, 945)]
    public void SkillBranchesUseTheSharedConditionLibrary(
        string conditionId, string templatePath, int x1, int y1, int x2, int y2)
    {
        var skill = new MultiPlayerCoreSkill();
        Assert.Contains(skill.SharedConditions, dependency => dependency.ConditionId == conditionId);

        var conditions = new ConditionLibrary(TemplateRoot);
        Assert.Empty(conditions.LoadGame("LastCloudia"));
        var condition = conditions.Require(conditionId, skill.Name, "共享条件");
        Assert.Equal(templatePath, condition.TemplatePath);
        Assert.True(File.Exists(condition.EffectiveTemplatePath));
        Assert.Equal((x1, y1, x2, y2),
            (condition.RegionX1, condition.RegionY1, condition.RegionX2, condition.RegionY2));
    }

    [Theory]
    [InlineData("LastCloudia.Emoji", false)]
    [InlineData("LastCloudia.Emoji", true)]
    [InlineData("LastCloudia.BattleLastResult", false)]
    [InlineData("LastCloudia.BattleLastResult", true)]
    public void MissingSharedConditionOrImageFailsBeforeExecution(string conditionId, bool missingImage)
    {
        var source = new ConditionLibrary(TemplateRoot);
        Assert.Empty(source.LoadGame("LastCloudia"));
        var conditions = new ConditionLibrary(_root);
        foreach (var condition in source.All)
        {
            var copy = GameTaskScript.Copy(condition);
            copy.TemplatePath = condition.EffectiveTemplatePath;
            conditions.Save(copy, "LastCloudia");
        }
        var blocks = new ActionBlockLibrary(TemplateRoot);
        Assert.Empty(blocks.LoadGame("LastCloudia"));
        if (missingImage)
            conditions.Require(conditionId, SkillName, "test").ResolvedTemplatePath = Path.Combine(_root, "missing.png");
        else
            conditions.Delete(conditionId);
        var resources = new SharedScriptResources(conditions, blocks, InstructionHandlerRegistry.CreateDefault());
        var exception = Record.Exception(() => resources.ValidateSkillDependencies(SkillCall));
        Assert.NotNull(exception);
        if (missingImage) Assert.IsType<FileNotFoundException>(exception);
        else Assert.IsType<ConditionResolutionException>(exception);
        Assert.Contains(SkillName, exception.Message);
        Assert.Contains(conditionId, exception.Message);
    }

    [Theory]
    [InlineData("LastCloudia.AbandonTask")]
    [InlineData("LastCloudia.ServerConnectionFailRetreat")]
    [InlineData("LastCloudia.ServerConnectionFailReturn")]
    [InlineData("LastCloudia.ServerConnectionFailMultiplayer")]
    [InlineData("LastCloudia.NetworkError")]
    public async Task ErrorConfirmationResetsStateOnlyAfterSuccessfulAction(string conditionId)
    {
        var state = new MultiPlayerCoreState { EmojiSent = true, InRoomCount = 39 };
        var skill = new MultiPlayerCoreSkill(state);
        var branch = Assert.Single(skill.SharedResources, b => b.ConditionId == conditionId);
        var blocks = new ActionBlockLibrary(_root);
        blocks.Save(new ActionBlock { Id = branch.ActionBlockId, Steps = [new ActionSpec
        { Type = CommandType.Click, Parameters = new() { ["IntervalToNextInstructionMs"] = 0 } }] }, "LastCloudia");
        var clicks = 0;
        var registry = new InstructionHandlerRegistry();
        registry.Register(new RecordingClick(_ => clicks++));
        var context = Context();
        context.SharedResources = new SharedScriptResources(null, blocks, registry);
        await skill.ExecuteSharedActionAsync(branch, context, default, resetState: true);
        Assert.Equal(1, clicks);
        Assert.False(state.EmojiSent);
        Assert.Equal(0, state.InRoomCount);

        state.EmojiSent = true;
        state.InRoomCount = 39;
        using var cancellation = new CancellationTokenSource();
        registry.Register(new RecordingClick(_ => cancellation.Cancel()));
        blocks.Require(branch.ActionBlockId, "test").Steps[0].Parameters["IntervalToNextInstructionMs"] = 1000;
        context.SharedResources = new SharedScriptResources(null, blocks, registry);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => skill.ExecuteSharedActionAsync(branch, context, cancellation.Token, resetState: true));
        Assert.True(state.EmojiSent);
        Assert.Equal(39, state.InRoomCount);
        // Next run explicitly clears state even if the preceding confirmation was cancelled.
        skill.ResetForRun();
        Assert.False(state.EmojiSent);
        Assert.Equal(0, state.InRoomCount);
    }
}
