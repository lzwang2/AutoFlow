using AutoFlow.Core.Scripting;
using AutoFlow.Core.Skills.LastCloudia.SinglePlayerCore;

namespace AutoFlow.Core.Skills.LastCloudia.WeekRound;

/// <summary>Finite four-call orchestration migrated from activity/week_round/WeekRound.ahk.</summary>
public sealed class WeekRoundSkill : ISkill
{
    public const string SkillName = "LastCloudia.WeekRound";
    public string Name => SkillName;
    public string Description => "Last Cloudia 周回活动（固定执行四次单人核心）";
    public IReadOnlyList<SkillResourceReference> SharedResources => SinglePlayerCoreSkill.ResourceDependencies;
    public IReadOnlyList<SkillConditionReference> SharedConditions => SinglePlayerCoreSkill.ConditionDependencies;
    public IReadOnlyList<SkillFileReference> RequiredFiles { get; } =
        new SinglePlayerCoreSkill().RequiredFiles;

    private readonly ISkill? _singlePlayerCore;

    public WeekRoundSkill() { }
    internal WeekRoundSkill(ISkill singlePlayerCore) => _singlePlayerCore = singlePlayerCore;

    public async Task ExecuteAsync(
        ScriptExecutionContext context,
        Dictionary<string, object>? parameters,
        CancellationToken cancellationToken)
    {
        if (parameters is { Count: > 0 })
            throw new ArgumentException("LastCloudia.WeekRound 不接收参数；轮数固定为 4。");

        var singlePlayerCore = _singlePlayerCore
            ?? SkillRegistry.Instance.GetSkill(SinglePlayerCoreSkill.SkillName)
            ?? throw new InvalidOperationException($"未注册技能: {SinglePlayerCoreSkill.SkillName}");

        for (var round = 1; round <= 4; round++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.ReportProgress($"第 {round}/4 次单人核心调用");
            context.Log($"WeekRound: 开始第 {round}/4 次单人核心调用");
            await singlePlayerCore.ExecuteAsync(
                context,
                new Dictionary<string, object>
                {
                    ["again"] = true,
                    ["last_step"] = 1
                },
                cancellationToken);
            context.Log($"WeekRound: 完成第 {round}/4 次单人核心调用");
        }
    }
}
