using Newtonsoft.Json;

namespace AutoFlow.Core.Scripting.Conditions;

/// <summary>
/// A reusable visual match condition: "what is this condition, and how is it judged".
///
/// This is the shared foundation referenced by both script models — the rule loop's
/// condition node and the linear script's WaitImage action point at the same object,
/// so a template path and its thresholds are defined exactly once.
///
/// Deliberately excludes timeout: the same condition may be polled for 30s in one
/// linear script, 3s in another, and not waited on at all in a rule loop. Timeout
/// belongs to the call site (see Spec FR-09).
/// </summary>
public sealed class VisualConditionSpec
{
    /// <summary>
    /// Stable identifier used for references, e.g. "LastCloudia.UnableToJoin".
    /// References use Id rather than Name so that renaming for display does not
    /// break existing scripts.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable label for the editor and logs.</summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Template image path as authored, relative to the template root (RuleTemplates/),
    /// including the game segment — e.g. "LastCloudia/images/unable_to_join.png".
    ///
    /// This is the persisted form and is never overwritten with a resolved absolute path:
    /// the editor loads a condition, edits it and writes it back, so mutating this field
    /// during load would bake a machine-specific absolute path into the file (see
    /// <see cref="ResolvedTemplatePath"/>).
    /// </summary>
    public string? TemplatePath { get; set; }

    /// <summary>
    /// Absolute path the loader found for <see cref="TemplatePath"/>, or null when the
    /// condition has not been resolved. Runtime-only, deliberately not serialized.
    /// </summary>
    [JsonIgnore]
    public string? ResolvedTemplatePath { get; set; }

    /// <summary>
    /// The path to actually open: the resolved one when available, otherwise the authored
    /// value. Matching and file existence checks must go through this, never
    /// <see cref="TemplatePath"/> directly.
    /// </summary>
    [JsonIgnore]
    public string? EffectiveTemplatePath =>
        !string.IsNullOrWhiteSpace(ResolvedTemplatePath) ? ResolvedTemplatePath : TemplatePath;

    // === Match region, in the reference coordinate space declared below ===

    public int RegionX1 { get; set; } = -1;
    public int RegionY1 { get; set; } = -1;
    public int RegionX2 { get; set; } = -1;
    public int RegionY2 { get; set; } = -1;

    /// <summary>
    /// Whether a valid match region is set. Mirrors Rule.HasRegion exactly: any negative
    /// origin or non-positive extent means "search the whole frame".
    /// </summary>
    public bool HasRegion => RegionX1 >= 0 && RegionY1 >= 0 && RegionX2 > RegionX1 && RegionY2 > RegionY1;

    public double MatchThreshold { get; set; } = 0.85;
    public double MeanDiffThreshold { get; set; } = 30.0;

    /// <summary>
    /// The reference space these region coordinates belong to: the target window OUTER
    /// size of the script the condition was authored against.
    ///
    /// Required because sharing breaks an assumption that inline conditions got for free.
    /// An inline condition's coordinates were implicitly guaranteed to match its own
    /// script's OuterWidth/OuterHeight; once shared across scripts that guarantee is gone,
    /// so the condition has to carry its own reference space and callers must verify it
    /// (see Spec FR-10). Zero means "unspecified" — only valid when no region is set.
    /// </summary>
    public int ReferenceOuterWidth { get; set; }
    public int ReferenceOuterHeight { get; set; }

    public List<string> Tags { get; set; } = new();

    /// <summary>Optional grouping for template library browsing.</summary>
    public string? Category { get; set; }
}
