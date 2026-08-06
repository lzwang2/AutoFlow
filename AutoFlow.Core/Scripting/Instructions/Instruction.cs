namespace AutoFlow.Core.Scripting.Instructions;

/// <summary>
/// Instruction command types for automation scripts
/// </summary>
public enum CommandType
{
    /// <summary>Click at script coordinate</summary>
    Click,

    /// <summary>Wait for template image to appear</summary>
    WaitImage,

    /// <summary>Wait for specific pixel color</summary>
    WaitColor,

    /// <summary>Wait for fixed time</summary>
    WaitTime,

    /// <summary>Loop back to earlier instruction</summary>
    Loop,

    /// <summary>Call a hardcoded skill (complex state machine)</summary>
    CallSkill,

    /// <summary>Comment (not executed)</summary>
    Comment
}

/// <summary>
/// Mouse button for click instructions
/// </summary>
public enum MouseButton
{
    Left,
    Right,
    Middle
}

/// <summary>
/// Unified instruction DTO - all fields are optional, used based on CommandType
/// </summary>
public sealed class Instruction
{
    // Common
    public CommandType CommandType { get; set; }
    public string? Notes { get; set; }
    public int IntervalToNextInstructionMs { get; set; } = 100;

    // Click
    public double PositionX { get; set; }
    public double PositionY { get; set; }
    public MouseButton MouseButton { get; set; } = MouseButton.Left;
    public int ClickCount { get; set; } = 1;

    // WaitImage
    public string? TemplatePath { get; set; }
    public double MatchThreshold { get; set; } = 0.85;
    public double MeanDiffThreshold { get; set; } = 30.0;
    public int WaitTimeoutMs { get; set; } = 10000;
    public bool ClickAfterMatch { get; set; }

    // WaitColor
    public string? WaitColorHex { get; set; }
    public int WaitColorTolerance { get; set; } = 15;

    // WaitTime
    public int WaitTimeMilliseconds { get; set; }

    // Loop
    public int LoopBackToIndex { get; set; }
    public int LoopCount { get; set; } = -1; // -1 = infinite

    // CallSkill
    public string? SkillName { get; set; }
    public Dictionary<string, object>? SkillParameters { get; set; }

    // Comment
    public string? CommentContent { get; set; }
}
