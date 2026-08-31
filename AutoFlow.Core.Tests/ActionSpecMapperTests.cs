using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Instructions;
using Newtonsoft.Json;
using Xunit;

namespace AutoFlow.Core.Tests;

public class ActionSpecMapperTests
{
    private static Instruction RoundTrip(Instruction original)
        => ActionSpecMapper.ToInstruction(ActionSpecMapper.ToActionSpec(original));

    /// <summary>Round-trips through actual JSON, which is where type fidelity breaks down.</summary>
    private static Instruction RoundTripViaJson(Instruction original)
    {
        var json = JsonConvert.SerializeObject(ActionSpecMapper.ToActionSpec(original));
        var spec = JsonConvert.DeserializeObject<ActionSpec>(json);
        Assert.NotNull(spec);
        return ActionSpecMapper.ToInstruction(spec!);
    }

    [Fact]
    public void Click_RoundTripsLosslessly()
    {
        var original = new Instruction
        {
            CommandType = CommandType.Click,
            PositionX = 368.5,
            PositionY = 645.25,
            MouseButton = MouseButton.Right,
            ClickCount = 2,
            Notes = "点击确认",
            IntervalToNextInstructionMs = 250
        };

        foreach (var restored in new[] { RoundTrip(original), RoundTripViaJson(original) })
        {
            Assert.Equal(CommandType.Click, restored.CommandType);
            Assert.Equal(368.5, restored.PositionX);
            Assert.Equal(645.25, restored.PositionY);
            Assert.Equal(MouseButton.Right, restored.MouseButton);
            Assert.Equal(2, restored.ClickCount);
            Assert.Equal("点击确认", restored.Notes);
            Assert.Equal(250, restored.IntervalToNextInstructionMs);
        }
    }

    [Fact]
    public void WaitImage_RoundTripsLosslessly()
    {
        var original = new Instruction
        {
            CommandType = CommandType.WaitImage,
            TemplatePath = "LastCloudia/images/unable_to_join.png",
            MatchThreshold = 0.92,
            MeanDiffThreshold = 22.5,
            WaitTimeoutMs = 30000,
            ClickAfterMatch = true
        };

        foreach (var restored in new[] { RoundTrip(original), RoundTripViaJson(original) })
        {
            Assert.Equal("LastCloudia/images/unable_to_join.png", restored.TemplatePath);
            Assert.Equal(0.92, restored.MatchThreshold);
            Assert.Equal(22.5, restored.MeanDiffThreshold);
            Assert.Equal(30000, restored.WaitTimeoutMs);
            Assert.True(restored.ClickAfterMatch);
        }
    }

    [Fact]
    public void WaitColor_RoundTripsLosslessly()
    {
        var original = new Instruction
        {
            CommandType = CommandType.WaitColor,
            PositionX = 100,
            PositionY = 200,
            WaitColorHex = "#FF8800",
            WaitColorTolerance = 25,
            WaitTimeoutMs = 5000
        };

        foreach (var restored in new[] { RoundTrip(original), RoundTripViaJson(original) })
        {
            Assert.Equal("#FF8800", restored.WaitColorHex);
            Assert.Equal(25, restored.WaitColorTolerance);
            Assert.Equal(5000, restored.WaitTimeoutMs);
            Assert.Equal(100, restored.PositionX);
        }
    }

    [Fact]
    public void WaitTime_RoundTripsLosslessly()
    {
        var original = new Instruction { CommandType = CommandType.WaitTime, WaitTimeMilliseconds = 1500 };

        foreach (var restored in new[] { RoundTrip(original), RoundTripViaJson(original) })
            Assert.Equal(1500, restored.WaitTimeMilliseconds);
    }

    [Fact]
    public void Loop_RoundTripsLosslessly()
    {
        var original = new Instruction
        {
            CommandType = CommandType.Loop,
            LoopBackToIndex = 3,
            LoopCount = -1
        };

        foreach (var restored in new[] { RoundTrip(original), RoundTripViaJson(original) })
        {
            Assert.Equal(3, restored.LoopBackToIndex);
            Assert.Equal(-1, restored.LoopCount);
        }
    }

    [Fact]
    public void Comment_RoundTripsLosslessly()
    {
        var original = new Instruction { CommandType = CommandType.Comment, CommentContent = "=== 阶段一 ===" };

        foreach (var restored in new[] { RoundTrip(original), RoundTripViaJson(original) })
            Assert.Equal("=== 阶段一 ===", restored.CommentContent);
    }

    [Fact]
    public void CallSkill_RoundTripsNestedParameters()
    {
        var original = new Instruction
        {
            CommandType = CommandType.CallSkill,
            SkillName = "LastCloudiaMultiPlayerCore",
            SkillParameters = new Dictionary<string, object>
            {
                ["maxRounds"] = 5,
                ["sendEmoji"] = true,
                ["label"] = "核心流程"
            }
        };

        foreach (var restored in new[] { RoundTrip(original), RoundTripViaJson(original) })
        {
            Assert.Equal("LastCloudiaMultiPlayerCore", restored.SkillName);
            Assert.NotNull(restored.SkillParameters);
            Assert.Equal(3, restored.SkillParameters!.Count);
            Assert.Equal("核心流程", restored.SkillParameters["label"].ToString());
        }
    }

    [Fact]
    public void EveryCommandType_HasFieldMapping()
    {
        // A new CommandType without a field list would throw at runtime; catch it here instead.
        foreach (var type in Enum.GetValues<CommandType>())
        {
            Assert.True(ActionSpecMapper.KnownFields.ContainsKey(type),
                $"CommandType.{type} 缺少字段映射，ActionSpecMapper.FieldsByType 需要更新");
        }
    }

    [Fact]
    public void EveryMappedFieldExistsOnInstruction()
    {
        // Guards against the field list drifting from the class definition after a rename.
        foreach (var (type, fields) in ActionSpecMapper.KnownFields)
        {
            foreach (var field in fields)
            {
                Assert.NotNull(typeof(Instruction).GetProperty(field));
            }
        }
    }

    [Fact]
    public void UnknownParameter_ThrowsWithAllowedList()
    {
        var spec = new ActionSpec
        {
            Name = "坏动作",
            Type = CommandType.Click,
            Parameters = { ["TemplatePath"] = "x.png" }   // not valid for Click
        };

        var ex = Assert.Throws<ActionMappingException>(() => ActionSpecMapper.ToInstruction(spec));

        Assert.Contains("坏动作", ex.Message);
        Assert.Contains("TemplatePath", ex.Message);
        // The message must list what IS allowed, otherwise the user cannot act on it.
        Assert.Contains("PositionX", ex.Message);
    }

    [Fact]
    public void UnconvertibleValue_ThrowsNamingParameterAndType()
    {
        var spec = new ActionSpec
        {
            Name = "坏点击",
            Type = CommandType.Click,
            Parameters = { ["ClickCount"] = "不是数字" }
        };

        var ex = Assert.Throws<ActionMappingException>(() => ActionSpecMapper.ToInstruction(spec));

        Assert.Contains("ClickCount", ex.Message);
        Assert.Contains("不是数字", ex.Message);
    }

    [Fact]
    public void ConditionRefParameter_IsAcceptedNotRejected()
    {
        // ConditionRef is resolved by the interpreter, so it must not count as unknown.
        var spec = new ActionSpec
        {
            Type = CommandType.WaitImage,
            Parameters =
            {
                [ActionSpecMapper.ConditionRefKey] = "LastCloudia.NetworkError",
                ["WaitTimeoutMs"] = 3000
            }
        };

        var instruction = ActionSpecMapper.ToInstruction(spec);

        Assert.Equal(CommandType.WaitImage, instruction.CommandType);
        Assert.Equal(3000, instruction.WaitTimeoutMs);
    }

    [Fact]
    public void SpecOnlyCarriesFieldsRelevantToItsType()
    {
        // The point of ActionSpec: a Click does not haul around TemplatePath, WaitTimeoutMs etc.
        var spec = ActionSpecMapper.ToActionSpec(new Instruction
        {
            CommandType = CommandType.Click,
            PositionX = 10,
            TemplatePath = "irrelevant.png"   // meaningless for Click
        });

        Assert.DoesNotContain("TemplatePath", spec.Parameters.Keys);
        Assert.DoesNotContain("WaitTimeoutMs", spec.Parameters.Keys);
        Assert.Contains("PositionX", spec.Parameters.Keys);
        Assert.Contains("IntervalToNextInstructionMs", spec.Parameters.Keys);
    }

    [Fact]
    public void EnumAcceptsBothStringAndNumericForms()
    {
        // JSON may carry either, depending on serializer settings.
        var fromString = ActionSpecMapper.ToInstruction(new ActionSpec
        {
            Type = CommandType.Click,
            Parameters = { ["MouseButton"] = "Middle" }
        });
        var fromNumber = ActionSpecMapper.ToInstruction(new ActionSpec
        {
            Type = CommandType.Click,
            Parameters = { ["MouseButton"] = (int)MouseButton.Middle }
        });

        Assert.Equal(MouseButton.Middle, fromString.MouseButton);
        Assert.Equal(MouseButton.Middle, fromNumber.MouseButton);
    }

    [Fact]
    public void MissingParameters_LeaveInstructionDefaults()
    {
        var instruction = ActionSpecMapper.ToInstruction(new ActionSpec { Type = CommandType.Click });

        // Defaults must survive: silently zeroing ClickCount would break execution.
        Assert.Equal(1, instruction.ClickCount);
        Assert.Equal(MouseButton.Left, instruction.MouseButton);
        Assert.Equal(100, instruction.IntervalToNextInstructionMs);
    }
}
