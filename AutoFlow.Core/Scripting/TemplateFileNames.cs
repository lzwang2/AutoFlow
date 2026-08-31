namespace AutoFlow.Core.Scripting;

/// <summary>
/// Filename helpers shared by every template writer, so rule, condition, action-block and
/// linear-script files sanitise identically. Previously each service carried its own copy.
/// </summary>
public static class TemplateFileNames
{
    /// <summary>
    /// Strips characters the filesystem rejects, joining the surviving runs with '_'.
    /// </summary>
    public static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("名称不能为空。", nameof(name));

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", name.Split(invalid, StringSplitOptions.RemoveEmptyEntries));

        if (string.IsNullOrWhiteSpace(sanitized))
            throw new ArgumentException($"名称 '{name}' 全部由非法字符组成，无法作为文件名。", nameof(name));

        return sanitized;
    }

    /// <summary>
    /// Turns a condition Id into its filename, e.g. "LastCloudia.UnableToJoin" →
    /// "LastCloudia_UnableToJoin.condition.json". Dots become underscores so the Id's
    /// namespace separator does not read as part of the extension chain.
    /// </summary>
    public static string ForConditionId(string id)
        => Sanitize(id).Replace('.', '_') + TemplateLayout.ConditionExtension;

    /// <summary>Same convention as <see cref="ForConditionId"/>, for action blocks.</summary>
    public static string ForActionBlockId(string id)
        => Sanitize(id).Replace('.', '_') + TemplateLayout.ActionBlockExtension;
}
