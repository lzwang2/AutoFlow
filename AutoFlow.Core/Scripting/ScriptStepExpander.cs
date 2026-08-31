using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Instructions;

namespace AutoFlow.Core.Scripting;

/// <summary>
/// An instruction plus the shared condition it was told to use, if any.
///
/// The condition rides alongside the instruction rather than inside it: <see cref="Instruction"/>
/// is unchanged per §11.2, and it has no region fields to hold one anyway. The interpreter
/// hands both to the handler.
/// </summary>
public sealed class ExpandedStep
{
    public required Instruction Instruction { get; init; }

    /// <summary>The resolved shared condition, or null when the action carries inline values.</summary>
    public VisualConditionSpec? Condition { get; init; }

    /// <summary>Human-readable origin, used in error messages ("步骤 3" / "动作块 'X' 第 2 步").</summary>
    public required string Origin { get; init; }
}

/// <summary>
/// Flattens a <see cref="ScriptDocument"/>'s <see cref="ScriptDocument.Steps"/> into an
/// ordered instruction list, expanding action-block references and resolving condition
/// references (Spec §12.1 steps 1–4).
///
/// <para>Order is preserved exactly: a block reference contributes its steps in place. This is
/// the whole of the change to linear execution — the interpreter still walks the resulting
/// list front to back.</para>
/// </summary>
public sealed class ScriptStepExpander
{
    private readonly ActionBlockLibrary? _blocks;
    private readonly ConditionLibrary? _conditions;

    public ScriptStepExpander(ConditionLibrary? conditions = null, ActionBlockLibrary? blocks = null)
    {
        _conditions = conditions;
        _blocks = blocks;
    }

    /// <summary>
    /// Expands a document's steps. When <see cref="ScriptDocument.Steps"/> is empty the
    /// document's <see cref="ScriptDocument.Instructions"/> are returned as-is, so scripts
    /// authored purely as instruction lists need no step list to run.
    /// </summary>
    public List<ExpandedStep> Expand(ScriptDocument document, string scriptName)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Steps.Count == 0)
        {
            return document.Instructions
                .Select((instruction, index) => new ExpandedStep
                {
                    Instruction = instruction,
                    Condition = null,
                    Origin = $"脚本 '{scriptName}' 指令 {index}"
                })
                .ToList();
        }

        return Expand(document.Steps, scriptName, $"脚本 '{scriptName}'");
    }

    /// <summary>
    /// Expands a bare step list. Used for a rule's response actions, where the owner is a rule
    /// rather than a document — the expansion itself is identical, so both go through here.
    /// </summary>
    /// <param name="ownerLabel">Prefix for error messages, e.g. "规则 #2 [重试]".</param>
    public List<ExpandedStep> Expand(IReadOnlyList<ScriptStep> steps, string scriptName, string ownerLabel)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var result = new List<ExpandedStep>();

        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var origin = $"{ownerLabel} 步骤 {i}";
            step.Validate(origin);

            if (step.Action is not null)
            {
                result.Add(BuildStep(step.Action, scriptName, origin));
                continue;
            }

            var reference = step.BlockRef!;
            if (_blocks is null)
            {
                throw new ActionBlockResolutionException(
                    $"{origin} 引用了动作块 '{reference.BlockId}'，但未提供动作块库。");
            }

            var block = _blocks.Require(reference.BlockId, origin);
            for (var j = 0; j < block.Steps.Count; j++)
            {
                result.Add(BuildStep(
                    block.Steps[j],
                    scriptName,
                    $"{origin} → 动作块 '{block.Id}' 第 {j} 步"));
            }
        }

        return result;
    }

    private ExpandedStep BuildStep(ActionSpec spec, string scriptName, string origin)
    {
        var instruction = ActionSpecMapper.ToInstruction(spec);
        var conditionId = ReadConditionRef(spec);

        if (conditionId is null)
            return new ExpandedStep { Instruction = instruction, Condition = null, Origin = origin };

        if (_conditions is null)
        {
            throw new ConditionResolutionException(
                $"{origin} 引用了条件 '{conditionId}'，但未提供条件库。");
        }

        // Require() produces the AC-08 message: which script, which step, which missing Id.
        var condition = _conditions.Require(conditionId, scriptName, origin);
        return new ExpandedStep { Instruction = instruction, Condition = condition, Origin = origin };
    }

    /// <summary>
    /// Reads the ConditionRef parameter. Returns null when absent or blank.
    /// A condition reference on an action type that cannot use one is an error rather than
    /// something to ignore — ignoring it would run the action with no condition at all.
    /// </summary>
    private static string? ReadConditionRef(ActionSpec spec)
    {
        if (!spec.Parameters.TryGetValue(ActionSpecMapper.ConditionRefKey, out var raw))
            return null;

        var id = raw?.ToString();
        if (string.IsNullOrWhiteSpace(id))
            return null;

        if (!SupportsCondition(spec.Type))
        {
            throw new ConditionResolutionException(
                $"动作类型 {spec.Type} 不能引用共享条件（发现 ConditionRef = '{id}'）。" +
                $"可引用条件的类型: {string.Join(", ", ConditionCapableTypes)}。");
        }

        return id;
    }

    /// <summary>
    /// Action types whose handler performs a visual check. Only WaitImage today; WaitColor
    /// checks a pixel colour rather than a template, so a VisualConditionSpec does not apply.
    /// </summary>
    private static readonly CommandType[] ConditionCapableTypes = { CommandType.WaitImage };

    public static bool SupportsCondition(CommandType type) => ConditionCapableTypes.Contains(type);
}
