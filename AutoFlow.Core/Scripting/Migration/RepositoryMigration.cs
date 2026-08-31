using AutoFlow.Core.Scripting.Conditions;

namespace AutoFlow.Core.Scripting.Migration;

/// <summary>
/// Drives the one-time migration of this repository's rule scripts (plan Phase 6, work items
/// 1–3). Encodes which files are migrated and where their conditions land, so the run is
/// reproducible rather than a sequence of ad-hoc commands.
///
/// <para>All conditions go into the <c>LastCloudia</c> library. The two scripts under
/// <c>TestScripts/</c> are Last Cloudia screens as well, so giving them a separate home would
/// prevent exactly the deduplication this refactor is for — <c>test.rules.json</c> detects the
/// same network-error popup as <c>network_error.rules.json</c>.</para>
/// </summary>
public sealed class RepositoryMigration
{
    public const string ConditionGame = "LastCloudia";

    private readonly string _root;

    public RepositoryMigration(string root) => _root = root;

    public string TemplateRoot => Path.Combine(_root, "RuleTemplates");

    private string GameDirectory => Path.Combine(TemplateRoot, ConditionGame);
    private string RulesDirectory => Path.Combine(GameDirectory, TemplateLayout.RulesDirectoryName);
    private string TestScriptsDirectory => Path.Combine(_root, "TestScripts");

    /// <summary>
    /// Every script to migrate, in a deterministic order. Both the pre-move location and
    /// <c>Rules/</c> are scanned so a re-run after the move still finds them (idempotency).
    /// </summary>
    public List<string> DiscoverScripts()
    {
        var scripts = new List<string>();

        foreach (var directory in new[] { GameDirectory, RulesDirectory, TestScriptsDirectory })
        {
            if (!Directory.Exists(directory))
                continue;

            scripts.AddRange(
                Directory.GetFiles(directory, "*" + TemplateLayout.RuleExtension)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        }

        return scripts;
    }

    /// <summary>
    /// Migrates content, verifies behavioural equivalence, then moves the template scripts into
    /// <c>&lt;Game&gt;/Rules/</c>.
    ///
    /// <para>The move happens last and only on success: if equivalence fails, the files stay
    /// where they are so the failure can be inspected against an unchanged layout.</para>
    /// </summary>
    public MigrationReport Run(bool moveTemplatesIntoRulesDirectory = true)
    {
        var scripts = DiscoverScripts();
        if (scripts.Count == 0)
        {
            var empty = new MigrationReport();
            empty.Errors.Add($"没有找到任何 .rules.json（搜索根: {_root}）。");
            return empty;
        }

        // Snapshot before any rewrite: the equivalence check needs the original inline values,
        // and migration overwrites the files in place.
        var before = scripts.ToDictionary(
            path => path,
            path => MigrationEquivalenceChecker.ReadLegacy(File.ReadAllText(path)),
            StringComparer.OrdinalIgnoreCase);

        var migrator = new RuleScriptMigrator(TemplateRoot);
        var report = migrator.Migrate(scripts, ConditionGame);

        if (!report.Success)
            return report;

        // Only the scripts this run rewrote. On a re-run everything is already current, and the
        // in-memory condition set is empty — verifying those would compare each migrated file
        // against itself read as if it were still v1, and report all 13 as broken.
        VerifyEquivalence(report.MigratedPaths, before, migrator.Conditions, report);

        if (!report.Success)
            return report;

        if (moveTemplatesIntoRulesDirectory)
            MoveTemplatesIntoRulesDirectory(report);

        return report;
    }

    private static void VerifyEquivalence(
        IReadOnlyList<string> scripts,
        Dictionary<string, List<RuleBehaviour>> before,
        IReadOnlyDictionary<string, VisualConditionSpec> conditions,
        MigrationReport report)
    {
        if (scripts.Count == 0)
        {
            report.Log.Add("本次没有脚本被改写，跳过结构等价性校验。");
            return;
        }

        var passed = 0;

        foreach (var path in scripts)
        {
            var name = Path.GetFileName(path);
            try
            {
                var after = MigrationEquivalenceChecker.ReadMigrated(File.ReadAllText(path), conditions);
                var differences = MigrationEquivalenceChecker.Compare(name, before[path], after);

                if (differences.Count == 0)
                {
                    passed++;
                    continue;
                }

                report.Errors.AddRange(differences);
            }
            catch (Exception ex)
            {
                report.Errors.Add($"{name}: 等价性校验失败 — {ex.Message}");
            }
        }

        report.Log.Add($"结构等价性校验: {passed}/{scripts.Count} 通过");
    }

    /// <summary>
    /// Moves <c>&lt;Game&gt;/*.rules.json</c> into <c>&lt;Game&gt;/Rules/</c> (Spec §6.4).
    /// Images deliberately stay put — every condition stores
    /// <c>"&lt;Game&gt;/images/x.png"</c>, so moving them would invalidate all of those strings.
    /// </summary>
    private void MoveTemplatesIntoRulesDirectory(MigrationReport report)
    {
        if (!Directory.Exists(GameDirectory))
            return;

        var loose = Directory.GetFiles(GameDirectory, "*" + TemplateLayout.RuleExtension);
        if (loose.Length == 0)
        {
            report.Log.Add($"{ConditionGame}/ 下没有待移动的规则文件（可能已经移过）。");
            return;
        }

        Directory.CreateDirectory(RulesDirectory);

        foreach (var source in loose.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var target = Path.Combine(RulesDirectory, Path.GetFileName(source));
            if (File.Exists(target))
            {
                report.Errors.Add($"目标已存在，未移动: {target}");
                continue;
            }

            File.Move(source, target);
        }

        report.Log.Add(
            $"已移动 {loose.Length} 份规则模板到 {ConditionGame}/{TemplateLayout.RulesDirectoryName}/");
    }

    /// <summary>
    /// Copies the files a migration touches into a throwaway directory so the whole run can be
    /// rehearsed — including image promotion, the file move and the equivalence check — without
    /// modifying the repository. A dry run that skips the filesystem work would not exercise
    /// the parts most likely to go wrong.
    /// </summary>
    public static string CreateSandbox(string repoRoot)
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "af_migration_" + Guid.NewGuid().ToString("N"));

        foreach (var relative in new[]
                 {
                     Path.Combine("RuleTemplates", ConditionGame),
                     Path.Combine("RuleTemplates", ConditionGame, TemplateLayout.RulesDirectoryName),
                     Path.Combine("RuleTemplates", ConditionGame, TemplateLayout.ImagesDirectoryName),
                     "TestScripts",
                     Path.Combine("TestScripts", TemplateLayout.ImagesDirectoryName)
                 })
        {
            var source = Path.Combine(repoRoot, relative);
            if (!Directory.Exists(source))
                continue;

            var target = Path.Combine(sandbox, relative);
            Directory.CreateDirectory(target);

            // Top level only: the nested directories are listed explicitly above, so a
            // recursive copy would duplicate them.
            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
            }
        }

        return sandbox;
    }
}
