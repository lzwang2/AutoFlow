using AutoFlow.Core.Scripting;
using AutoFlow.Core.Skills.LastCloudia.Common;
using OpenCvSharp;

namespace AutoFlow.Core.Skills.LastCloudia.SinglePlayerCore;

/// <summary>Reusable single-player state machine migrated from SinglePlayerCoreProcess.ahk.</summary>
public sealed class SinglePlayerCoreSkill : ISkill
{
    public const string SkillName = "LastCloudia.SinglePlayerCore";
    public string Name => SkillName;
    public string Description => "Last Cloudia 单人核心流程（准备、体力、战斗与结算）";

    internal static IReadOnlyList<SkillResourceReference> ResourceDependencies { get; } =
        Array.AsReadOnly<SkillResourceReference>(
        [
            new("LastCloudia.BattleAgain", "LastCloudia.ConfirmBattleAgainYes"),
            new("LastCloudia.BattleAgain", "LastCloudia.ConfirmBattleAgainNo"),
            new("LastCloudia.FirstClearReward", "LastCloudia.ConfirmFirstClearReward"),
            new("LastCloudia.BattleEvaluateReward", "LastCloudia.ConfirmBattleEvaluateReward")
        ]);

    internal static IReadOnlyList<SkillConditionReference> ConditionDependencies { get; } =
        Array.AsReadOnly<SkillConditionReference>([new("LastCloudia.BattleLastResult")]);

    public IReadOnlyList<SkillResourceReference> SharedResources => ResourceDependencies;
    public IReadOnlyList<SkillConditionReference> SharedConditions => ConditionDependencies;
    public IReadOnlyList<SkillFileReference> RequiredFiles => LastCloudiaCommon.RequiredImageFiles;

    private readonly ISinglePlayerCoreRuntime _runtime;
    private string? _previousState;
    private bool _captureFailing;

    public SinglePlayerCoreSkill() : this(new SinglePlayerCoreRuntime()) { }
    internal SinglePlayerCoreSkill(ISinglePlayerCoreRuntime runtime) => _runtime = runtime;

    public void ResetForRun()
    {
        _previousState = null;
        _captureFailing = false;
    }

    public async Task ExecuteAsync(
        ScriptExecutionContext context,
        Dictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        var again = ReadAgain(parameters);
        var lastStep = ReadLastStep(parameters);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var frame = _runtime.Capture(context);
            if (frame is null || frame.Empty())
            {
                frame?.Dispose();
                if (!_captureFailing) context.Log("SinglePlayerCore: 抓帧失败，等待后重试");
                _captureFailing = true;
                _previousState = null;
                await _runtime.WaitAfterCaptureFailureAsync(cancellationToken);
                continue;
            }

            if (_captureFailing) context.Log("SinglePlayerCore: 抓帧已恢复");
            _captureFailing = false;

            using (frame)
            {
                if (_runtime.IsTeamPrepare(context, frame))
                {
                    RecordState("队伍准备", context);
                    await _runtime.HandleTeamPrepareAsync(context, cancellationToken);
                }
                else if (_runtime.IsStaminaRecover(context, frame))
                {
                    RecordState("体力恢复", context);
                    await _runtime.HandleStaminaRecoverAsync(context, cancellationToken);
                }
                else if (_runtime.IsBattleEnd(context, frame))
                {
                    RecordState("战斗结算", context);
                    await _runtime.HandleBattleEndAsync(context, cancellationToken);
                }
                else if (_runtime.IsBattleAgain(context, frame))
                {
                    RecordState(again ? "再战确认-是" : "再战确认-否", context);
                    await _runtime.ConfirmBattleAgainAsync(context, again, cancellationToken);
                    if (lastStep == 1) return;
                }
                else if (_runtime.IsFirstClearReward(context, frame))
                {
                    RecordState("初次通关奖励", context);
                    await _runtime.ConfirmFirstClearRewardAsync(context, cancellationToken);
                }
                else if (_runtime.IsBattleEvaluateReward(context, frame))
                {
                    RecordState("战斗评价奖励", context);
                    await _runtime.ConfirmBattleEvaluateRewardAsync(context, cancellationToken);
                }
                else if (_runtime.IsBattleLastResult(context, frame))
                {
                    RecordState("最后结算", context);
                    await _runtime.HandleBattleLastResultAsync(context, cancellationToken);
                    if (lastStep == 2) return;
                }
                else
                {
                    RecordState("未知状态安全点击", context);
                    await _runtime.HandleUnknownStateAsync(context, cancellationToken);
                }
            }
        }
    }

    private void RecordState(string state, ScriptExecutionContext context)
    {
        if (state != _previousState || context.EnableVerboseLogging)
            context.Log($"SinglePlayerCore: 命中 [{state}]");
        _previousState = state;
    }

    private static bool ReadAgain(Dictionary<string, object>? parameters)
    {
        if (parameters is null || !parameters.TryGetValue("again", out var value) || value is not bool again)
            throw new ArgumentException("LastCloudia.SinglePlayerCore 参数 again 必须是布尔值。");
        return again;
    }

    private static int ReadLastStep(Dictionary<string, object>? parameters)
    {
        if (parameters is null || !parameters.TryGetValue("last_step", out var value))
            throw new ArgumentException("LastCloudia.SinglePlayerCore 缺少整数参数 last_step（仅允许 1 或 2）。");

        var lastStep = value switch
        {
            byte number => (int)number,
            sbyte number => (int)number,
            short number => (int)number,
            ushort number => (int)number,
            int number => number,
            uint number when number <= int.MaxValue => (int)number,
            long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
            ulong number when number <= int.MaxValue => (int)number,
            _ => throw new ArgumentException("LastCloudia.SinglePlayerCore 参数 last_step 必须是整数 1 或 2。")
        };

        if (lastStep is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(parameters), "LastCloudia.SinglePlayerCore 参数 last_step 仅允许 1 或 2。");
        return lastStep;
    }
}
