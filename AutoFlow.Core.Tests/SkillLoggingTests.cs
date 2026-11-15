using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Handlers;
using AutoFlow.Core.Scripting.Instructions;
using Fischless.GameCapture;
using OpenCvSharp;
using Xunit;
using AutoFlow.Core.Skills.LastCloudia.MultiPlayerCore;

namespace AutoFlow.Core.Tests;

public sealed class SkillLoggingTests
{
    private static ScriptExecutionContext LoggingContext(List<string> logs, bool verbose = false) => new()
    {
        Capture = new EmptyCapture(), Input = null!, CoordinateTransform = null!, WindowInfo = null!, TemplateMatch = null!,
        Logger = logs.Add, EnableVerboseLogging = verbose
    };

    [Theory]
    [InlineData(false, 4)]
    [InlineData(true, 6)]
    public void SkillBranchChangesAndUnmatchedGapsControlHitLogging(bool verbose, int count)
    {
        var logs = new List<string>();
        var context = LoggingContext(logs, verbose);
        var skill = new MultiPlayerCoreSkill();
        foreach (var branch in new[] { "房间内-出发", "房间内-出发", "多人奖励确认", "多人奖励确认", "房间内-出发", null, "房间内-出发" })
            skill.RecordMatchedBranch(branch, context);
        Assert.Equal(count, logs.Count);
        if (!verbose)
            Assert.Equal(new[] { "MultiPlayerCore: 命中 [房间内-出发]", "MultiPlayerCore: 命中 [多人奖励确认]",
                "MultiPlayerCore: 命中 [房间内-出发]", "MultiPlayerCore: 命中 [房间内-出发]" }, logs);
    }

    [Fact]
    public void GameplayResetKeepsLogHistoryButNewRunLogsItsFirstHit()
    {
        var state = new MultiPlayerCoreState();
        var skill = new MultiPlayerCoreSkill(state);
        var logs = new List<string>();
        var context = LoggingContext(logs);
        skill.RecordMatchedBranch("网络错误", context);
        state.Reset(); // error/reward confirmation resets gameplay state inside the same run
        skill.RecordMatchedBranch("网络错误", context);
        Assert.Single(logs);
        skill.ResetForRun();
        skill.RecordMatchedBranch("网络错误", context);
        Assert.Equal(2, logs.Count);
    }

    [Fact]
    public async Task CaptureFailureRemainsVisibleAndNextHitIsReported()
    {
        var skill = new MultiPlayerCoreSkill();
        var logs = new List<string>();
        var context = LoggingContext(logs);
        skill.RecordMatchedBranch("房间内-出发", context);
        await skill.ExecuteAsync(context, null, default);
        skill.RecordMatchedBranch("房间内-出发", context);
        Assert.Equal(new[] { "MultiPlayerCore: 命中 [房间内-出发]", "MultiPlayerCore: 抓帧失败",
            "MultiPlayerCore: 命中 [房间内-出发]" }, logs);
    }

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
