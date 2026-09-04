using AutoFlow.App.Common;
using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Instructions;

namespace AutoFlow.App.ViewModels;

/// <summary>
/// One entry in an editable step list: either an atomic action or a reference to a shared
/// action block. Mirrors <see cref="ScriptStep"/>, which is the on-disk form of the same
/// union, so the editor can hold a reference instead of a copy.
///
/// <para>Both editors use this type. The linear script's instruction list and a rule's
/// response-action list are the same shape — an ordered list of actions that may reference
/// shared objects — and giving them one view model is what lets a block or condition be
/// reused across the two (FR-06).</para>
///
/// <para>A block reference is deliberately NOT expanded into its steps here. Expanding would
/// turn reuse back into duplication (NFR-01) and the reference would be lost on the next
/// save.</para>
/// </summary>
public sealed class StepViewModel : ObservableObject, IConditionConsumer
{
    private readonly Func<string, VisualConditionSpec?>? _lookupCondition;
    private readonly Func<string, ActionBlock?>? _lookupBlock;

    private string? _conditionRef;
    private string? _blockId;
    private string? _alias;

    private StepViewModel(
        InstructionViewModel? action,
        Func<string, VisualConditionSpec?>? lookupCondition,
        Func<string, ActionBlock?>? lookupBlock)
    {
        Action = action;
        _lookupCondition = lookupCondition;
        _lookupBlock = lookupBlock;

        if (action is not null)
        {
            // The action's own edits change this row's label, and the property panel edits the
            // action directly, so the two have to stay in step.
            action.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(Kind));
                OnPropertyChanged(nameof(Summary));
                OnPropertyChanged(nameof(SupportsCondition));
            };
        }
    }

    /// <summary>An ordinary action step.</summary>
    public static StepViewModel ForAction(
        Instruction instruction,
        Func<string, VisualConditionSpec?>? lookupCondition = null,
        Func<string, ActionBlock?>? lookupBlock = null,
        string? conditionRef = null)
        => new(new InstructionViewModel(instruction), lookupCondition, lookupBlock)
        {
            _conditionRef = string.IsNullOrWhiteSpace(conditionRef) ? null : conditionRef.Trim()
        };

    /// <summary>A step that runs a shared action block.</summary>
    public static StepViewModel ForBlock(
        string blockId,
        string? alias = null,
        Func<string, VisualConditionSpec?>? lookupCondition = null,
        Func<string, ActionBlock?>? lookupBlock = null)
        => new(null, lookupCondition, lookupBlock)
        {
            _blockId = blockId,
            _alias = alias
        };

    /// <summary>Null when this step is a block reference.</summary>
    public InstructionViewModel? Action { get; }

    public bool IsBlockRef => _blockId is not null;

    /// <summary>The referenced block's Id, or null for an action step.</summary>
    public string? BlockId => _blockId;

    /// <summary>The resolved block, or null when the Id is not in the loaded library.</summary>
    public ActionBlock? Block => _blockId is null ? null : _lookupBlock?.Invoke(_blockId);

    /// <summary>Left column of the step list: what kind of step this is.</summary>
    public string Kind => IsBlockRef ? "动作块" : Action!.CommandType.ToString();

    /// <summary>
    /// Whether this action type can point at a shared visual condition. Asks the expander
    /// rather than deciding here, so the editor offers exactly the types the interpreter
    /// accepts — offering more would produce a script that fails to load.
    /// </summary>
    public bool SupportsCondition =>
        !IsBlockRef && ScriptStepExpander.SupportsCondition(Action!.CommandType);

    /// <summary>
    /// Id of the shared condition this action checks. Null means the action uses its own
    /// inline values, which is still valid for a WaitImage authored with a template path.
    /// </summary>
    public string? ConditionRef
    {
        get => _conditionRef;
        set
        {
            _conditionRef = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            OnPropertyChanged();
            RefreshConditionView();
        }
    }

    public VisualConditionSpec? Condition =>
        _conditionRef is null ? null : _lookupCondition?.Invoke(_conditionRef);

    /// <summary>
    /// Describes what the step will actually check. Separates "no reference" from "reference
    /// that does not resolve" — the second is a broken script the user has to see.
    /// </summary>
    public string ConditionSummary
    {
        get
        {
            if (IsBlockRef)
                return string.Empty;

            if (_conditionRef is null)
            {
                return string.IsNullOrWhiteSpace(Action!.TemplatePath)
                    ? "未引用共享条件"
                    : $"内联图片 {System.IO.Path.GetFileName(Action.TemplatePath)}";
            }

            var condition = Condition;
            if (condition is null)
                return $"⚠ 条件 '{_conditionRef}' 在当前条件库中找不到";

            var region = condition.HasRegion
                ? $"区域 ({condition.RegionX1},{condition.RegionY1})-({condition.RegionX2},{condition.RegionY2})"
                : "整帧匹配";
            var image = string.IsNullOrWhiteSpace(condition.TemplatePath)
                ? "无图片"
                : System.IO.Path.GetFileName(condition.TemplatePath);
            return $"{image} · {region} · 阈值 {condition.MatchThreshold:F2}";
        }
    }

    public string Summary
    {
        get
        {
            if (IsBlockRef)
            {
                var block = Block;
                if (block is null)
                    return $"⚠ 动作块 '{_blockId}' 在当前动作块库中找不到";

                var label = !string.IsNullOrWhiteSpace(_alias) ? _alias! : block.Name;
                if (string.IsNullOrWhiteSpace(label)) label = block.Id;
                return $"{label} ({block.Steps.Count} 步)";
            }

            return _conditionRef is null
                ? Action!.Summary
                : $"{Action!.Summary} ← 条件 {_conditionRef}";
        }
    }

    /// <summary>
    /// Whether this step needs the <see cref="ScriptStep"/> form to be stored without loss.
    /// A plain action list is still written as an instruction list, which keeps existing files
    /// unchanged instead of rewriting every one of them into the step form.
    /// </summary>
    public bool RequiresStepForm => IsBlockRef || _conditionRef is not null;

    /// <summary>The underlying instruction, or null for a block reference.</summary>
    public Instruction? Model => Action?.Model;

    public ScriptStep ToScriptStep()
    {
        if (IsBlockRef)
            return ScriptStep.FromBlock(_blockId!, _alias);

        var spec = ActionSpecMapper.ToActionSpec(Action!.Model);

        if (_conditionRef is not null)
        {
            spec.Parameters[ActionSpecMapper.ConditionRefKey] = _conditionRef;

            // The referenced condition owns the image and thresholds, so the inline copies are
            // dropped rather than written alongside it. Keeping both would leave two sources of
            // truth for the same check, with only the handler's precedence deciding the winner.
            spec.Parameters.Remove(nameof(Instruction.TemplatePath));
            spec.Parameters.Remove(nameof(Instruction.MatchThreshold));
            spec.Parameters.Remove(nameof(Instruction.MeanDiffThreshold));
        }

        return ScriptStep.FromAction(spec);
    }

    /// <summary>Re-reads everything that comes from the shared libraries.</summary>
    public void RefreshConditionView()
    {
        OnPropertyChanged(nameof(Condition));
        OnPropertyChanged(nameof(ConditionSummary));
        OnPropertyChanged(nameof(Block));
        OnPropertyChanged(nameof(Summary));
    }
}
