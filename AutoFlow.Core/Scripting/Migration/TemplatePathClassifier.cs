namespace AutoFlow.Core.Scripting.Migration;

/// <summary>The shapes of <c>TemplatePath</c> found in the pre-migration scripts.</summary>
public enum TemplatePathKind
{
    /// <summary>Empty or null — an unconditional catch-all rule.</summary>
    None,

    /// <summary>Includes the game segment, resolves against the template root: "LastCloudia/images/x.png".</summary>
    LibraryRelative,

    /// <summary>Resolves against the referencing file's own directory: "images/welcome.png".</summary>
    FileRelative,

    /// <summary>A rooted path. Only valid on the machine that produced it.</summary>
    Absolute
}

/// <summary>
/// Classifies a <c>TemplatePath</c> so the migrator can rewrite it correctly, and refuses
/// anything outside the four known shapes rather than guessing.
/// </summary>
public static class TemplatePathClassifier
{
    public static TemplatePathKind Classify(string? templatePath, string game)
    {
        if (string.IsNullOrWhiteSpace(templatePath))
            return TemplatePathKind.None;

        if (Path.IsPathRooted(templatePath))
            return TemplatePathKind.Absolute;

        var normalized = templatePath.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 2)
        {
            throw new InvalidOperationException(
                $"无法识别的图片路径形态: '{templatePath}'。" +
                $"预期是 '<游戏>/{TemplateLayout.ImagesDirectoryName}/<文件>' 或 " +
                $"'{TemplateLayout.ImagesDirectoryName}/<文件>'，迁移工具不猜测。");
        }

        if (string.Equals(segments[0], game, StringComparison.OrdinalIgnoreCase))
            return TemplatePathKind.LibraryRelative;

        if (string.Equals(segments[0], TemplateLayout.ImagesDirectoryName, StringComparison.OrdinalIgnoreCase))
            return TemplatePathKind.FileRelative;

        throw new InvalidOperationException(
            $"无法识别的图片路径形态: '{templatePath}'（首段 '{segments[0]}' 既不是游戏名 '{game}' " +
            $"也不是 '{TemplateLayout.ImagesDirectoryName}'）。迁移工具不猜测，请人工确认。");
    }

    /// <summary>
    /// The path a shared condition should store: always library-relative, because a condition
    /// file lives in &lt;Game&gt;/Conditions/ while the images stay in &lt;Game&gt;/images/, so
    /// file-relative paths would no longer resolve from there.
    /// </summary>
    public static string ToLibraryRelative(string? templatePath, string game)
    {
        var kind = Classify(templatePath, game);
        if (kind == TemplatePathKind.None)
            throw new InvalidOperationException("空路径没有对应的库内相对路径。");

        var fileName = Path.GetFileName(templatePath!.Replace('\\', '/'));
        return $"{game}/{TemplateLayout.ImagesDirectoryName}/{fileName}";
    }
}
