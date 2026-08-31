namespace AutoFlow.Core.Scripting.Migration;

/// <summary>
/// Outcome of a migration run. Counts are reported rather than merely logged because
/// "how many duplicate conditions were actually merged" is the measurable benefit the
/// refactor claims (NFR-01) — a run that merges nothing has not achieved anything.
/// </summary>
public sealed class MigrationReport
{
    public List<string> Log { get; } = new();
    public List<string> Errors { get; } = new();

    /// <summary>
    /// Scripts actually rewritten by this run. Equivalence checking is meaningful only for
    /// these: a script that was already current has no legacy form to compare against, so
    /// checking it would compare a migrated file with itself read as if it were v1.
    /// </summary>
    public List<string> MigratedPaths { get; } = new();

    public int ScriptsMigrated { get; set; }
    public int ScriptsAlreadyCurrent { get; set; }
    public int ConditionsWritten { get; set; }
    public int DuplicatesMerged { get; set; }
    public int ImagesPromoted { get; set; }
    public int CatchAllRules { get; set; }

    public bool Success => Errors.Count == 0;

    public string Summarize()
    {
        var lines = new List<string>
        {
            $"迁移脚本: {ScriptsMigrated} 份（已是新版跳过 {ScriptsAlreadyCurrent} 份）",
            $"写出条件: {ConditionsWritten} 个，合并重复 {DuplicatesMerged} 份",
            $"提升图片到库内: {ImagesPromoted} 张",
            $"兜底规则（无条件）: {CatchAllRules} 条"
        };

        if (Errors.Count > 0)
        {
            lines.Add($"错误 {Errors.Count} 条:");
            lines.AddRange(Errors.Select(e => "  - " + e));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
