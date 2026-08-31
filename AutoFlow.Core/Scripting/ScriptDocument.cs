using AutoFlow.Core.Scripting.Instructions;

namespace AutoFlow.Core.Scripting;

/// <summary>
/// Script document - the root JSON structure
/// </summary>
public sealed class ScriptDocument
{
    public string Schema { get; set; } = "autoflow/script";

    /// <summary>
    /// Format version. Defaults to <see cref="ScriptFormatVersion.Current"/> so a newly
    /// created script passes its own version gate on reload.
    /// </summary>
    public int FormatVersion { get; set; } = ScriptFormatVersion.Current;

    /// <summary>Stable identifier, used when this script is referenced from a template library.</summary>
    public string? Id { get; set; }

    /// <summary>Display name. Falls back to the filename when absent.</summary>
    public string? Name { get; set; }

    public ScriptMetadata Metadata { get; set; } = new();

    /// <summary>
    /// The execution-time step list. Stays <see cref="Instruction"/> per §11.2 — the 7
    /// handlers and the editor bindings read these fields directly.
    /// </summary>
    public List<Instruction> Instructions { get; set; } = new();

    /// <summary>
    /// The serialization-form step list: atomic actions and action-block references, in order.
    /// When non-empty this is authoritative and <see cref="Instructions"/> is derived from it
    /// at load time by <c>ScriptStepExpander</c>. Scripts that carry only
    /// <see cref="Instructions"/> keep working unchanged.
    /// </summary>
    public List<ScriptStep> Steps { get; set; } = new();

    /// <summary>
    /// Target window OUTER size (including title bar and borders) that the script was
    /// designed for. When loading this script, the tool should resize the window to these
    /// dimensions, then measure the actual client area to use as the coordinate reference.
    /// Defaults to 1920x1080.
    /// </summary>
    public int OuterWidth { get; set; } = 1920;
    public int OuterHeight { get; set; } = 1080;
}

/// <summary>
/// Script metadata
/// </summary>
public sealed class ScriptMetadata
{
    public string ScriptVersion { get; set; } = "1.0.0";
    public string? Description { get; set; }
    public string? TargetWindowTitle { get; set; }
    public List<string> Tags { get; set; } = new();
}
