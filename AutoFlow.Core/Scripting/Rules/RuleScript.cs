using AutoFlow.Core.Scripting.Instructions;

namespace AutoFlow.Core.Scripting.Rules;

/// <summary>
/// A reactive dispatch script: an infinite loop that, each tick, checks a list of
/// rules top-to-bottom and executes the actions of the first rule whose condition matches.
/// Models the "which screen am I on right now?" pattern (see AHK MultiPlayerCoreProcess).
/// </summary>
public sealed class RuleScript
{
    public string Schema { get; set; } = "autoflow/rule-script";
    public int FormatVersion { get; set; } = 1;
    public RuleScriptMetadata Metadata { get; set; } = new();

    /// <summary>Rules are evaluated in order; first match wins.</summary>
    public List<Rule> Rules { get; set; } = new();

    /// <summary>Delay (ms) between dispatch ticks when no rule matched.</summary>
    public int IdleTickMs { get; set; } = 500;

    /// <summary>
    /// Target window OUTER size (including title bar and borders) that the script was
    /// designed for. When loading this script, the tool should resize the window to these
    /// dimensions, then measure the actual client area to use as the coordinate reference.
    /// Defaults to 1920x1080; scripts ported from AutoHotkey projects typically declare
    /// whatever outer size that project used (e.g. 750x1040 outer → ~734x1001 client).
    /// </summary>
    public int OuterWidth { get; set; } = 1920;
    public int OuterHeight { get; set; } = 1080;
}

public sealed class RuleScriptMetadata
{
    public string ScriptVersion { get; set; } = "1.0.0";
    public string? Description { get; set; }
    public string? TargetWindowTitle { get; set; }
    public string? Name { get; set; }
    public string? Author { get; set; }
    public string? CreatedAt { get; set; }
}

/// <summary>
/// One rule = a condition (template match, optionally region-scoped) plus an
/// ordered list of actions (reusing the existing Instruction handlers).
/// </summary>
public sealed class Rule
{
    /// <summary>Human-readable label, used in logs and the editor list.</summary>
    public string Name { get; set; } = "新规则";

    /// <summary>Optional description for this rule.</summary>
    public string? Description { get; set; }

    /// <summary>Disabled rules are skipped during evaluation.</summary>
    public bool Enabled { get; set; } = true;

    // === Condition: template match ===
    public string? TemplatePath { get; set; }
    public double MatchThreshold { get; set; } = 0.85;
    public double MeanDiffThreshold { get; set; } = 30.0;

    /// <summary>
    /// Optional match region in the script's reference coordinate space
    /// (see <see cref="RuleScript.OuterWidth"/>). When set, the template is
    /// only searched inside this rectangle (faster and avoids false positives). When any
    /// value is negative the whole frame is searched.
    /// </summary>
    public int RegionX1 { get; set; } = -1;
    public int RegionY1 { get; set; } = -1;
    public int RegionX2 { get; set; } = -1;
    public int RegionY2 { get; set; } = -1;

    public bool HasRegion => RegionX1 >= 0 && RegionY1 >= 0 && RegionX2 > RegionX1 && RegionY2 > RegionY1;

    /// <summary>
    /// When true, the rule fires at most once per run (models AHK's emoji_sent flag).
    /// </summary>
    public bool TriggerOnce { get; set; }

    // === Actions: reuse the linear instruction model + handlers ===
    public List<Instruction> Actions { get; set; } = new();
}
