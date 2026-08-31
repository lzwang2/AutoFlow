using System.Text;

namespace AutoFlow.Core.Scripting.Migration;

/// <summary>
/// Derives a shared-condition <c>Id</c> from a template image path.
///
/// The existing rules are named in Chinese («无法加入房间»), which does not make a usable
/// reference key, so the Id comes from the image filename in PascalCase, prefixed with the
/// game name. The Chinese name moves to the condition's <c>Name</c> field for display.
/// </summary>
public static class ConditionIdGenerator
{
    /// <summary>
    /// Filenames whose mechanical conversion would be misleading. <c>_area</c> is a
    /// screenshot-cropping convention, not part of the meaning, so <c>AbandonTaskArea</c> would
    /// read as if there were some other kind of "abandon task" condition.
    /// </summary>
    private static readonly Dictionary<string, string> Overrides = new(StringComparer.OrdinalIgnoreCase)
    {
        ["abandon_task_area.png"] = "AbandonTask",
        ["network_error_area.png"] = "NetworkError"
    };

    public static string FromTemplatePath(string game, string templatePath)
    {
        if (string.IsNullOrWhiteSpace(game))
            throw new ArgumentException("游戏名不能为空。", nameof(game));
        if (string.IsNullOrWhiteSpace(templatePath))
            throw new ArgumentException("模板图片路径不能为空。", nameof(templatePath));

        var fileName = Path.GetFileName(templatePath.Replace('\\', '/'));

        if (Overrides.TryGetValue(fileName, out var mapped))
            return $"{game}.{mapped}";

        var stem = Path.GetFileNameWithoutExtension(fileName);

        // Refuse to guess. A new *_area.png would otherwise silently produce an Id ending in
        // "Area", which the two hand-mapped cases above exist specifically to avoid.
        if (stem.EndsWith("_area", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"图片 '{fileName}' 以 _area 结尾，需要人工指定条件 Id 而不是机械转换。" +
                $"请在 {nameof(ConditionIdGenerator)}.{nameof(Overrides)} 中登记它。");
        }

        return $"{game}.{ToPascalCase(stem)}";
    }

    /// <summary>Converts snake_case / kebab-case / spaced names to PascalCase.</summary>
    public static string ToPascalCase(string name)
    {
        var builder = new StringBuilder(name.Length);
        var capitaliseNext = true;

        foreach (var ch in name)
        {
            if (ch is '_' or '-' or ' ' or '.')
            {
                capitaliseNext = true;
                continue;
            }

            builder.Append(capitaliseNext ? char.ToUpperInvariant(ch) : ch);
            capitaliseNext = false;
        }

        if (builder.Length == 0)
            throw new InvalidOperationException($"无法从 '{name}' 生成条件 Id。");

        return builder.ToString();
    }
}
