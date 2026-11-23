using AutoFlow.Core.Scripting;
using AutoFlow.Core.Skills.LastCloudia.SinglePlayerCore;
using OpenCvSharp;
using Xunit;

namespace AutoFlow.Core.Tests;

public sealed class SinglePlayerCoreSkillTests
{
    public enum State { Team, Stamina, BattleEnd, BattleAgain, FirstReward, EvaluateReward, LastResult, Unknown }

    private sealed class Runtime : ISinglePlayerCoreRuntime
    {
        public Queue<State> States { get; } = new();
        public State Current { get; private set; }
        public bool CaptureFails { get; set; }
        public bool AllStatesMatch { get; set; }
        public int Captures { get; private set; }
        public int CaptureFailureWaits { get; private set; }
        public List<string> Actions { get; } = [];
        public Action<Runtime>? AfterAction { get; set; }
        public Action<Runtime>? AfterCaptureFailure { get; set; }

        public Mat? Capture(ScriptExecutionContext context)
        {
            Captures++;
            if (CaptureFails) return null;
            if (States.Count > 0) Current = States.Dequeue();
            return new Mat(1, 1, MatType.CV_8UC1, Scalar.All(0));
        }

        public bool IsTeamPrepare(ScriptExecutionContext context, Mat frame) => AllStatesMatch || Current == State.Team;
        public bool IsStaminaRecover(ScriptExecutionContext context, Mat frame) => AllStatesMatch || Current == State.Stamina;
        public bool IsBattleEnd(ScriptExecutionContext context, Mat frame) => AllStatesMatch || Current == State.BattleEnd;
        public bool IsBattleAgain(ScriptExecutionContext context, Mat frame) => AllStatesMatch || Current == State.BattleAgain;
        public bool IsFirstClearReward(ScriptExecutionContext context, Mat frame) => AllStatesMatch || Current == State.FirstReward;
        public bool IsBattleEvaluateReward(ScriptExecutionContext context, Mat frame) => AllStatesMatch || Current == State.EvaluateReward;
        public bool IsBattleLastResult(ScriptExecutionContext context, Mat frame) => AllStatesMatch || Current == State.LastResult;

        private Task Record(string action)
        {
            Actions.Add(action);
            AfterAction?.Invoke(this);
            return Task.CompletedTask;
        }

        public Task HandleTeamPrepareAsync(ScriptExecutionContext context, CancellationToken ct) => Record("team");
        public Task HandleStaminaRecoverAsync(ScriptExecutionContext context, CancellationToken ct) => Record("stamina");
        public Task HandleBattleEndAsync(ScriptExecutionContext context, CancellationToken ct) => Record("battle-end");
        public Task ConfirmBattleAgainAsync(ScriptExecutionContext context, bool again, CancellationToken ct) => Record(again ? "again-yes" : "again-no");
        public Task ConfirmFirstClearRewardAsync(ScriptExecutionContext context, CancellationToken ct) => Record("first-reward");
        public Task ConfirmBattleEvaluateRewardAsync(ScriptExecutionContext context, CancellationToken ct) => Record("evaluate-reward");
        public Task HandleBattleLastResultAsync(ScriptExecutionContext context, CancellationToken ct) => Record("last-result");
        public Task HandleUnknownStateAsync(ScriptExecutionContext context, CancellationToken ct) => Record("unknown");
        public Task WaitAfterCaptureFailureAsync(CancellationToken ct)
        {
            CaptureFailureWaits++;
            AfterCaptureFailure?.Invoke(this);
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private static ScriptExecutionContext Context(List<string>? logs = null) => new()
    {
        Capture = null!, Input = null!, CoordinateTransform = null!, WindowInfo = null!, TemplateMatch = null!,
        Logger = logs is null ? null : logs.Add
    };

    private static Dictionary<string, object> Parameters(bool again = true, int lastStep = 1) =>
        new() { ["again"] = again, ["last_step"] = lastStep };

    [Theory]
    [InlineData(true, "again-yes")]
    [InlineData(false, "again-no")]
    public async Task LastStepOneReturnsAfterTheConfiguredAgainConfirmation(bool again, string expected)
    {
        var runtime = new Runtime();
        runtime.States.Enqueue(State.BattleAgain);
        await new SinglePlayerCoreSkill(runtime).ExecuteAsync(Context(), Parameters(again), default);
        Assert.Equal([expected], runtime.Actions);
        Assert.Equal(1, runtime.Captures);
    }

    [Fact]
    public async Task LastStepTwoContinuesPastAgainAndReturnsAfterLastResult()
    {
        var runtime = new Runtime();
        runtime.States.Enqueue(State.BattleAgain);
        runtime.States.Enqueue(State.FirstReward);
        runtime.States.Enqueue(State.EvaluateReward);
        runtime.States.Enqueue(State.LastResult);
        await new SinglePlayerCoreSkill(runtime).ExecuteAsync(Context(), Parameters(lastStep: 2), default);
        Assert.Equal(["again-yes", "first-reward", "evaluate-reward", "last-result"], runtime.Actions);
    }

    [Fact]
    public async Task BranchPriorityUsesTheAhkOrder()
    {
        using var cancellation = new CancellationTokenSource();
        var runtime = new Runtime { AllStatesMatch = true, AfterAction = _ => cancellation.Cancel() };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SinglePlayerCoreSkill(runtime).ExecuteAsync(Context(), Parameters(), cancellation.Token));
        Assert.Equal(["team"], runtime.Actions);
    }

    public static IEnumerable<object[]> NonTerminalBranches =>
    [
        [State.Team, "team"],
        [State.Stamina, "stamina"],
        [State.BattleEnd, "battle-end"],
        [State.FirstReward, "first-reward"],
        [State.EvaluateReward, "evaluate-reward"],
        [State.LastResult, "last-result"],
        [State.Unknown, "unknown"]
    ];

    [Theory]
    [MemberData(nameof(NonTerminalBranches))]
    public async Task EveryNonTerminalBranchDispatchesItsMappedAction(State state, string expected)
    {
        using var cancellation = new CancellationTokenSource();
        var runtime = new Runtime { AfterAction = _ => cancellation.Cancel() };
        runtime.States.Enqueue(state);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SinglePlayerCoreSkill(runtime).ExecuteAsync(Context(), Parameters(lastStep: 1), cancellation.Token));
        Assert.Equal([expected], runtime.Actions);
    }

    [Fact]
    public async Task LoopHasNoOneThousandCheckLimitAndRemainsCancellable()
    {
        using var cancellation = new CancellationTokenSource();
        var runtime = new Runtime
        {
            AfterAction = r =>
            {
                if (r.Actions.Count == 1001) cancellation.Cancel();
            }
        };
        runtime.States.Enqueue(State.Team);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SinglePlayerCoreSkill(runtime).ExecuteAsync(Context(), Parameters(), cancellation.Token));
        Assert.Equal(1001, runtime.Actions.Count);
        Assert.True(runtime.Captures > 1000);
    }

    [Fact]
    public async Task CaptureFailureWaitsWithoutExecutingUnknownState()
    {
        using var cancellation = new CancellationTokenSource();
        var logs = new List<string>();
        var runtime = new Runtime
        {
            CaptureFails = true,
            AfterCaptureFailure = r =>
            {
                if (r.CaptureFailureWaits == 3) cancellation.Cancel();
            }
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SinglePlayerCoreSkill(runtime).ExecuteAsync(Context(logs), Parameters(), cancellation.Token));
        Assert.Empty(runtime.Actions);
        Assert.Equal(3, runtime.CaptureFailureWaits);
        Assert.Single(logs, line => line.Contains("抓帧失败"));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("yes", 1)]
    [InlineData(true, 0)]
    [InlineData(true, 3)]
    [InlineData(true, true)]
    [InlineData(true, 1.0)]
    public async Task InvalidParametersFailBeforeCapture(object? again, object? lastStep)
    {
        var runtime = new Runtime();
        Dictionary<string, object>? parameters = new();
        if (again is not null) parameters["again"] = again;
        if (lastStep is not null) parameters["last_step"] = lastStep;
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            new SinglePlayerCoreSkill(runtime).ExecuteAsync(Context(), parameters, default));
        Assert.Equal(0, runtime.Captures);
    }
}
