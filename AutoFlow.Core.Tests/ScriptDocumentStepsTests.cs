using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Instructions;
using Newtonsoft.Json;
using Xunit;

namespace AutoFlow.Core.Tests;

public class ScriptDocumentStepsTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "AutoFlowTests_" + Guid.NewGuid().ToString("N"));

    public ScriptDocumentStepsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static ScriptDocument WithSteps() => new()
    {
        Id = "TestGame.Daily",
        Name = "每日任务",
        OuterWidth = 750,
        OuterHeight = 1040,
        Steps =
        {
            ScriptStep.FromAction(new ActionSpec
            {
                Type = CommandType.WaitImage,
                Parameters =
                {
                    [ActionSpecMapper.ConditionRefKey] = "TestGame.UnableToJoin",
                    ["WaitTimeoutMs"] = 30000
                }
            }),
            ScriptStep.FromBlock("TestGame.CloseAndRetry", alias: "收尾")
        }
    };

    [Fact]
    public void StepsSurviveJsonRoundTrip()
    {
        var service = new ScriptDocumentService();
        var path = Path.Combine(_dir, "steps.json");

        service.Save(WithSteps(), path);
        var loaded = service.Load(path);

        Assert.Equal("TestGame.Daily", loaded.Id);
        Assert.Equal("每日任务", loaded.Name);
        Assert.Equal(2, loaded.Steps.Count);

        var first = loaded.Steps[0];
        Assert.True(first.IsAction);
        Assert.Equal(CommandType.WaitImage, first.Action!.Type);
        Assert.Equal("TestGame.UnableToJoin", first.Action.Parameters[ActionSpecMapper.ConditionRefKey]);

        var second = loaded.Steps[1];
        Assert.True(second.IsBlockRef);
        Assert.Equal("TestGame.CloseAndRetry", second.BlockRef!.BlockId);
        Assert.Equal("收尾", second.BlockRef.Alias);
    }

    [Fact]
    public void OuterSizeIsPreservedAsReferenceSpaceBasis()
    {
        // OuterWidth/Height 是 FR-10 参照空间校验的比较基准，不能在改造中丢掉。
        var service = new ScriptDocumentService();
        var path = Path.Combine(_dir, "outer.json");

        service.Save(WithSteps(), path);
        var loaded = service.Load(path);

        Assert.Equal(750, loaded.OuterWidth);
        Assert.Equal(1040, loaded.OuterHeight);
    }

    [Fact]
    public void InstructionsOnlyDocumentHasEmptySteps()
    {
        var service = new ScriptDocumentService();
        var path = Path.Combine(_dir, "instructions.json");

        var document = new ScriptDocument();
        document.Instructions.Add(new Instruction { CommandType = CommandType.Click, PositionX = 5 });
        service.Save(document, path);

        var loaded = service.Load(path);

        Assert.Empty(loaded.Steps);
        Assert.Equal(5, Assert.Single(loaded.Instructions).PositionX);
    }

    [Fact]
    public void StepJsonUsesSelfDescribingShape()
    {
        var json = JsonConvert.SerializeObject(WithSteps());

        // 不靠类型判别字符串，靠字段名区分两种步骤。
        Assert.Contains("\"Action\"", json);
        Assert.Contains("\"BlockRef\"", json);
    }

    [Fact]
    public void ValidateRejectsAmbiguousAndEmptySteps()
    {
        var both = new ScriptStep
        {
            Action = new ActionSpec { Type = CommandType.Click },
            BlockRef = new ActionBlockRef { BlockId = "b" }
        };
        Assert.Throws<InvalidOperationException>(() => both.Validate("步骤 0"));

        Assert.Throws<InvalidOperationException>(() => new ScriptStep().Validate("步骤 0"));
    }

    [Fact]
    public void ValidateAcceptsExactlyOne()
    {
        ScriptStep.FromAction(new ActionSpec { Type = CommandType.Click }).Validate("步骤 0");
        ScriptStep.FromBlock("b").Validate("步骤 0");
    }

    [Fact]
    public void DescribeIdentifiesStepContent()
    {
        Assert.Equal(
            "Click",
            ScriptStep.FromAction(new ActionSpec { Type = CommandType.Click }).Describe());
        Assert.Equal(
            "点关闭",
            ScriptStep.FromAction(new ActionSpec { Type = CommandType.Click, Name = "点关闭" }).Describe());
        Assert.Equal("动作块 'b'", ScriptStep.FromBlock("b").Describe());
    }
}
