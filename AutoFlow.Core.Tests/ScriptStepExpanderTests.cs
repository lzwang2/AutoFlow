using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Instructions;
using Xunit;

namespace AutoFlow.Core.Tests;

public class ScriptStepExpanderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "AutoFlowTests_" + Guid.NewGuid().ToString("N"));

    private const string Game = "TestGame";

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static ActionSpec Click(double x, double y) => new()
    {
        Type = CommandType.Click,
        Parameters = { ["PositionX"] = x, ["PositionY"] = y }
    };

    private static ActionSpec WaitTime(int ms) => new()
    {
        Type = CommandType.WaitTime,
        Parameters = { ["WaitTimeMilliseconds"] = ms }
    };

    private ConditionLibrary LibraryWith(params VisualConditionSpec[] conditions)
    {
        var library = new ConditionLibrary(_root);
        foreach (var condition in conditions)
            library.Save(condition, Game);
        return library;
    }

    private ActionBlockLibrary BlocksWith(params ActionBlock[] blocks)
    {
        var library = new ActionBlockLibrary(_root);
        foreach (var block in blocks)
            library.Save(block, Game);
        return library;
    }

    [Fact]
    public void EmptyStepsFallsBackToInstructions()
    {
        // 纯 Instructions 写法的脚本不写 Steps 也要能跑。
        var document = new ScriptDocument
        {
            Instructions =
            {
                new Instruction { CommandType = CommandType.Click },
                new Instruction { CommandType = CommandType.WaitTime }
            }
        };

        var steps = new ScriptStepExpander().Expand(document, "s");

        Assert.Equal(2, steps.Count);
        Assert.Equal(CommandType.Click, steps[0].Instruction.CommandType);
        Assert.All(steps, s => Assert.Null(s.Condition));
    }

    [Fact]
    public void InlineActionsBecomeInstructionsInOrder()
    {
        var document = new ScriptDocument
        {
            Steps =
            {
                ScriptStep.FromAction(Click(10, 20)),
                ScriptStep.FromAction(WaitTime(300)),
                ScriptStep.FromAction(Click(30, 40))
            }
        };

        var steps = new ScriptStepExpander().Expand(document, "s");

        Assert.Equal(3, steps.Count);
        Assert.Equal(10, steps[0].Instruction.PositionX);
        Assert.Equal(300, steps[1].Instruction.WaitTimeMilliseconds);
        Assert.Equal(30, steps[2].Instruction.PositionX);
    }

    [Fact]
    public void BlockReferenceExpandsInPlacePreservingOrder()
    {
        var blocks = BlocksWith(new ActionBlock
        {
            Id = "TestGame.CloseAndRetry",
            Steps = { Click(1, 1), WaitTime(300), Click(2, 2) }
        });

        var document = new ScriptDocument
        {
            Steps =
            {
                ScriptStep.FromAction(Click(99, 99)),
                ScriptStep.FromBlock("TestGame.CloseAndRetry"),
                ScriptStep.FromAction(WaitTime(50))
            }
        };

        var steps = new ScriptStepExpander(blocks: blocks).Expand(document, "s");

        // 块引用就地展开：1 + 3 + 1 = 5，且顺序不能乱。
        Assert.Equal(5, steps.Count);
        Assert.Equal(99, steps[0].Instruction.PositionX);
        Assert.Equal(1, steps[1].Instruction.PositionX);
        Assert.Equal(300, steps[2].Instruction.WaitTimeMilliseconds);
        Assert.Equal(2, steps[3].Instruction.PositionX);
        Assert.Equal(50, steps[4].Instruction.WaitTimeMilliseconds);
    }

    [Fact]
    public void ConditionRefIsResolvedAndAttached()
    {
        var condition = new VisualConditionSpec
        {
            Id = "TestGame.UnableToJoin",
            TemplatePath = $"{Game}/images/x.png",
            RegionX1 = 285, RegionY1 = 320, RegionX2 = 440, RegionY2 = 340
        };

        var waitImage = new ActionSpec
        {
            Type = CommandType.WaitImage,
            Parameters =
            {
                [ActionSpecMapper.ConditionRefKey] = "TestGame.UnableToJoin",
                ["WaitTimeoutMs"] = 30000
            }
        };

        var document = new ScriptDocument { Steps = { ScriptStep.FromAction(waitImage) } };

        var step = Assert.Single(
            new ScriptStepExpander(LibraryWith(condition)).Expand(document, "s"));

        Assert.NotNull(step.Condition);
        Assert.Equal("TestGame.UnableToJoin", step.Condition!.Id);
        Assert.Equal(285, step.Condition.RegionX1);
        // 超时留在调用点，不进条件对象（FR-09 / AC-09）。
        Assert.Equal(30000, step.Instruction.WaitTimeoutMs);
    }

    [Fact]
    public void SameConditionTwoTimeoutsNeedsNoDuplication()
    {
        // AC-09
        var condition = new VisualConditionSpec { Id = "TestGame.C", TemplatePath = $"{Game}/images/x.png" };

        ActionSpec Wait(int timeout) => new()
        {
            Type = CommandType.WaitImage,
            Parameters =
            {
                [ActionSpecMapper.ConditionRefKey] = "TestGame.C",
                ["WaitTimeoutMs"] = timeout
            }
        };

        var document = new ScriptDocument
        {
            Steps = { ScriptStep.FromAction(Wait(5000)), ScriptStep.FromAction(Wait(30000)) }
        };

        var steps = new ScriptStepExpander(LibraryWith(condition)).Expand(document, "s");

        Assert.Equal(5000, steps[0].Instruction.WaitTimeoutMs);
        Assert.Equal(30000, steps[1].Instruction.WaitTimeoutMs);
        Assert.Same(steps[0].Condition, steps[1].Condition);
    }

    [Fact]
    public void MissingConditionNamesScriptStepAndId()
    {
        // AC-08
        var waitImage = new ActionSpec
        {
            Type = CommandType.WaitImage,
            Parameters = { [ActionSpecMapper.ConditionRefKey] = "TestGame.Nope" }
        };

        var document = new ScriptDocument { Steps = { ScriptStep.FromAction(Click(1, 1)), ScriptStep.FromAction(waitImage) } };

        var ex = Assert.Throws<ConditionResolutionException>(
            () => new ScriptStepExpander(LibraryWith()).Expand(document, "我的脚本"));

        Assert.Contains("我的脚本", ex.Message);
        Assert.Contains("步骤 1", ex.Message);
        Assert.Contains("TestGame.Nope", ex.Message);
    }

    [Fact]
    public void MissingBlockNamesScriptAndStep()
    {
        var document = new ScriptDocument { Steps = { ScriptStep.FromBlock("TestGame.Nope") } };

        var ex = Assert.Throws<ActionBlockResolutionException>(
            () => new ScriptStepExpander(blocks: BlocksWith()).Expand(document, "我的脚本"));

        Assert.Contains("我的脚本", ex.Message);
        Assert.Contains("步骤 0", ex.Message);
        Assert.Contains("TestGame.Nope", ex.Message);
    }

    [Fact]
    public void BlockRefWithoutLibraryIsAnError()
    {
        var document = new ScriptDocument { Steps = { ScriptStep.FromBlock("TestGame.X") } };

        var ex = Assert.Throws<ActionBlockResolutionException>(
            () => new ScriptStepExpander().Expand(document, "s"));

        Assert.Contains("未提供动作块库", ex.Message);
    }

    [Fact]
    public void ConditionRefWithoutLibraryIsAnError()
    {
        var document = new ScriptDocument
        {
            Steps =
            {
                ScriptStep.FromAction(new ActionSpec
                {
                    Type = CommandType.WaitImage,
                    Parameters = { [ActionSpecMapper.ConditionRefKey] = "TestGame.X" }
                })
            }
        };

        var ex = Assert.Throws<ConditionResolutionException>(
            () => new ScriptStepExpander().Expand(document, "s"));

        Assert.Contains("未提供条件库", ex.Message);
    }

    [Fact]
    public void ConditionRefOnNonVisualActionIsRejected()
    {
        // 静默忽略会让这个动作完全不带条件地执行。
        var document = new ScriptDocument
        {
            Steps =
            {
                ScriptStep.FromAction(new ActionSpec
                {
                    Type = CommandType.Click,
                    Parameters = { [ActionSpecMapper.ConditionRefKey] = "TestGame.C" }
                })
            }
        };

        var ex = Assert.Throws<ConditionResolutionException>(
            () => new ScriptStepExpander(LibraryWith()).Expand(document, "s"));

        Assert.Contains("Click", ex.Message);
        Assert.Contains("不能引用共享条件", ex.Message);
    }

    [Fact]
    public void BlankConditionRefIsTreatedAsAbsent()
    {
        var document = new ScriptDocument
        {
            Steps =
            {
                ScriptStep.FromAction(new ActionSpec
                {
                    Type = CommandType.WaitImage,
                    Parameters =
                    {
                        [ActionSpecMapper.ConditionRefKey] = "  ",
                        ["TemplatePath"] = "inline.png"
                    }
                })
            }
        };

        var step = Assert.Single(new ScriptStepExpander(LibraryWith()).Expand(document, "s"));

        Assert.Null(step.Condition);
        Assert.Equal("inline.png", step.Instruction.TemplatePath);
    }

    [Fact]
    public void StepWithBothActionAndBlockRefIsRejected()
    {
        var document = new ScriptDocument
        {
            Steps = { new ScriptStep { Action = Click(1, 1), BlockRef = new ActionBlockRef { BlockId = "b" } } }
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => new ScriptStepExpander().Expand(document, "s"));

        Assert.Contains("同时设置了", ex.Message);
    }

    [Fact]
    public void EmptyStepIsRejected()
    {
        var document = new ScriptDocument { Steps = { new ScriptStep() } };

        var ex = Assert.Throws<InvalidOperationException>(
            () => new ScriptStepExpander().Expand(document, "s"));

        Assert.Contains("既没有 Action 也没有 BlockRef", ex.Message);
    }

    [Fact]
    public void OriginNamesBlockAndInnerStepIndex()
    {
        var blocks = BlocksWith(new ActionBlock
        {
            Id = "TestGame.B",
            Steps = { Click(1, 1), Click(2, 2) }
        });

        var document = new ScriptDocument { Steps = { ScriptStep.FromBlock("TestGame.B") } };

        var steps = new ScriptStepExpander(blocks: blocks).Expand(document, "s");

        Assert.Contains("动作块 'TestGame.B' 第 1 步", steps[1].Origin);
    }

    [Fact]
    public void OnlyWaitImageSupportsConditions()
    {
        Assert.True(ScriptStepExpander.SupportsCondition(CommandType.WaitImage));
        // WaitColor 判的是像素颜色，不是模板，VisualConditionSpec 用不上。
        Assert.False(ScriptStepExpander.SupportsCondition(CommandType.WaitColor));
        Assert.False(ScriptStepExpander.SupportsCondition(CommandType.Click));
    }
}
