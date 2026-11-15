using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Instructions;
using Newtonsoft.Json;

namespace AutoFlow.Core.Scripting.Actions;

/// <summary>
/// Raised when an action block reference cannot be resolved or expanded.
/// </summary>
public sealed class ActionBlockResolutionException : Exception
{
    public ActionBlockResolutionException(string message) : base(message) { }
}

/// <summary>
/// Loads and indexes shared <see cref="ActionBlock"/> files, and expands block references
/// into concrete instruction sequences for the interpreters.
///
/// Layout: &lt;TemplateRoot&gt;/&lt;Game&gt;/Actions/*.actionblock.json, one block per file.
/// </summary>
public sealed class ActionBlockLibrary
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore
    };

    // Aliases onto TemplateLayout so the layout has exactly one definition.
    public const string FileExtension = TemplateLayout.ActionBlockExtension;
    public const string SubDirectoryName = TemplateLayout.ActionsDirectoryName;

    private readonly Dictionary<string, ActionBlock> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _sourceFileById = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _templateRoot;

    public ActionBlockLibrary(string? templateRoot = null)
        => _templateRoot = templateRoot ?? TemplatePathResolver.TemplateRoot;

    public IReadOnlyCollection<ActionBlock> All => _byId.Values;

    public ActionBlockLibrary Snapshot()
    {
        var copy = new ActionBlockLibrary(_templateRoot);
        foreach (var (id, block) in _byId) copy._byId.Add(id, GameTaskScript.Copy(block));
        return copy;
    }

    public static string DirectoryFor(string templateRoot, string game)
        => Path.Combine(templateRoot, game, SubDirectoryName);

    public IReadOnlyList<string> LoadGame(string game)
    {
        var warnings = new List<string>();
        var directory = DirectoryFor(_templateRoot, game);
        if (!Directory.Exists(directory))
            return warnings;

        foreach (var file in Directory.GetFiles(directory, "*" + FileExtension))
        {
            ActionBlock? block;
            try
            {
                block = JsonConvert.DeserializeObject<ActionBlock>(File.ReadAllText(file));
            }
            catch (JsonException ex)
            {
                warnings.Add($"动作块解析失败: {file} — {ex.Message}");
                continue;
            }

            if (block is null)
            {
                warnings.Add($"动作块文件内容为空: {file}");
                continue;
            }

            if (string.IsNullOrWhiteSpace(block.Id))
            {
                warnings.Add($"动作块缺少 Id，已跳过: {file}");
                continue;
            }

            if (_sourceFileById.TryGetValue(block.Id, out var existing))
            {
                warnings.Add(
                    $"动作块 Id 重复: '{block.Id}' 同时出现在 {existing} 和 {file}，后者已忽略。");
                continue;
            }

            _byId[block.Id] = block;
            _sourceFileById[block.Id] = file;
        }

        return warnings;
    }

    public bool TryGet(string id, out ActionBlock? block) => _byId.TryGetValue(id, out block);

    /// <summary>
    /// Drops everything loaded so far. See <see cref="ConditionLibrary.Clear"/> for why an
    /// accumulating load needs this.
    /// </summary>
    public void Clear()
    {
        _byId.Clear();
        _sourceFileById.Clear();
    }

    /// <summary>
    /// Deletes a block's file and forgets it. Returns false when the Id is unknown.
    /// </summary>
    public bool Delete(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !_sourceFileById.TryGetValue(id, out var path))
            return false;

        if (File.Exists(path))
            File.Delete(path);

        _byId.Remove(id);
        _sourceFileById.Remove(id);
        return true;
    }

    public ActionBlock Require(string id, string referencingContext)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ActionBlockResolutionException($"{referencingContext} 的动作块引用为空。");

        if (_byId.TryGetValue(id, out var block))
            return block;

        var known = _byId.Count == 0
            ? "（动作块库为空，是否尚未加载对应游戏的 Actions 目录？）"
            : "已知动作块: " + string.Join(", ", _byId.Keys.Order());

        throw new ActionBlockResolutionException(
            $"{referencingContext} 引用了不存在的动作块 '{id}'。{known}");
    }

    /// <summary>
    /// Verifies that every condition a block depends on exists, so an imported block fails
    /// at import time with a clear message rather than mid-run.
    /// </summary>
    public IReadOnlyList<string> ValidateConditionRefs(ActionBlock block, ConditionLibrary conditions)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(conditions);

        var missing = new List<string>();
        foreach (var conditionId in block.ConditionRefs)
        {
            if (!conditions.TryGet(conditionId, out _))
                missing.Add(conditionId);
        }
        return missing;
    }

    /// <summary>
    /// Expands a mixed list of inline actions and block references into a flat instruction
    /// sequence for execution.
    ///
    /// Nested block references are rejected rather than followed: blocks are flat sequences
    /// in this round, and allowing nesting without cycle detection would risk infinite
    /// expansion.
    /// </summary>
    public List<Instruction> Expand(IEnumerable<object> steps, string referencingContext)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var result = new List<Instruction>();

        foreach (var step in steps)
        {
            switch (step)
            {
                case Instruction instruction:
                    result.Add(instruction);
                    break;

                case ActionSpec spec:
                    result.Add(ActionSpecMapper.ToInstruction(spec));
                    break;

                case ActionBlockRef reference:
                    var block = Require(reference.BlockId, referencingContext);
                    foreach (var blockStep in block.Steps)
                        result.Add(ActionSpecMapper.ToInstruction(blockStep));
                    break;

                default:
                    throw new ActionBlockResolutionException(
                        $"{referencingContext} 含无法识别的步骤类型 {step?.GetType().Name ?? "null"}。" +
                        $"步骤必须是 Instruction、ActionSpec 或 ActionBlockRef。");
            }
        }

        return result;
    }

    /// <summary>Writes a block to &lt;TemplateRoot&gt;/&lt;Game&gt;/Actions/ and returns the path.</summary>
    public string Save(ActionBlock block, string game)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (string.IsNullOrWhiteSpace(block.Id))
            throw new ArgumentException("动作块必须有 Id 才能保存。", nameof(block));

        var directory = DirectoryFor(_templateRoot, game);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, TemplateFileNames.ForActionBlockId(block.Id));
        File.WriteAllText(path, JsonConvert.SerializeObject(block, JsonSettings));

        _byId[block.Id] = block;
        _sourceFileById[block.Id] = path;
        return path;
    }

    /// <summary>The file a loaded block came from, or null if the Id is unknown.</summary>
    public string? SourceFileOf(string id)
        => _sourceFileById.TryGetValue(id, out var path) ? path : null;
}
