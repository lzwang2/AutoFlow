using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Instructions;
using Newtonsoft.Json;
using Xunit;

namespace AutoFlow.Core.Tests;

public class ActionBlockLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "AutoFlowBlocks_" + Guid.NewGuid().ToString("N"));

    private const string Game = "TestGame";

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static ActionBlock SampleBlock() => new()
    {
        Id = "TestGame.ConfirmAndReturn",
        Name = "确认并返回",
        Description = "点击确认，等待，再点返回",
        Steps =
        {
            ActionSpecMapper.ToActionSpec(new Instruction
            {
                CommandType = CommandType.Click,
                PositionX = 368,
                PositionY = 645
            }),
            ActionSpecMapper.ToActionSpec(new Instruction
            {
                CommandType = CommandType.WaitTime,
                WaitTimeMilliseconds = 500
            }),
            ActionSpecMapper.ToActionSpec(new Instruction
            {
                CommandType = CommandType.Click,
                PositionX = 100,
                PositionY = 900
            })
        }
    };

    [Fact]
    public void SaveThenLoad_PreservesBlock()
    {
        var library = new ActionBlockLibrary(_root);
        library.Save(SampleBlock(), Game);

        var reloaded = new ActionBlockLibrary(_root);
        var warnings = reloaded.LoadGame(Game);

        Assert.Empty(warnings);
        Assert.True(reloaded.TryGet("TestGame.ConfirmAndReturn", out var block));
        Assert.Equal("确认并返回", block!.Name);
        Assert.Equal(3, block.Steps.Count);
        Assert.Equal(CommandType.WaitTime, block.Steps[1].Type);
    }

    [Fact]
    public void Expand_TurnsBlockRefIntoInstructionSequence()
    {
        var library = new ActionBlockLibrary(_root);
        library.Save(SampleBlock(), Game);

        var steps = new object[]
        {
            new Instruction { CommandType = CommandType.Comment, CommentContent = "开始" },
            new ActionBlockRef { BlockId = "TestGame.ConfirmAndReturn" },
            new Instruction { CommandType = CommandType.WaitTime, WaitTimeMilliseconds = 100 }
        };

        var expanded = library.Expand(steps, "测试脚本");

        // 1 comment + 3 block steps + 1 wait
        Assert.Equal(5, expanded.Count);
        Assert.Equal(CommandType.Comment, expanded[0].CommandType);
        Assert.Equal(CommandType.Click, expanded[1].CommandType);
        Assert.Equal(368, expanded[1].PositionX);
        Assert.Equal(CommandType.WaitTime, expanded[2].CommandType);
        Assert.Equal(CommandType.Click, expanded[3].CommandType);
        Assert.Equal(CommandType.WaitTime, expanded[4].CommandType);
        Assert.Equal(100, expanded[4].WaitTimeMilliseconds);
    }

    [Fact]
    public void Expand_AcceptsInlineActionSpecs()
    {
        var library = new ActionBlockLibrary(_root);

        var expanded = library.Expand(new object[]
        {
            new ActionSpec { Type = CommandType.WaitTime, Parameters = { ["WaitTimeMilliseconds"] = 250 } }
        }, "测试脚本");

        Assert.Single(expanded);
        Assert.Equal(250, expanded[0].WaitTimeMilliseconds);
    }

    [Fact]
    public void Expand_MissingBlockRef_NamesContextAndId()
    {
        var library = new ActionBlockLibrary(_root);
        library.LoadGame(Game);

        var ex = Assert.Throws<ActionBlockResolutionException>(() =>
            library.Expand(new object[] { new ActionBlockRef { BlockId = "TestGame.Nope" } },
                "脚本 multi_game.rules.json 的规则 '再战确认'"));

        Assert.Contains("multi_game.rules.json", ex.Message);
        Assert.Contains("再战确认", ex.Message);
        Assert.Contains("TestGame.Nope", ex.Message);
    }

    [Fact]
    public void Expand_UnknownStepType_IsRejected()
    {
        var library = new ActionBlockLibrary(_root);

        var ex = Assert.Throws<ActionBlockResolutionException>(() =>
            library.Expand(new object[] { "我不是步骤" }, "测试脚本"));

        Assert.Contains("无法识别的步骤类型", ex.Message);
    }

    [Fact]
    public void ValidateConditionRefs_ReportsOnlyMissingOnes()
    {
        var conditionsDir = ConditionLibrary.DirectoryFor(_root, Game);
        Directory.CreateDirectory(conditionsDir);
        File.WriteAllText(
            Path.Combine(conditionsDir, "present.condition.json"),
            JsonConvert.SerializeObject(new { Id = "TestGame.Present" }));

        var conditions = new ConditionLibrary(_root);
        conditions.LoadGame(Game);

        var block = SampleBlock();
        block.ConditionRefs.Add("TestGame.Present");
        block.ConditionRefs.Add("TestGame.Absent");

        var missing = new ActionBlockLibrary(_root).ValidateConditionRefs(block, conditions);

        Assert.Single(missing);
        Assert.Equal("TestGame.Absent", missing[0]);
    }

    [Fact]
    public void ConditionRefs_StoreIdsNotCopies()
    {
        // NFR-01: storing condition objects would turn reuse back into duplication.
        var property = typeof(ActionBlock).GetProperty(nameof(ActionBlock.ConditionRefs));

        Assert.Equal(typeof(List<string>), property!.PropertyType);
    }

    [Fact]
    public void DuplicateBlockIds_AreReported()
    {
        var dir = ActionBlockLibrary.DirectoryFor(_root, Game);
        Directory.CreateDirectory(dir);
        foreach (var name in new[] { "a", "b" })
        {
            File.WriteAllText(
                Path.Combine(dir, name + ActionBlockLibrary.FileExtension),
                JsonConvert.SerializeObject(new { Id = "TestGame.Dup", Name = name }));
        }

        var warnings = new ActionBlockLibrary(_root).LoadGame(Game);

        Assert.Single(warnings);
        Assert.Contains("Id 重复", warnings[0]);
    }

    [Fact]
    public void BlockWithoutId_IsSkippedWithWarning()
    {
        var dir = ActionBlockLibrary.DirectoryFor(_root, Game);
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "noid" + ActionBlockLibrary.FileExtension),
            JsonConvert.SerializeObject(new { Name = "无 Id" }));

        var library = new ActionBlockLibrary(_root);
        var warnings = library.LoadGame(Game);

        Assert.Single(warnings);
        Assert.Contains("缺少 Id", warnings[0]);
        Assert.Empty(library.All);
    }

    [Fact]
    public void SameBlockUsableByRuleAndLinearScript()
    {
        // AC-02: one block, two consumers, no duplication.
        var library = new ActionBlockLibrary(_root);
        library.Save(SampleBlock(), Game);

        var asRuleActions = library.Expand(
            new object[] { new ActionBlockRef { BlockId = "TestGame.ConfirmAndReturn" } },
            "规则 '再战确认'");

        var asLinearSteps = library.Expand(
            new object[] { new ActionBlockRef { BlockId = "TestGame.ConfirmAndReturn" } },
            "线性脚本 step 3");

        Assert.Equal(3, asRuleActions.Count);
        Assert.Equal(3, asLinearSteps.Count);
        Assert.Equal(asRuleActions[0].PositionX, asLinearSteps[0].PositionX);
        Assert.Single(library.All);
    }
}
