using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Handlers;
using AutoFlow.Core.Scripting.Instructions;
using Fischless.GameCapture;
using OpenCvSharp;
using Xunit;

namespace AutoFlow.Core.Tests;

public sealed class SkillLoggingTests
{
    private sealed class EmptyCapture : IGameCapture
    {
        public bool IsCapturing => true;
        public void Start(nint hWnd, Dictionary<string, object>? settings = null) { }
        public Mat? Capture() => null;
        public void Stop() { }
        public void Dispose() { }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SkillEventRemainsVisibleWhileCallBoundariesRequireVerboseMode(bool verbose)
    {
        var logs = new List<string>();
        using var capture = new EmptyCapture();
        var context = new ScriptExecutionContext
        {
            Capture = capture, Input = null!, CoordinateTransform = null!, WindowInfo = null!, TemplateMatch = null!,
            Logger = logs.Add, EnableVerboseLogging = verbose
        };
        await new CallSkillHandler().ExecuteAsync(new Instruction
        { CommandType = CommandType.CallSkill, SkillName = "LastCloudia.MultiPlayerCore" }, context, default);
        Assert.Contains("MultiPlayerCore: 抓帧失败", logs);
        Assert.Equal(verbose ? 2 : 0, logs.Count(line => line.StartsWith("[DEBUG] CallSkill:")));
        logs.Clear();
        await new CallSkillHandler().ExecuteAsync(new Instruction
        { CommandType = CommandType.CallSkill, SkillName = "Missing.Skill" }, context, default);
        Assert.Contains(logs, line => line.Contains("not found"));
    }
}
