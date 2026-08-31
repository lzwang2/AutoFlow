namespace AutoFlow.Core.Scripting;

/// <summary>
/// What kind of object a template file holds. Used by <see cref="TemplateIndex"/> so the
/// four template kinds can be browsed separately instead of being lumped into one list
/// (Spec FR-06).
/// </summary>
public enum TemplateKind
{
    /// <summary>A rule-loop script (*.rules.json) under &lt;Game&gt;/Rules/.</summary>
    Rule,

    /// <summary>A shared visual condition (*.condition.json) under &lt;Game&gt;/Conditions/.</summary>
    Condition,

    /// <summary>A reusable action block (*.actionblock.json) under &lt;Game&gt;/Actions/.</summary>
    ActionBlock,

    /// <summary>A linear instruction script (*.json) under &lt;Game&gt;/Scripts/.</summary>
    LinearScript
}

/// <summary>
/// Single source of truth for the template library layout (Spec §6.4):
///
/// <code>
/// &lt;TemplateRoot&gt;/            = &lt;AppBase&gt;/RuleTemplates
///   &lt;Game&gt;/
///     images/                  image assets — position deliberately unchanged
///     Rules/       *.rules.json
///     Conditions/  *.condition.json
///     Actions/     *.actionblock.json
///     Scripts/     *.json
/// </code>
///
/// The subdirectory names and extensions were previously spelled out in each service.
/// Centralising them here means a layout change cannot leave one service reading from a
/// directory another writes to.
///
/// <para><b>images/ stays put.</b> Every existing template writes its image path as
/// "&lt;Game&gt;/images/&lt;file&gt;.png", resolved against the template root by
/// <see cref="Conditions.TemplatePathResolver"/> level 2. Moving images would invalidate
/// all of those strings for no benefit.</para>
/// </summary>
public static class TemplateLayout
{
    public const string ImagesDirectoryName = "images";
    public const string RulesDirectoryName = "Rules";
    public const string ConditionsDirectoryName = "Conditions";
    public const string ActionsDirectoryName = "Actions";
    public const string ScriptsDirectoryName = "Scripts";

    public const string RuleExtension = ".rules.json";
    public const string ConditionExtension = ".condition.json";
    public const string ActionBlockExtension = ".actionblock.json";
    public const string LinearScriptExtension = ".json";

    public static string SubDirectoryNameFor(TemplateKind kind) => kind switch
    {
        TemplateKind.Rule => RulesDirectoryName,
        TemplateKind.Condition => ConditionsDirectoryName,
        TemplateKind.ActionBlock => ActionsDirectoryName,
        TemplateKind.LinearScript => ScriptsDirectoryName,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知的模板类型。")
    };

    public static string ExtensionFor(TemplateKind kind) => kind switch
    {
        TemplateKind.Rule => RuleExtension,
        TemplateKind.Condition => ConditionExtension,
        TemplateKind.ActionBlock => ActionBlockExtension,
        TemplateKind.LinearScript => LinearScriptExtension,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "未知的模板类型。")
    };

    /// <summary>&lt;templateRoot&gt;/&lt;game&gt;/&lt;kind's subdirectory&gt;</summary>
    public static string DirectoryFor(string templateRoot, string game, TemplateKind kind)
        => Path.Combine(templateRoot, game, SubDirectoryNameFor(kind));

    /// <summary>&lt;templateRoot&gt;/&lt;game&gt;/images</summary>
    public static string ImagesDirectoryFor(string templateRoot, string game)
        => Path.Combine(templateRoot, game, ImagesDirectoryName);

    /// <summary>
    /// Classifies a template file by its extension and containing directory.
    /// Returns null for files that are not templates.
    ///
    /// Extension order matters: ".rules.json" also ends with ".json", so rule scripts must
    /// be tested first or they would be misfiled as linear scripts. Beyond that, a bare
    /// "*.json" only counts as a linear script when it actually sits in a Scripts/
    /// directory — otherwise every stray json file in the tree would be indexed.
    /// </summary>
    public static TemplateKind? ClassifyFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;

        var fileName = Path.GetFileName(filePath);

        if (fileName.EndsWith(RuleExtension, StringComparison.OrdinalIgnoreCase))
            return TemplateKind.Rule;
        if (fileName.EndsWith(ConditionExtension, StringComparison.OrdinalIgnoreCase))
            return TemplateKind.Condition;
        if (fileName.EndsWith(ActionBlockExtension, StringComparison.OrdinalIgnoreCase))
            return TemplateKind.ActionBlock;

        if (fileName.EndsWith(LinearScriptExtension, StringComparison.OrdinalIgnoreCase))
        {
            var parent = Path.GetFileName(Path.GetDirectoryName(filePath));
            if (string.Equals(parent, ScriptsDirectoryName, StringComparison.OrdinalIgnoreCase))
                return TemplateKind.LinearScript;
        }

        return null;
    }

    /// <summary>
    /// The game segment a template file belongs to, i.e. the directory directly under
    /// <paramref name="templateRoot"/>. Returns null when the file is outside the root, or
    /// sits directly in it (a pre-migration layout that has no game segment).
    /// </summary>
    public static string? GameOf(string templateRoot, string filePath)
    {
        var rootFull = Path.GetFullPath(templateRoot);
        var fileFull = Path.GetFullPath(filePath);

        if (!fileFull.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            return null;

        var relative = Path.GetRelativePath(rootFull, fileFull);
        var segments = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        // Need at least <game>/<file>; a single segment means the file is in the root itself.
        return segments.Length >= 2 ? segments[0] : null;
    }
}
