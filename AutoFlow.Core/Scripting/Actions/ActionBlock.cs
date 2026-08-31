namespace AutoFlow.Core.Scripting.Actions;

/// <summary>
/// An ordered, reusable sequence of atomic actions — the "click confirm, wait, click back"
/// kind of fragment that otherwise gets retyped into every script.
///
/// Not parameterised in this round: a block is a fixed sequence. Parameterisation would
/// require a substitution model and is a separate concern.
/// </summary>
public sealed class ActionBlock
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public List<ActionSpec> Steps { get; set; } = new();

    /// <summary>
    /// Ids of the visual conditions this block depends on — Ids, never copies of the
    /// condition objects. Storing copies would degrade reuse back into duplication, which is
    /// exactly what NFR-01 forbids.
    ///
    /// Purpose: when a block is imported into a script, its condition dependencies can be
    /// validated up front instead of failing mid-run.
    /// </summary>
    public List<string> ConditionRefs { get; set; } = new();

    /// <summary>Optional grouping for template library browsing.</summary>
    public string? Category { get; set; }
}

/// <summary>
/// A reference to an <see cref="ActionBlock"/> from a rule's action list or a linear
/// script's step list. Kept as a distinct type so a step list can hold both inline actions
/// and block references without the two being confused.
/// </summary>
public sealed class ActionBlockRef
{
    public string BlockId { get; set; } = string.Empty;

    /// <summary>Optional label shown in the editor in place of the block's own name.</summary>
    public string? Alias { get; set; }
}
