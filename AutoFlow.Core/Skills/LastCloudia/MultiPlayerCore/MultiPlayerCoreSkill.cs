using AutoFlow.Core.Scripting;
using AutoFlow.Core.Skills.LastCloudia.Common;
using AutoFlow.Core.Vision;
using OpenCvSharp;

namespace AutoFlow.Core.Skills.LastCloudia.MultiPlayerCore;

/// <summary>
/// Last Cloudia multi-player core loop skill.
/// Translated from MultiPlayerCoreProcess.ahk - handles prepare/join/battle/rewards/errors.
/// Runs until explicitly stopped (cancellation). Common detections/actions are delegated to
/// <see cref="LastCloudiaCommon"/>; only multi-player-specific logic lives here.
///
/// All coordinates are in the AHK 750x1040 client reference space, used verbatim.
/// </summary>
public sealed class MultiPlayerCoreSkill : ISkill
{
    public string Name => "LastCloudia.MultiPlayerCore";
    public string Description => "Last Cloudia 多人游戏核心流程（准备/战斗/结算/网络错误处理）";

    private static readonly SkillResourceReference UnableToJoin = new("LastCloudia.UnableToJoin", "LastCloudia.ConfirmUnableToJoin");
    private static readonly SkillResourceReference MultiGameRefight = new("LastCloudia.MultiGameRefight", "LastCloudia.ConfirmMultiGameRefight");
    private static readonly SkillResourceReference FirstClearReward = new("LastCloudia.FirstClearReward", "LastCloudia.ConfirmFirstClearReward");
    private static readonly SkillResourceReference FirstMeeting = new("LastCloudia.FirstMeeting", "LastCloudia.ConfirmFirstMeeting");
    private static readonly SkillResourceReference MultiClearReward = new("LastCloudia.MultiClearReward", "LastCloudia.ConfirmMultiClearReward");
    private static readonly SkillResourceReference AbandonTask = new("LastCloudia.AbandonTask", "LastCloudia.ConfirmAbandonTask");
    private static readonly SkillResourceReference ServerConnectionFailRetreat = new("LastCloudia.ServerConnectionFailRetreat", "LastCloudia.ConfirmServerConnectionFailRetreat");
    private static readonly SkillResourceReference ServerConnectionFailReturn = new("LastCloudia.ServerConnectionFailReturn", "LastCloudia.ConfirmServerConnectionFailReturn");
    private static readonly SkillResourceReference ServerConnectionFailMultiplayer = new("LastCloudia.ServerConnectionFailMultiplayer", "LastCloudia.ConfirmServerConnectionFailMultiplayer");
    private static readonly SkillResourceReference NetworkError = new("LastCloudia.NetworkError", "LastCloudia.ConfirmNetworkError");
    private static readonly SkillResourceReference RefightDenied = new("LastCloudia.RefightDenied", "LastCloudia.ConfirmRefightDenied");

    public IReadOnlyList<SkillResourceReference> SharedResources { get; } = Array.AsReadOnly<SkillResourceReference>(
        [UnableToJoin, MultiGameRefight, FirstClearReward, FirstMeeting, MultiClearReward, AbandonTask, ServerConnectionFailRetreat, ServerConnectionFailReturn, ServerConnectionFailMultiplayer, NetworkError, RefightDenied]);

    public IReadOnlyList<SkillConditionReference> SharedConditions { get; } = Array.AsReadOnly<SkillConditionReference>(
        [new("LastCloudia.Emoji"), new("LastCloudia.BattleLastResult")]);

    public IReadOnlyList<SkillFileReference> RequiredFiles { get; } =
        Array.AsReadOnly<SkillFileReference>(
        [
            .. LastCloudiaCommon.RequiredImageFiles,
            new(Path.Combine("Skills", "LastCloudia", "MultiPlayerCore", "images", "go.png")),
            new(Path.Combine("Skills", "LastCloudia", "MultiPlayerCore", "images", "not_prepared.png")),
            new(Path.Combine("Skills", "LastCloudia", "MultiPlayerCore", "images", "prepare_page.png"))
        ]);

    private readonly MultiPlayerCoreState _state = new();
    // Log history belongs to the run, not to gameplay state resets after rewards/errors.
    private string? _previousMatchedBranch;
    private readonly string _imageBasePath;

    // Emoji click coordinates (multi_player_core_emoji_coords), one picked at random.
    private static readonly (int X, int Y)[] EmojiCoords =
    {
        (180, 669), (290, 669), (400, 669), (510, 669)
    };

    private readonly Random _random = new();

    public MultiPlayerCoreSkill()
    {
        // Skill-specific images: bin/Skills/LastCloudia/MultiPlayerCore/images/ (copied at build time)
        _imageBasePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Skills", "LastCloudia", "MultiPlayerCore", "images");
    }

    internal MultiPlayerCoreSkill(MultiPlayerCoreState state) : this() => _state = state;

    internal async Task ExecuteSharedActionAsync(SkillResourceReference branch, ScriptExecutionContext context,
        CancellationToken ct, bool resetState = false)
    {
        await Resources(context).ExecuteBlockAsync(branch.ActionBlockId, Name, context, ct);
        if (resetState) _state.Reset();
    }

    public void ResetForRun()
    {
        _state.Reset();
        _previousMatchedBranch = null;
    }

    internal void RecordMatchedBranch(string? branch, ScriptExecutionContext context)
    {
        if (branch is not null && (branch != _previousMatchedBranch || context.EnableVerboseLogging))
            context.Log($"MultiPlayerCore: 命中 [{branch}]");
        _previousMatchedBranch = branch;
    }

    public async Task ExecuteAsync(
        ScriptExecutionContext context,
        Dictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        var isRoomOwner = parameters?.TryGetValue("is_room_owner", out var ownerValue) == true
            && ownerValue is bool b && b;

        // Single-tick processing - equivalent to one call to MultiPlayerCoreProcess in AHK.
        // The outer rule loop repeatedly calls this skill, just like AHK's outer loop
        // repeatedly calls MultiPlayerCoreProcess.
        var frame = context.Capture.Capture();
        if (frame == null || frame.Empty())
        {
            frame?.Dispose();
            _previousMatchedBranch = null;
            context.Log("MultiPlayerCore: 抓帧失败");
            return;
        }

        string? matchedState;
        using (frame)
        {
            matchedState = await ProcessTickAsync(context, frame, isRoomOwner, cancellationToken);
        }

        RecordMatchedBranch(matchedState, context);
    }

    /// <summary>
    /// Process one tick of the state machine. Translates the if-else-if chain from
    /// MultiPlayerCoreProcess.ahk (lines 71-202). Returns true if any branch matched
    /// (so the caller skips the idle delay and re-scans immediately).
    /// Branch order is preserved from the AHK source — first match wins.
    /// </summary>
    private async Task<string?> ProcessTickAsync(
        ScriptExecutionContext context,
        Mat frame,
        bool isRoomOwner,
        CancellationToken ct)
    {
        // 队伍准备页面 (AHK 72-74)
        if (IsSkillImageMatch(context, frame, "prepare_page", 300, 850, 430, 880))
        {
            context.Log("MultiPlayerCore: 准备按钮点击来源 [prepare_page]");
            await LastCloudiaCommon.ClickReadyAsync(context, ct, delayBeforeMs: 500);
            await Task.Delay(2000, ct);
            return "队伍准备页面";
        }

        // 无法创建或加入房间 (AHK 77-84)
        if (Resources(context).Matches(UnableToJoin.ConditionId, frame, context, Name))
        {
            await ExecuteSharedActionAsync(UnableToJoin, context, ct);
            return "无法创建或加入房间";
        }

        // 房间内，人数已足够出发 (AHK 86-111)
        if (IsSkillImageMatch(context, frame, "go", 374, 614, 638, 663))
        {
            await HandleGoRoomAsync(context, frame, isRoomOwner, ct);
            return "房间内-出发";
        }

        // 再战确认 (AHK 113-119)
        if (Resources(context).Matches(MultiGameRefight.ConditionId, frame, context, Name))
        {
            await ExecuteSharedActionAsync(MultiGameRefight, context, ct);
            return "再战确认";
        }

        // 表情包界面 (AHK 121-131) — 只发一次
        if (Resources(context).Matches("LastCloudia.Emoji", frame, context, Name))
        {
            if (!_state.EmojiSent)
            {
                var (ex, ey) = EmojiCoords[_random.Next(EmojiCoords.Length)];
                await ClickScriptAsync(context, ex, ey, delayBeforeMs: 1000, ct);
                _state.EmojiSent = true;
            }
            return "表情包界面";
        }

        // 战斗结算 (AHK 133-138)
        if (LastCloudiaCommon.IsBattleEnd(context, frame))
        {
            await LastCloudiaCommon.HandleBattleEndAsync(context, ct);
            return "战斗结算";
        }

        // 初次通关奖励 (AHK 141-145)
        if (Resources(context).Matches(FirstClearReward.ConditionId, frame, context, Name))
        {
            await ExecuteSharedActionAsync(FirstClearReward, context, ct);
            return "初次通关奖励";
        }

        // 初次见面奖励 (AHK 147-151)
        if (Resources(context).Matches(FirstMeeting.ConditionId, frame, context, Name))
        {
            await ExecuteSharedActionAsync(FirstMeeting, context, ct);
            return "初次见面奖励";
        }

        // 多人奖励确认 (AHK 153-159)
        if (Resources(context).Matches(MultiClearReward.ConditionId, frame, context, Name))
        {
            await ExecuteSharedActionAsync(MultiClearReward, context, ct);
            return "多人奖励确认";
        }

        // 最后结算页面 (AHK 161-167) — 连点两轮后重置状态
        if (Resources(context).Matches("LastCloudia.BattleLastResult", frame, context, Name))
        {
            await LastCloudiaCommon.HandleBattleLastResultAsync(context, ct);
            _state.Reset();
            return "最后结算页面";
        }

        // 放弃任务 (AHK 169-172)
        if (Resources(context).Matches(AbandonTask.ConditionId, frame, context, Name))
        {
            await ExecuteSharedActionAsync(AbandonTask, context, ct, resetState: true);
            return "放弃任务";
        }

        // 无法连接伺服器 类型1 (AHK 174-177)
        if (Resources(context).Matches(ServerConnectionFailRetreat.ConditionId, frame, context, Name))
        {
            await ExecuteSharedActionAsync(ServerConnectionFailRetreat, context, ct, resetState: true);
            return "无法连接伺服器-类型1";
        }

        // 无法连接伺服器 类型2 (AHK 179-182)
        if (Resources(context).Matches(ServerConnectionFailReturn.ConditionId, frame, context, Name))
        {
            await ExecuteSharedActionAsync(ServerConnectionFailReturn, context, ct, resetState: true);
            return "无法连接伺服器-类型2";
        }

        // 无法连接多人游戏伺服器 (AHK 184-187)
        if (Resources(context).Matches(ServerConnectionFailMultiplayer.ConditionId, frame, context, Name))
        {
            await ExecuteSharedActionAsync(ServerConnectionFailMultiplayer, context, ct, resetState: true);
            return "无法连接多人游戏伺服器";
        }

        // 发生了网络错误 (AHK 189-192)
        if (Resources(context).Matches(NetworkError.ConditionId, frame, context, Name))
        {
            await ExecuteSharedActionAsync(NetworkError, context, ct, resetState: true);
            return "网络错误";
        }

        // 再战被拒绝 (AHK 194-198)
        if (Resources(context).Matches(RefightDenied.ConditionId, frame, context, Name))
        {
            await ExecuteSharedActionAsync(RefightDenied, context, ct);
            return "再战被拒绝";
        }

        // 无匹配 (AHK 200-201: else Sleep 500) — 交由主循环空转
        return null;
    }

    /// <summary>
    /// 房间内出发逻辑 (AHK 86-111)。房主等待更多人后点出发；非房主累计等待，超时撤退。
    /// </summary>
    private async Task HandleGoRoomAsync(
        ScriptExecutionContext context,
        Mat frame,
        bool isRoomOwner,
        CancellationToken ct)
    {
        // prepare_page 到进入房间时间不稳定，可能导致准备状态被取消，这里兼容补点一次 (AHK 89-93)
        if (IsSkillImageMatch(context, frame, "not_prepared", 265, 755, 450, 800))
        {
            context.Log("MultiPlayerCore: 准备按钮点击来源 [not_prepared]");
            await LastCloudiaCommon.ClickReadyAsync(context, ct);
        }

        if (isRoomOwner)
        {
            // 房主：等一会儿凑人，再点出发（出发按钮坐标与准备相同）(AHK 96-98)
            await Task.Delay(10000, ct);
            context.Log("MultiPlayerCore: 出发按钮点击来源 [房主等待结束]");
            await LastCloudiaCommon.ClickReadyAsync(context, ct);
        }
        else
        {
            // 非房主：累计等待，达到 40 轮（约 2 分钟）则撤退 (AHK 101-110)
            _state.InRoomCount += 1;
            await Task.Delay(2000, ct);
            if (_state.InRoomCount == 40)
            {
                context.Log("MultiPlayerCore: 准备按钮点击来源 [非房主超时撤退]");
                await LastCloudiaCommon.ClickReadyAsync(context, ct);
                await Task.Delay(2000, ct);
                await LastCloudiaCommon.ClickBackAsync(context, ct);
                _state.Reset();
            }
        }
    }

    private static SharedScriptResources Resources(ScriptExecutionContext context)
        => context.SharedResources ?? throw new InvalidOperationException("LastCloudia.MultiPlayerCore 缺少本次运行的共享资源。");

    /// <summary>技能特有图片的区域匹配（复用 Common 的原语，图片在本技能的 images/ 下）。</summary>
    private bool IsSkillImageMatch(
        ScriptExecutionContext context,
        Mat frame,
        string imageName,
        int x1, int y1, int x2, int y2,
        double threshold = 0.85)
        => LastCloudiaCommon.IsImageMatchAt(
            context, frame, Path.Combine(_imageBasePath, $"{imageName}.png"), x1, y1, x2, y2, threshold);

    /// <summary>移动到坐标 → 等待 → 点击（复用 Common 的 move-wait-click 原语）。</summary>
    private static Task ClickScriptAsync(
        ScriptExecutionContext context,
        int scriptX, int scriptY,
        int delayBeforeMs,
        CancellationToken ct)
        => LastCloudiaCommon.ClickTargetButtonAsync(context, scriptX, scriptY, delayBeforeMs, ct);
}
