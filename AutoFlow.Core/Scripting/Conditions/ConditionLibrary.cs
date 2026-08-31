using Newtonsoft.Json;

namespace AutoFlow.Core.Scripting.Conditions;

/// <summary>
/// Raised when a script references a condition Id that cannot be resolved.
/// Carries enough context to locate the offending reference (Spec AC-08).
/// </summary>
public sealed class ConditionResolutionException : Exception
{
    public ConditionResolutionException(string message) : base(message) { }
}

/// <summary>
/// Loads and indexes shared <see cref="VisualConditionSpec"/> files.
///
/// Layout: &lt;TemplateRoot&gt;/&lt;Game&gt;/Conditions/*.condition.json, one condition per
/// file — consistent with the existing one-template-per-.rules.json organisation, and
/// keeps diffs legible when a single condition is edited.
/// </summary>
public sealed class ConditionLibrary
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore
    };

    // Aliases onto TemplateLayout so the layout has exactly one definition.
    public const string FileExtension = TemplateLayout.ConditionExtension;
    public const string SubDirectoryName = TemplateLayout.ConditionsDirectoryName;

    private readonly Dictionary<string, VisualConditionSpec> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _sourceFileById = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _templateRoot;

    public ConditionLibrary(string? templateRoot = null)
        => _templateRoot = templateRoot ?? TemplatePathResolver.TemplateRoot;

    public IReadOnlyCollection<VisualConditionSpec> All => _byId.Values;

    public static string DirectoryFor(string templateRoot, string game)
        => Path.Combine(templateRoot, game, SubDirectoryName);

    /// <summary>
    /// Loads every condition under &lt;TemplateRoot&gt;/&lt;Game&gt;/Conditions/.
    /// Image paths are resolved to absolute; unresolvable ones are left as-is and reported,
    /// so a single broken image does not prevent the rest of the library from loading.
    /// </summary>
    public IReadOnlyList<string> LoadGame(string game)
    {
        var warnings = new List<string>();
        var directory = DirectoryFor(_templateRoot, game);
        if (!Directory.Exists(directory))
            return warnings;

        foreach (var file in Directory.GetFiles(directory, "*" + FileExtension))
        {
            VisualConditionSpec? condition;
            try
            {
                condition = JsonConvert.DeserializeObject<VisualConditionSpec>(File.ReadAllText(file));
            }
            catch (JsonException ex)
            {
                warnings.Add($"条件文件解析失败: {file} — {ex.Message}");
                continue;
            }

            if (condition is null)
            {
                warnings.Add($"条件文件内容为空: {file}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(condition.Id))
            {
                warnings.Add($"条件文件缺少 Id，已跳过: {file}");
                continue;
            }

            if (_sourceFileById.TryGetValue(condition.Id, out var existing))
            {
                warnings.Add(
                    $"条件 Id 重复: '{condition.Id}' 同时出现在 {existing} 和 {file}，后者已忽略。");
                continue;
            }

            // _templateRoot must be passed through: a library pointed at a non-default root
            // would otherwise resolve level 2 against the app directory instead of itself.
            var resolved = TemplatePathResolver.Resolve(condition.TemplatePath, directory, _templateRoot);
            if (resolved is null && !string.IsNullOrWhiteSpace(condition.TemplatePath))
            {
                warnings.Add(
                    $"条件 '{condition.Id}' 的图片无法定位: {condition.TemplatePath}。" +
                    $"已尝试: {TemplatePathResolver.DescribeAttempts(condition.TemplatePath, directory, _templateRoot)}");
            }
            else
            {
                condition.TemplatePath = resolved;
            }

            _byId[condition.Id] = condition;
            _sourceFileById[condition.Id] = file;
        }

        return warnings;
    }

    public bool TryGet(string id, out VisualConditionSpec? condition)
        => _byId.TryGetValue(id, out condition);

    /// <summary>
    /// Resolves a condition reference, throwing a message that names the referencing script
    /// and rule when it fails — so the user can find the broken reference (Spec AC-08).
    /// </summary>
    public VisualConditionSpec Require(string id, string referencingScript, string referencingRule)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ConditionResolutionException(
                $"脚本 '{referencingScript}' 的规则 '{referencingRule}' 的 ConditionRef 为空。" +
                $"若本意是无条件规则，请确认这是预期行为。");
        }

        if (_byId.TryGetValue(id, out var condition))
            return condition;

        var known = _byId.Count == 0
            ? "（条件库为空，是否尚未加载对应游戏的 Conditions 目录？）"
            : "已知条件: " + string.Join(", ", _byId.Keys.Order());

        throw new ConditionResolutionException(
            $"脚本 '{referencingScript}' 的规则 '{referencingRule}' 引用了不存在的条件 Id '{id}'。{known}");
    }

    /// <summary>
    /// Writes a condition to &lt;TemplateRoot&gt;/&lt;Game&gt;/Conditions/ and returns the path.
    /// <paramref name="warnings"/> reports a stored absolute <see cref="VisualConditionSpec.TemplatePath"/>:
    /// the file is still written, because refusing to save would lose the user's work, but an
    /// absolute path only resolves on the machine that produced it.
    /// </summary>
    public string Save(VisualConditionSpec condition, string game, out IReadOnlyList<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(condition);
        if (string.IsNullOrWhiteSpace(condition.Id))
            throw new ArgumentException("条件必须有 Id 才能保存。", nameof(condition));

        var collected = new List<string>();
        if (!string.IsNullOrWhiteSpace(condition.TemplatePath) && Path.IsPathRooted(condition.TemplatePath))
        {
            collected.Add(
                $"条件 '{condition.Id}' 的图片是绝对路径: {condition.TemplatePath}。" +
                $"建议改成 '{game}/{TemplateLayout.ImagesDirectoryName}/<文件名>' 形式——" +
                $"绝对路径只在当前机器上有效，且条件文件移动后无法复现。");
        }

        var directory = DirectoryFor(_templateRoot, game);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, TemplateFileNames.ForConditionId(condition.Id));
        File.WriteAllText(path, JsonConvert.SerializeObject(condition, JsonSettings));

        _byId[condition.Id] = condition;
        _sourceFileById[condition.Id] = path;

        warnings = collected;
        return path;
    }

    /// <summary>Overload for callers that do not inspect warnings.</summary>
    public string Save(VisualConditionSpec condition, string game) => Save(condition, game, out _);

    /// <summary>The file a loaded condition came from, or null if the Id is unknown.</summary>
    public string? SourceFileOf(string id)
        => _sourceFileById.TryGetValue(id, out var path) ? path : null;
}
