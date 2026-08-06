using AutoFlow.Core.Scripting.Instructions;

namespace AutoFlow.Core.Scripting;

/// <summary>
/// Script document - the root JSON structure
/// </summary>
public sealed class ScriptDocument
{
    public string Schema { get; set; } = "autoflow/script";
    public int FormatVersion { get; set; } = 1;
    public ScriptMetadata Metadata { get; set; } = new();
    public List<Instruction> Instructions { get; set; } = new();

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
