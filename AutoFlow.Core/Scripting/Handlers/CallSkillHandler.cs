using AutoFlow.Core.Scripting.Instructions;
using AutoFlow.Core.Skills;

namespace AutoFlow.Core.Scripting.Handlers;

/// <summary>
/// Handler for CallSkill instruction - invokes a hardcoded skill by name
/// </summary>
public sealed class CallSkillHandler : InstructionHandlerBase
{
    public override CommandType CommandType => CommandType.CallSkill;

    public override async Task ExecuteAsync(Instruction instruction, ScriptExecutionContext context, CancellationToken cancellationToken)
    {
        var skillName = instruction.SkillName?.Trim();
        if (string.IsNullOrEmpty(skillName))
        {
            context.Log("CallSkill: SkillName is empty, skipping");
            return;
        }

        var skill = SkillRegistry.Instance.GetSkill(skillName);
        if (skill == null)
        {
            context.Log($"CallSkill: Skill '{skillName}' not found in registry");
            return;
        }

        context.Log($"CallSkill: Invoking '{skillName}'");
        await skill.ExecuteAsync(context, instruction.SkillParameters, cancellationToken);
        context.Log($"CallSkill: '{skillName}' completed");
    }
}
