using AutoFlow.Core.Scripting;
using AutoFlow.Core.Skills.LastCloudia.Common;
using OpenCvSharp;

namespace AutoFlow.Core.Skills.LastCloudia.SinglePlayerCore;

internal interface ISinglePlayerCoreRuntime
{
    Mat? Capture(ScriptExecutionContext context);
    bool IsTeamPrepare(ScriptExecutionContext context, Mat frame);
    bool IsStaminaRecover(ScriptExecutionContext context, Mat frame);
    bool IsBattleEnd(ScriptExecutionContext context, Mat frame);
    bool IsBattleAgain(ScriptExecutionContext context, Mat frame);
    bool IsFirstClearReward(ScriptExecutionContext context, Mat frame);
    bool IsBattleEvaluateReward(ScriptExecutionContext context, Mat frame);
    bool IsBattleLastResult(ScriptExecutionContext context, Mat frame);
    Task HandleTeamPrepareAsync(ScriptExecutionContext context, CancellationToken ct);
    Task HandleStaminaRecoverAsync(ScriptExecutionContext context, CancellationToken ct);
    Task HandleBattleEndAsync(ScriptExecutionContext context, CancellationToken ct);
    Task ConfirmBattleAgainAsync(ScriptExecutionContext context, bool again, CancellationToken ct);
    Task ConfirmFirstClearRewardAsync(ScriptExecutionContext context, CancellationToken ct);
    Task ConfirmBattleEvaluateRewardAsync(ScriptExecutionContext context, CancellationToken ct);
    Task HandleBattleLastResultAsync(ScriptExecutionContext context, CancellationToken ct);
    Task HandleUnknownStateAsync(ScriptExecutionContext context, CancellationToken ct);
    Task WaitAfterCaptureFailureAsync(CancellationToken ct);
}

internal sealed class SinglePlayerCoreRuntime : ISinglePlayerCoreRuntime
{
    private const string Owner = "LastCloudia.SinglePlayerCore";
    private static SharedScriptResources Resources(ScriptExecutionContext context) =>
        context.SharedResources ?? throw new InvalidOperationException($"{Owner} 缺少本次运行的共享资源。");

    public Mat? Capture(ScriptExecutionContext context) => context.CaptureFrame();
    public bool IsTeamPrepare(ScriptExecutionContext context, Mat frame) =>
        LastCloudiaCommon.IsSingleTeamPrepare(context, frame);
    public bool IsStaminaRecover(ScriptExecutionContext context, Mat frame) =>
        LastCloudiaCommon.IsStaminaRecover(context, frame);
    public bool IsBattleEnd(ScriptExecutionContext context, Mat frame) =>
        LastCloudiaCommon.IsBattleEnd(context, frame);
    public bool IsBattleAgain(ScriptExecutionContext context, Mat frame) =>
        Resources(context).Matches("LastCloudia.BattleAgain", frame, context, Owner);
    public bool IsFirstClearReward(ScriptExecutionContext context, Mat frame) =>
        Resources(context).Matches("LastCloudia.FirstClearReward", frame, context, Owner);
    public bool IsBattleEvaluateReward(ScriptExecutionContext context, Mat frame) =>
        Resources(context).Matches("LastCloudia.BattleEvaluateReward", frame, context, Owner);
    public bool IsBattleLastResult(ScriptExecutionContext context, Mat frame) =>
        Resources(context).Matches("LastCloudia.BattleLastResult", frame, context, Owner);

    public async Task HandleTeamPrepareAsync(ScriptExecutionContext context, CancellationToken ct)
    {
        await LastCloudiaCommon.ClickReadyAsync(context, ct);
        await Task.Delay(2000, ct);
    }

    public async Task HandleStaminaRecoverAsync(ScriptExecutionContext context, CancellationToken ct)
    {
        await LastCloudiaCommon.ClickAddStaminaAsync(context, count: 4, ct);
        await Task.Delay(1000, ct);
        await LastCloudiaCommon.ClickRecoverStaminaAsync(context, ct);
        await Task.Delay(1000, ct);
    }

    public Task HandleBattleEndAsync(ScriptExecutionContext context, CancellationToken ct) =>
        LastCloudiaCommon.HandleBattleEndAsync(context, ct);

    public Task ConfirmBattleAgainAsync(ScriptExecutionContext context, bool again, CancellationToken ct) =>
        Resources(context).ExecuteBlockAsync(
            again ? "LastCloudia.ConfirmBattleAgainYes" : "LastCloudia.ConfirmBattleAgainNo",
            Owner, context, ct);

    public Task ConfirmFirstClearRewardAsync(ScriptExecutionContext context, CancellationToken ct) =>
        Resources(context).ExecuteBlockAsync("LastCloudia.ConfirmFirstClearReward", Owner, context, ct);

    public Task ConfirmBattleEvaluateRewardAsync(ScriptExecutionContext context, CancellationToken ct) =>
        Resources(context).ExecuteBlockAsync("LastCloudia.ConfirmBattleEvaluateReward", Owner, context, ct);

    public Task HandleBattleLastResultAsync(ScriptExecutionContext context, CancellationToken ct) =>
        LastCloudiaCommon.HandleBattleLastResultAsync(context, ct);

    public Task HandleUnknownStateAsync(ScriptExecutionContext context, CancellationToken ct) =>
        LastCloudiaCommon.HandleUnknownSinglePlayerStateAsync(context, ct);

    public Task WaitAfterCaptureFailureAsync(CancellationToken ct) => Task.Delay(250, ct);
}
