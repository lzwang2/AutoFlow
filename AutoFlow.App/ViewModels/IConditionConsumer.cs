namespace AutoFlow.App.ViewModels;

/// <summary>
/// Something in the editor that can point at a shared visual condition: a rule, or a
/// condition-style action step.
///
/// Exists so the condition picker is written once and works in both editors — the whole point
/// of FR-06 is that the same condition object is reusable from either, and a picker that only
/// knew about rules would quietly make the linear editor a second-class citizen.
/// </summary>
public interface IConditionConsumer
{
    /// <summary>Id of the referenced condition, or null for "no shared condition".</summary>
    string? ConditionRef { get; set; }

    /// <summary>Re-reads everything derived from the condition library.</summary>
    void RefreshConditionView();
}
