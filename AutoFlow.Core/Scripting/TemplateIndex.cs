using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Rules;
using Newtonsoft.Json;

namespace AutoFlow.Core.Scripting;

/// <summary>
/// One browsable row in the template library, whatever kind of template it describes
/// (Spec FR-06: conditions and action blocks must be browsable independently of rules).
/// </summary>
public sealed class TemplateIndexEntry
{
    public TemplateKind Kind { get; set; }

    /// <summary>Absolute path of the backing file.</summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Just the filename of <see cref="FilePath"/>, so a row can be matched against a file on
    /// disk. Separate from the path because the UI ellipsis-trims the path from the right —
    /// which is precisely where the filename lives, making it the first thing to vanish.
    /// </summary>
    public string FileName => string.IsNullOrEmpty(FilePath) ? "" : Path.GetFileName(FilePath);

    /// <summary>The &lt;Game&gt; directory segment this template lives under.</summary>
    public string Game { get; set; } = "";

    /// <summary>Display name: metadata name where present, else the filename.</summary>
    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>Free-form grouping label, from the object's Category/Tags.</summary>
    public string Category { get; set; } = "";

    /// <summary>
    /// The stable identifier for kinds that have one (conditions, action blocks). Empty for
    /// rules and linear scripts, which are addressed by file path.
    /// </summary>
    public string Id { get; set; } = "";

    /// <summary>Rules in a rule script, steps in a block, instructions in a script; 1 for a condition.</summary>
    public int ItemCount { get; set; }

    /// <summary>
    /// The <c>FormatVersion</c> the file declares, for the kinds that carry one (rule scripts and
    /// linear scripts). Conditions and action blocks have no version of their own and stay at
    /// <see cref="ScriptFormatVersion.Current"/>.
    ///
    /// <para>Indexing deliberately does not run the version gate — a file that cannot be loaded
    /// should still be visible, or the user is left wondering why a file they can see in Explorer
    /// is missing from the library. It is recorded here instead so the row can say so up front,
    /// rather than looking usable until the import fails.</para>
    /// </summary>
    public int FormatVersion { get; set; } = ScriptFormatVersion.Current;

    /// <summary>Whether this template can actually be loaded by this build.</summary>
    public bool IsSupportedVersion => FormatVersion == ScriptFormatVersion.Current;

    public string KindLabel => Kind switch
    {
        TemplateKind.Rule => "规则",
        TemplateKind.Condition => "条件",
        TemplateKind.ActionBlock => "动作块",
        TemplateKind.LinearScript => "线性脚本",
        _ => Kind.ToString()
    };

    /// <summary>
    /// Why this row cannot be used, or empty when it can. Shown next to the row so an
    /// unusable template is visibly unusable before the user clicks it.
    /// </summary>
    public string UnsupportedReason => IsSupportedVersion
        ? ""
        : $"⚠ 旧格式 (FormatVersion {FormatVersion})，需迁移后才能使用";

    public string DisplayText => Kind switch
    {
        TemplateKind.Rule => $"{Name} ({ItemCount} 个规则)",
        TemplateKind.Condition => string.IsNullOrEmpty(Id) ? Name : $"{Name} [{Id}]",
        TemplateKind.ActionBlock => $"{Name} ({ItemCount} 步)",
        TemplateKind.LinearScript => $"{Name} ({ItemCount} 条指令)",
        _ => Name
    };
}

/// <summary>
/// Scans a game's template directories and produces one flat, filterable list covering all
/// four kinds. Nothing here executes or resolves anything — it exists so the UI can answer
/// "what templates exist, of what kind?" without loading four libraries and unioning them
/// by hand, and so rule templates can never be confused with linear script templates
/// (they come from different directories and carry different <see cref="TemplateKind"/>).
/// </summary>
public sealed class TemplateIndex
{
    private readonly string _templateRoot;

    public TemplateIndex(string? templateRoot = null)
        => _templateRoot = templateRoot ?? TemplatePathResolver.TemplateRoot;

    public string TemplateRoot => _templateRoot;

    /// <summary>Games are the directories directly under the template root.</summary>
    public IReadOnlyList<string> Games()
    {
        if (!Directory.Exists(_templateRoot))
            return Array.Empty<string>();

        return Directory.GetDirectories(_templateRoot)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .Cast<string>()
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Builds the index for one game. Unparseable files are reported through
    /// <paramref name="warnings"/> and omitted, never thrown — a broken file must not make
    /// the whole library unbrowsable.
    /// </summary>
    public IReadOnlyList<TemplateIndexEntry> Build(string game, out IReadOnlyList<string> warnings)
    {
        var collected = new List<string>();
        var entries = new List<TemplateIndexEntry>();

        entries.AddRange(ScanRules(game, collected));
        entries.AddRange(ScanConditions(game, collected));
        entries.AddRange(ScanActionBlocks(game, collected));
        entries.AddRange(ScanLinearScripts(game, collected));

        warnings = collected;
        return entries
            .OrderBy(e => e.Kind)
            .ThenBy(e => e.Category, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Every game's entries, concatenated.</summary>
    public IReadOnlyList<TemplateIndexEntry> BuildAll(out IReadOnlyList<string> warnings)
    {
        var collected = new List<string>();
        var entries = new List<TemplateIndexEntry>();

        foreach (var game in Games())
        {
            entries.AddRange(Build(game, out var gameWarnings));
            collected.AddRange(gameWarnings);
        }

        warnings = collected;
        return entries;
    }

    /// <summary>Convenience filter for a single kind, e.g. "show me only conditions".</summary>
    public IReadOnlyList<TemplateIndexEntry> Build(string game, TemplateKind kind, out IReadOnlyList<string> warnings)
        => Build(game, out warnings).Where(e => e.Kind == kind).ToList();

    private IEnumerable<TemplateIndexEntry> ScanRules(string game, List<string> warnings)
    {
        // Pre-migration, rule files sit directly in <Game>/; Phase 6 moves them into
        // <Game>/Rules/. Both are scanned so the library stays browsable throughout the
        // migration rather than appearing empty between the two steps.
        var directories = new[]
        {
            TemplateLayout.DirectoryFor(_templateRoot, game, TemplateKind.Rule),
            Path.Combine(_templateRoot, game)
        };

        foreach (var directory in directories.Where(Directory.Exists))
        {
            foreach (var file in Directory.GetFiles(directory, "*" + TemplateLayout.RuleExtension))
            {
                RuleScript? script;
                try
                {
                    script = JsonConvert.DeserializeObject<RuleScript>(File.ReadAllText(file));
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    warnings.Add($"规则模板解析失败: {file} — {ex.Message}");
                    continue;
                }

                if (script?.Rules is null)
                {
                    warnings.Add($"规则模板缺少 Rules 节点: {file}");
                    continue;
                }

                yield return new TemplateIndexEntry
                {
                    Kind = TemplateKind.Rule,
                    FilePath = file,
                    Game = game,
                    Name = script.Metadata?.Name ?? Path.GetFileNameWithoutExtension(file),
                    Description = script.Metadata?.Description ?? "",
                    ItemCount = script.Rules.Count,
                    FormatVersion = script.FormatVersion
                };
            }
        }
    }

    private IEnumerable<TemplateIndexEntry> ScanConditions(string game, List<string> warnings)
    {
        var directory = ConditionLibrary.DirectoryFor(_templateRoot, game);
        if (!Directory.Exists(directory))
            yield break;

        foreach (var file in Directory.GetFiles(directory, "*" + TemplateLayout.ConditionExtension))
        {
            VisualConditionSpec? condition;
            try
            {
                condition = JsonConvert.DeserializeObject<VisualConditionSpec>(File.ReadAllText(file));
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                warnings.Add($"条件模板解析失败: {file} — {ex.Message}");
                continue;
            }

            if (condition is null)
            {
                warnings.Add($"条件模板内容为空: {file}");
                continue;
            }

            yield return new TemplateIndexEntry
            {
                Kind = TemplateKind.Condition,
                FilePath = file,
                Game = game,
                Id = condition.Id,
                Name = string.IsNullOrWhiteSpace(condition.Name) ? condition.Id : condition.Name,
                Description = condition.Description ?? "",
                Category = condition.Category ?? "",
                ItemCount = 1
            };
        }
    }

    private IEnumerable<TemplateIndexEntry> ScanActionBlocks(string game, List<string> warnings)
    {
        var directory = ActionBlockLibrary.DirectoryFor(_templateRoot, game);
        if (!Directory.Exists(directory))
            yield break;

        foreach (var file in Directory.GetFiles(directory, "*" + TemplateLayout.ActionBlockExtension))
        {
            ActionBlock? block;
            try
            {
                block = JsonConvert.DeserializeObject<ActionBlock>(File.ReadAllText(file));
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                warnings.Add($"动作块模板解析失败: {file} — {ex.Message}");
                continue;
            }

            if (block is null)
            {
                warnings.Add($"动作块模板内容为空: {file}");
                continue;
            }

            yield return new TemplateIndexEntry
            {
                Kind = TemplateKind.ActionBlock,
                FilePath = file,
                Game = game,
                Id = block.Id,
                Name = string.IsNullOrWhiteSpace(block.Name) ? block.Id : block.Name,
                Description = block.Description ?? "",
                Category = block.Category ?? "",
                ItemCount = block.Steps.Count
            };
        }
    }

    private IEnumerable<TemplateIndexEntry> ScanLinearScripts(string game, List<string> warnings)
    {
        var library = new ScriptTemplateLibrary(_templateRoot);
        var entries = library.List(game, out var scriptWarnings);
        warnings.AddRange(scriptWarnings);
        return entries;
    }
}
