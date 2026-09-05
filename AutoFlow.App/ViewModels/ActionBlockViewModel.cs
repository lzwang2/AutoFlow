using System.Collections.ObjectModel;
using AutoFlow.App.Common;
using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;

namespace AutoFlow.App.ViewModels;

/// <summary>
/// Editable wrapper around a shared <see cref="ActionBlock"/>.
///
/// <para>Unlike <see cref="ConditionViewModel"/>, this does NOT edit the model in place. A block's
/// steps are an ordered list, and mutating the library's list as the user reorders rows would let
/// a half-finished edit reach any script that expands the block. Edits accumulate in
/// <see cref="Steps"/> and are pushed to the model by <see cref="ApplyToModel"/> on save.</para>
/// </summary>
public sealed class ActionBlockViewModel : ObservableObject
{
    private readonly ActionBlock _model;

    public ActionBlockViewModel(
        ActionBlock model,
        Func<string, VisualConditionSpec?>? lookupCondition = null,
        Func<string, ActionBlock?>? lookupBlock = null)
    {
        _model = model;
        Steps = new ObservableCollection<StepViewModel>(
            model.Steps.Select(spec => StepViewModel.ForAction(
                ActionSpecMapper.ToInstruction(spec),
                lookupCondition,
                lookupBlock,
                ReadConditionRef(spec))));

        Steps.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(StepCount));
            OnPropertyChanged(nameof(DisplayText));
        };
    }

    public ActionBlock Model => _model;

    /// <summary>
    /// Id is read-only, for the same reason a condition's is: renaming it would break every step
    /// pointing at the old value, and the rename would look like it succeeded.
    /// </summary>
    public string Id => _model.Id;

    public string Name
    {
        get => _model.Name;
        set { _model.Name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    public string? Description
    {
        get => _model.Description;
        set { _model.Description = value; OnPropertyChanged(); }
    }

    /// <summary>The editable step list. Block references are not offered: blocks are flat.</summary>
    public ObservableCollection<StepViewModel> Steps { get; }

    public int StepCount => Steps.Count;

    public string DisplayText => string.IsNullOrWhiteSpace(Name)
        ? $"{Id} ({StepCount} 步)"
        : $"{Id} — {Name} ({StepCount} 步)";

    /// <summary>
    /// Pushes the edited steps back onto the model, recomputing
    /// <see cref="ActionBlock.ConditionRefs"/> from the steps that actually reference a condition.
    /// Recomputed rather than hand-maintained because a stale dependency list is worse than none:
    /// it makes import-time validation pass for a condition the block no longer uses, and miss
    /// one it now does.
    /// </summary>
    public void ApplyToModel()
    {
        _model.Steps.Clear();
        var refs = new List<string>();

        foreach (var step in Steps)
        {
            if (step.Action is null)
                continue; // a block ref inside a block; not offered by the editor

            var spec = ActionSpecMapper.ToActionSpec(step.Action.Model);

            if (step.ConditionRef is { } conditionRef)
            {
                spec.Parameters[ActionSpecMapper.ConditionRefKey] = conditionRef;

                // The condition owns the image and thresholds, so the inline copies are dropped
                // rather than written alongside it — two sources of truth for one check.
                spec.Parameters.Remove(nameof(Core.Scripting.Instructions.Instruction.TemplatePath));
                spec.Parameters.Remove(nameof(Core.Scripting.Instructions.Instruction.MatchThreshold));
                spec.Parameters.Remove(nameof(Core.Scripting.Instructions.Instruction.MeanDiffThreshold));

                if (!refs.Contains(conditionRef, StringComparer.OrdinalIgnoreCase))
                    refs.Add(conditionRef);
            }

            _model.Steps.Add(spec);
        }

        _model.ConditionRefs = refs;
    }

    private static string? ReadConditionRef(ActionSpec spec)
        => spec.Parameters.TryGetValue(ActionSpecMapper.ConditionRefKey, out var raw)
            ? raw?.ToString()
            : null;
}
