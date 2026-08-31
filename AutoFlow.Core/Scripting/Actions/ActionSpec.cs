using AutoFlow.Core.Scripting.Instructions;

namespace AutoFlow.Core.Scripting.Actions;

/// <summary>
/// Serialization form of a single atomic action, used by the action template library and
/// by <see cref="ActionBlock"/> for persistence and cross-script transfer.
///
/// This is NOT the execution-time model. <see cref="Instruction"/> stays the flat, strongly
/// typed DTO that the seven handlers and the editor bind to; ActionSpec exists purely so a
/// reusable action can be stored and referenced without dragging along every field of every
/// command type. Convert with <see cref="ActionSpecMapper"/>.
///
/// See Spec §11.2 for why Instruction is deliberately left unchanged.
/// </summary>
public sealed class ActionSpec
{
    /// <summary>Stable identifier, required only for actions stored in the template library.</summary>
    public string? Id { get; set; }

    public string? Name { get; set; }

    /// <summary>Reuses the existing enum rather than introducing a parallel one.</summary>
    public CommandType Type { get; set; }

    /// <summary>
    /// Parameter values keyed by the corresponding <see cref="Instruction"/> property name.
    /// Only parameters meaningful for <see cref="Type"/> are stored; see
    /// <see cref="ActionSpecMapper"/> for the per-type field lists.
    /// </summary>
    public Dictionary<string, object?> Parameters { get; set; } = new();

    public string? Notes { get; set; }

    /// <summary>Optional grouping for template library browsing.</summary>
    public string? Category { get; set; }
}
