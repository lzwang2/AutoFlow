using System.Collections.ObjectModel;
using AutoFlow.App.Common;
using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Instructions;
using AutoFlow.Core.Scripting.Rules;

namespace AutoFlow.App.ViewModels;

/// <summary>
/// Editable wrapper around a <see cref="Rule"/>.
///
/// <para>A rule no longer carries its own condition: it names one through
/// <see cref="ConditionRef"/> and the shared library owns the details. The lookups are
/// injected as delegates rather than library references so this view model stays testable and
/// does not have to know how the libraries are loaded.</para>
///
/// <para>Response actions are <see cref="StepViewModel"/>s, the same type the linear editor
/// uses, so an action block can be referenced from either side (FR-06).</para>
/// </summary>
public sealed class RuleViewModel : ObservableObject, IConditionConsumer
{
    private readonly Rule _model;
    private readonly Func<string, VisualConditionSpec?>? _lookupCondition;
    private readonly Func<string, ActionBlock?>? _lookupBlock;

    public RuleViewModel(
        Rule model,
        Func<string, VisualConditionSpec?>? lookupCondition = null,
        Func<string, ActionBlock?>? lookupBlock = null)
    {
        _model = model;
        _lookupCondition = lookupCondition;
        _lookupBlock = lookupBlock;

        Actions = new ObservableCollection<StepViewModel>();
        LoadActionsFromModel();
        Actions.CollectionChanged += (_, _) => SyncActions();
    }

    public Rule Model => _model;

    /// <summary>
    /// The response actions. Each entry is an action or a reference to a shared action block.
    /// </summary>
    public ObservableCollection<StepViewModel> Actions { get; }

    /// <summary>
    /// Reads whichever of the model's two action lists is authoritative. <c>Steps</c> wins when
    /// present, matching the interpreter — reading <c>Actions</c> first would show a stale copy
    /// of a rule that actually runs from its step list.
    /// </summary>
    private void LoadActionsFromModel()
    {
        if (_model.Steps.Count > 0)
        {
            for (var i = 0; i < _model.Steps.Count; i++)
            {
                var step = _model.Steps[i];
                step.Validate($"规则 '{_model.Name}' 动作 {i}");

                if (step.BlockRef is { } reference)
                {
                    Actions.Add(StepViewModel.ForBlock(
                        reference.BlockId, reference.Alias, _lookupCondition, _lookupBlock));
                    continue;
                }

                var spec = step.Action!;
                var conditionRef = spec.Parameters.TryGetValue(ActionSpecMapper.ConditionRefKey, out var raw)
                    ? raw?.ToString()
                    : null;

                Actions.Add(StepViewModel.ForAction(
                    ActionSpecMapper.ToInstruction(spec), _lookupCondition, _lookupBlock, conditionRef));
            }
            return;
        }

        foreach (var action in _model.Actions)
        {
            Actions.Add(StepViewModel.ForAction(action, _lookupCondition, _lookupBlock));
        }
    }

    /// <summary>
    /// Writes the editable list back into the model.
    ///
    /// <para>Uses the <c>Steps</c> form only when an entry actually needs it — a block or
    /// condition reference. A plain action list stays in <c>Actions</c>, so existing rule files
    /// are not rewritten into a different shape just by being opened and saved.</para>
    /// </summary>
    public void SyncActions()
    {
        if (Actions.Any(a => a.RequiresStepForm))
        {
            _model.Steps = Actions.Select(a => a.ToScriptStep()).ToList();

            // Both lists populated would leave it ambiguous which one ran; Steps wins, so
            // Actions is cleared rather than left holding a partial duplicate.
            _model.Actions.Clear();
        }
        else
        {
            _model.Steps.Clear();
            _model.Actions.Clear();
            foreach (var step in Actions)
            {
                _model.Actions.Add(step.Model!);
            }
        }

        OnPropertyChanged(nameof(Summary));
    }

    public string Name
    {
        get => _model.Name;
        set { _model.Name = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public string? Description
    {
        get => _model.Description;
        set { _model.Description = value; OnPropertyChanged(); }
    }

    public bool Enabled
    {
        get => _model.Enabled;
        set { _model.Enabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    /// <summary>
    /// Id of the shared condition this rule matches on. Empty means the rule is an
    /// unconditional catch-all, which is a legitimate configuration, not a missing value.
    /// </summary>
    public string? ConditionRef
    {
        get => _model.ConditionRef;
        set
        {
            _model.ConditionRef = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            OnPropertyChanged();
            RefreshConditionView();
        }
    }

    public bool TriggerOnce
    {
        get => _model.TriggerOnce;
        set { _model.TriggerOnce = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    /// <summary>The resolved condition, or null when unset or unresolvable.</summary>
    public VisualConditionSpec? Condition =>
        _model.UsesSharedCondition ? _lookupCondition?.Invoke(_model.ConditionRef!) : null;

    /// <summary>
    /// Describes the referenced condition for the editor. Distinguishes "no condition"
    /// from "condition named but not found" — the latter is a broken reference the user
    /// needs to see, and collapsing both into one message would hide it.
    /// </summary>
    public string ConditionSummary
    {
        get
        {
            if (!_model.UsesSharedCondition)
                return "无条件 (兜底规则，总是命中)";

            var condition = Condition;
            if (condition is null)
                return $"⚠ 条件 '{_model.ConditionRef}' 在当前条件库中找不到";

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
            var flag = Enabled ? "" : "[停用] ";
            var once = TriggerOnce ? " (仅一次)" : "";
            var condition = _model.UsesSharedCondition ? _model.ConditionRef! : "无条件";
            return $"{flag}若[{condition}] → {Actions.Count}个动作{once}";
        }
    }

    /// <summary>
    /// Re-reads everything derived from the shared libraries. Call after a library is reloaded
    /// or the referenced condition is edited, since this view model caches nothing but also
    /// cannot observe changes inside the library.
    /// </summary>
    public void RefreshConditionView()
    {
        OnPropertyChanged(nameof(Condition));
        OnPropertyChanged(nameof(ConditionSummary));
        OnPropertyChanged(nameof(Summary));

        foreach (var action in Actions)
        {
            action.RefreshConditionView();
        }
    }

    public void RefreshSummary()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(ConditionSummary));
    }

    /// <summary>
    /// Get the underlying model (syncing actions first)
    /// </summary>
    public Rule ToModel()
    {
        SyncActions();
        return _model;
    }
}
