namespace AutoFlow.Core.Scripting.Conditions;

/// <summary>
/// Resolves relative template image paths to absolute ones.
///
/// Extracted verbatim from RuleScriptService.Load so shared conditions resolve images
/// identically to inline ones — the fallback order is deliberately unchanged, since
/// altering it would silently change how existing scripts resolve their images.
///
/// Two levels, in order:
///   1. Relative to the referencing file's own directory — for scripts carrying private
///      images (e.g. TestScripts/MultiPlayerGame.rules.json → TestScripts/images/welcome.png)
///   2. Relative to the app's RuleTemplates directory — for shared template images, which
///      is why library paths include the game segment ("LastCloudia/images/x.png") and
///      therefore survive the .rules.json file being moved
/// </summary>
public static class TemplatePathResolver
{
    public static string TemplateRoot =>
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RuleTemplates");

    /// <summary>
    /// Attempts to resolve <paramref name="templatePath"/>. Returns the absolute path on
    /// success, or null if no candidate exists. Empty and already-absolute paths are
    /// returned unchanged, matching the existing behaviour.
    /// </summary>
    /// <param name="templateRoot">
    /// The library root for level-2 resolution. Defaults to <see cref="TemplateRoot"/>.
    /// Callers holding their own root must pass it, otherwise level 2 silently checks the app
    /// directory instead of the library they were given.
    /// </param>
    public static string? Resolve(string? templatePath, string? referencingFileDirectory, string? templateRoot = null)
    {
        if (string.IsNullOrWhiteSpace(templatePath) || Path.IsPathRooted(templatePath))
            return templatePath;

        foreach (var candidate in Candidates(templatePath, referencingFileDirectory, templateRoot))
        {
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    /// <summary>
    /// The candidate paths tried by <see cref="Resolve"/>, in order. Exposed so failure
    /// messages can list exactly what was attempted.
    /// </summary>
    public static IEnumerable<string> Candidates(
        string templatePath, string? referencingFileDirectory, string? templateRoot = null)
    {
        if (!string.IsNullOrEmpty(referencingFileDirectory))
            yield return Path.Combine(referencingFileDirectory, templatePath);

        yield return Path.Combine(templateRoot ?? TemplateRoot, templatePath);
    }

    /// <summary>
    /// Human-readable description of the attempted locations, for error messages.
    /// </summary>
    public static string DescribeAttempts(
        string templatePath, string? referencingFileDirectory, string? templateRoot = null)
        => string.Join("; ", Candidates(templatePath, referencingFileDirectory, templateRoot));
}
