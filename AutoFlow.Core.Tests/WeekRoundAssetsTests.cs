using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Instructions;
using Xunit;

namespace AutoFlow.Core.Tests;

public sealed class WeekRoundAssetsTests
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "RuleTemplates");

    [Fact]
    public void PresetContainsOneWeekRoundCallAndValidatesItsIndirectDependencies()
    {
        var path = Path.Combine(Root, "LastCloudia", "Scripts", "WeekRound.json");
        var script = GameTaskScript.LoadLinear(path);
        Assert.Equal((750, 1040), (script.OuterWidth, script.OuterHeight));
        Assert.Empty(script.Instructions);
        var action = Assert.Single(script.Steps).Action!;
        Assert.Equal(CommandType.CallSkill, action.Type);
        Assert.Equal("LastCloudia.WeekRound", action.Parameters["SkillName"]);
        Assert.False(action.Parameters.ContainsKey("SkillParameters"));

        var conditions = new ConditionLibrary(Root);
        var blocks = new ActionBlockLibrary(Root);
        Assert.Empty(conditions.LoadGame("LastCloudia"));
        Assert.Empty(blocks.LoadGame("LastCloudia"));
        GameTaskScript.Validate(script, conditions, blocks, AppContext.BaseDirectory);
    }

    [Theory]
    [InlineData("LastCloudia.BattleAgain", 130, 600, 600, 670)]
    [InlineData("LastCloudia.BattleEvaluateReward", 335, 690, 390, 710)]
    public void NewConditionsUseTheAhkRegions(string id, int x1, int y1, int x2, int y2)
    {
        var library = new ConditionLibrary(Root);
        Assert.Empty(library.LoadGame("LastCloudia"));
        var condition = library.Require(id, "test", "test");
        Assert.Equal((x1, y1, x2, y2),
            (condition.RegionX1, condition.RegionY1, condition.RegionX2, condition.RegionY2));
        Assert.Equal((750, 1040), (condition.ReferenceOuterWidth, condition.ReferenceOuterHeight));
        Assert.True(File.Exists(condition.EffectiveTemplatePath));
    }

    [Theory]
    [InlineData("LastCloudia.ConfirmBattleAgainYes", 490, 640)]
    [InlineData("LastCloudia.ConfirmBattleAgainNo", 235, 635)]
    [InlineData("LastCloudia.ConfirmBattleEvaluateReward", 370, 695)]
    public void NewActionBlocksKeepTheAhkCoordinates(string id, int x, int y)
    {
        var library = new ActionBlockLibrary(Root);
        Assert.Empty(library.LoadGame("LastCloudia"));
        var instruction = ActionSpecMapper.ToInstruction(Assert.Single(library.Require(id, "test").Steps));
        Assert.Equal(CommandType.Click, instruction.CommandType);
        Assert.Equal((x, y), ((int)instruction.PositionX, (int)instruction.PositionY));
        Assert.Equal(800, instruction.IntervalToNextInstructionMs);
    }

    [Fact]
    public void TopLevelWeekRoundCallPreflightsIndirectSinglePlayerResourcesAndFiles()
    {
        var script = GameTaskScript.LoadLinear(Path.Combine(Root, "LastCloudia", "Scripts", "WeekRound.json"));
        var sourceConditions = new ConditionLibrary(Root);
        var blocks = new ActionBlockLibrary(Root);
        Assert.Empty(sourceConditions.LoadGame("LastCloudia"));
        Assert.Empty(blocks.LoadGame("LastCloudia"));

        var tempRoot = Path.Combine(Path.GetTempPath(), "AutoFlowWeekRound_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var incomplete = new ConditionLibrary(tempRoot);
            foreach (var condition in sourceConditions.All.Where(c => c.Id != "LastCloudia.BattleAgain"))
            {
                var copy = GameTaskScript.Copy(condition);
                copy.TemplatePath = condition.EffectiveTemplatePath;
                incomplete.Save(copy, "LastCloudia");
            }

            var conditionError = Assert.Throws<ConditionResolutionException>(() =>
                GameTaskScript.Validate(script, incomplete, blocks, AppContext.BaseDirectory));
            Assert.Contains("LastCloudia.WeekRound", conditionError.Message);
            Assert.Contains("LastCloudia.BattleAgain", conditionError.Message);

            var fileError = Assert.Throws<FileNotFoundException>(() =>
                GameTaskScript.Validate(script, sourceConditions, blocks, tempRoot));
            Assert.Contains("LastCloudia.WeekRound", fileError.Message);
            Assert.Contains(Path.Combine("Skills", "LastCloudia", "Common", "images"), fileError.Message);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
