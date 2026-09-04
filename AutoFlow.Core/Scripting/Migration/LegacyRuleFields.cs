namespace AutoFlow.Core.Scripting.Migration;

/// <summary>
/// JSON property names of the inline condition fields a v1 rule carried.
///
/// These were <c>Rule</c> properties until Phase 7 removed them, so the migration tooling
/// can no longer spell them as <c>nameof(Rule.TemplatePath)</c>. They live here as explicit
/// string constants because they describe an on-disk format that is now frozen: v1 files in
/// git history and on users' machines still use exactly these names, and they must not drift
/// when the current model changes.
///
/// <para>Public because tests assert that migrated files no longer contain these fields, and
/// that check has to use the same names the migration strips — two independent lists would
/// let a rename pass both.</para>
/// </summary>
public static class LegacyRuleFields
{
    public const string TemplatePath = "TemplatePath";
    public const string MatchThreshold = "MatchThreshold";
    public const string MeanDiffThreshold = "MeanDiffThreshold";
    public const string RegionX1 = "RegionX1";
    public const string RegionY1 = "RegionY1";
    public const string RegionX2 = "RegionX2";
    public const string RegionY2 = "RegionY2";

    /// <summary>Every inline condition field, for bulk removal during migration.</summary>
    public static readonly string[] All =
    [
        TemplatePath,
        MatchThreshold,
        MeanDiffThreshold,
        RegionX1,
        RegionY1,
        RegionX2,
        RegionY2
    ];
}
