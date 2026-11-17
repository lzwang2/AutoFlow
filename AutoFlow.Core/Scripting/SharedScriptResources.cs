using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Handlers;
using AutoFlow.Core.Scripting.Instructions;
using OpenCvSharp;
using AutoFlow.Core.Skills;

namespace AutoFlow.Core.Scripting;

/// <summary>Private library snapshots shared by rules and skills for one execution.</summary>
public sealed class SharedScriptResources
{
    private readonly ConditionLibrary _conditions;
    private readonly ActionBlockLibrary _blocks;
    private readonly InstructionSequenceExecutor _executor;

    public SharedScriptResources(ConditionLibrary? conditions, ActionBlockLibrary? blocks, InstructionHandlerRegistry handlers)
    {
        _conditions = (conditions ?? new ConditionLibrary()).Snapshot();
        _blocks = (blocks ?? new ActionBlockLibrary()).Snapshot();
        _executor = new InstructionSequenceExecutor(handlers);
    }

    public bool Matches(string id, Mat frame, ScriptExecutionContext context, string owner)
        => VisualConditionEvaluator.Evaluate(_conditions.Require(id, owner, "技能条件"), frame, context) is not null;

    public List<ExpandedStep> ResolveBlock(string id, string owner)
        => new ScriptStepExpander(_conditions, _blocks).Expand([ScriptStep.FromBlock(id)], owner, owner);

    public Task ExecuteBlockAsync(string id, string owner, ScriptExecutionContext context, CancellationToken ct)
        => _executor.ExecuteAsync(ResolveBlock(id, owner), context, ct);

    public void ValidateSkillDependencies(IEnumerable<ExpandedStep> steps)
    {
        foreach (var step in steps)
        {
            if (step.Instruction.CommandType != CommandType.CallSkill) continue;
            var skill = SkillRegistry.Instance.GetSkill(step.Instruction.SkillName?.Trim() ?? "");
            if (skill is null) continue; // Unknown skills are reported by the caller's skill validation.
            foreach (var dependency in skill.SharedResources)
            {
                ValidateConditionImage(dependency.ConditionId, skill.Name, step.Origin);
                var actions = ResolveBlock(dependency.ActionBlockId, skill.Name);
                if (actions.Count == 0 || actions.Any(a => a.Instruction.CommandType is not (CommandType.Click or CommandType.WaitTime)))
                    throw new InvalidOperationException($"技能 {skill.Name} 动作块 {dependency.ActionBlockId} 必须是非空的点击／等待动作序列。");
            }
            foreach (var dependency in skill.SharedConditions)
                ValidateConditionImage(dependency.ConditionId, skill.Name, step.Origin);
        }
    }

    private void ValidateConditionImage(string id, string owner, string origin)
    {
        var condition = _conditions.Require(id, owner, origin);
        if (!File.Exists(condition.EffectiveTemplatePath))
            throw new FileNotFoundException($"技能 {owner} 条件 {condition.Id} 的图片缺失: {condition.EffectiveTemplatePath}");
    }
}
