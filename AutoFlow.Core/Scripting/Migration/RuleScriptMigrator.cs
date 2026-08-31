using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Rules;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AutoFlow.Core.Scripting.Migration;

/// <summary>
/// One-time migration from inline rule conditions (FormatVersion 1) to shared condition
/// references (FormatVersion 2), per Spec FR-11 and plan Phase 6.
///
/// <para><b>Reads raw JSON, deliberately not <see cref="RuleScriptService"/>.</b> That loader
/// rewrites every <c>TemplatePath</c> to an absolute path on the way in, so migrating through
/// it would bake this machine's directory layout into all 13 files — the exact defect the
/// migration is supposed to remove from <c>test.rules.json</c>.</para>
///
/// <para>Conditions with identical content are merged into one file. Two rules in different
/// scripts that point at the same image with the same region and thresholds are the same
/// condition, and collapsing them is the actual reuse benefit being claimed.</para>
/// </summary>
public sealed class RuleScriptMigrator
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Include
    };

    private readonly string _templateRoot;
    private readonly bool _dryRun;

    /// <summary>Content fingerprint → the Id already assigned to that condition.</summary>
    private readonly Dictionary<string, string> _idByFingerprint = new(StringComparer.Ordinal);

    /// <summary>Id → the condition itself, so a second sighting can be verified as identical.</summary>
    private readonly Dictionary<string, VisualConditionSpec> _byId = new(StringComparer.OrdinalIgnoreCase);

    public RuleScriptMigrator(string templateRoot, bool dryRun = false)
    {
        _templateRoot = templateRoot;
        _dryRun = dryRun;
    }

    public IReadOnlyDictionary<string, VisualConditionSpec> Conditions => _byId;

    /// <summary>
    /// Migrates every script in <paramref name="scriptPaths"/>, writing shared conditions into
    /// <paramref name="conditionGame"/>'s library. Scripts are rewritten in place.
    /// </summary>
    public MigrationReport Migrate(IEnumerable<string> scriptPaths, string conditionGame)
    {
        var report = new MigrationReport();

        foreach (var path in scriptPaths)
        {
            try
            {
                MigrateOne(path, conditionGame, report);
            }
            catch (Exception ex)
            {
                report.Errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        if (!_dryRun && report.Success)
        {
            WriteConditions(conditionGame, report);
        }

        return report;
    }

    private void MigrateOne(string scriptPath, string conditionGame, MigrationReport report)
    {
        var root = JObject.Parse(File.ReadAllText(scriptPath));

        var formatVersion = root.Value<int?>(nameof(RuleScript.FormatVersion)) ?? 1;
        if (formatVersion >= ScriptFormatVersion.Current)
        {
            report.ScriptsAlreadyCurrent++;
            report.Log.Add($"{Path.GetFileName(scriptPath)}: 已是 FormatVersion {formatVersion}，跳过。");
            return;
        }

        var outerWidth = root.Value<int?>(nameof(RuleScript.OuterWidth)) ?? 1920;
        var outerHeight = root.Value<int?>(nameof(RuleScript.OuterHeight)) ?? 1080;

        if (root[nameof(RuleScript.Rules)] is not JArray rules)
        {
            throw new InvalidOperationException("脚本没有 Rules 数组。");
        }

        foreach (var ruleToken in rules)
        {
            if (ruleToken is not JObject rule)
                continue;

            MigrateRule(rule, scriptPath, conditionGame, outerWidth, outerHeight, report);
        }

        root[nameof(RuleScript.FormatVersion)] = ScriptFormatVersion.Current;

        if (!_dryRun)
        {
            File.WriteAllText(scriptPath, root.ToString(Formatting.Indented));
        }

        report.ScriptsMigrated++;
        report.MigratedPaths.Add(scriptPath);
    }

    private void MigrateRule(
        JObject rule, string scriptPath, string conditionGame,
        int outerWidth, int outerHeight, MigrationReport report)
    {
        var ruleName = rule.Value<string>(nameof(Rule.Name)) ?? "(未命名)";
        var templatePath = rule.Value<string>(nameof(Rule.TemplatePath));

        // Catch-all rule: no condition to extract. Leave ConditionRef absent.
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            report.CatchAllRules++;
            report.Log.Add($"{Path.GetFileName(scriptPath)} / {ruleName}: 无条件兜底规则，保持原样。");
            StripInlineCondition(rule);
            return;
        }

        var kind = TemplatePathClassifier.Classify(templatePath, conditionGame);

        // A file-relative image lives beside its script, not in the shared library. Promoting it
        // means copying it in, because a condition file sits in <Game>/Conditions/ and could not
        // resolve "images/welcome.png" from there.
        if (kind is TemplatePathKind.FileRelative)
        {
            PromoteImage(templatePath!, scriptPath, conditionGame, report);
        }

        var condition = BuildCondition(rule, templatePath!, conditionGame, outerWidth, outerHeight);
        var id = Intern(condition, scriptPath, ruleName, report);

        rule[nameof(Rule.ConditionRef)] = id;
        StripInlineCondition(rule);
    }

    private static VisualConditionSpec BuildCondition(
        JObject rule, string templatePath, string game, int outerWidth, int outerHeight)
        => new()
        {
            Id = ConditionIdGenerator.FromTemplatePath(game, templatePath),
            Name = rule.Value<string>(nameof(Rule.Name)) ?? string.Empty,
            Description = rule.Value<string>(nameof(Rule.Description)),
            TemplatePath = TemplatePathClassifier.ToLibraryRelative(templatePath, game),
            MatchThreshold = rule.Value<double?>(nameof(Rule.MatchThreshold)) ?? 0.85,
            MeanDiffThreshold = rule.Value<double?>(nameof(Rule.MeanDiffThreshold)) ?? 30.0,
            RegionX1 = rule.Value<int?>(nameof(Rule.RegionX1)) ?? -1,
            RegionY1 = rule.Value<int?>(nameof(Rule.RegionY1)) ?? -1,
            RegionX2 = rule.Value<int?>(nameof(Rule.RegionX2)) ?? -1,
            RegionY2 = rule.Value<int?>(nameof(Rule.RegionY2)) ?? -1,

            // Regions are meaningless without the window size they were measured against
            // (FR-10). The owning script's outer size is that size.
            ReferenceOuterWidth = outerWidth,
            ReferenceOuterHeight = outerHeight
        };

    /// <summary>
    /// Registers a condition, reusing an existing Id when the content is identical.
    /// Returns the Id to reference.
    /// </summary>
    private string Intern(
        VisualConditionSpec condition, string scriptPath, string ruleName, MigrationReport report)
    {
        var fingerprint = Fingerprint(condition);

        if (_idByFingerprint.TryGetValue(fingerprint, out var existingId))
        {
            report.DuplicatesMerged++;
            report.Log.Add(
                $"{Path.GetFileName(scriptPath)} / {ruleName}: 内容与已有条件相同，合并为 '{existingId}'。");
            return existingId;
        }

        // Same generated Id but different content — two genuinely different conditions would
        // silently overwrite each other, so stop instead.
        if (_byId.TryGetValue(condition.Id, out var clash))
        {
            throw new InvalidOperationException(
                $"条件 Id 冲突: '{condition.Id}' 已存在但内容不同。" +
                $"已有: 区域 ({clash.RegionX1},{clash.RegionY1})-({clash.RegionX2},{clash.RegionY2}) " +
                $"阈值 {clash.MatchThreshold}/{clash.MeanDiffThreshold} 参照 {clash.ReferenceOuterWidth}x{clash.ReferenceOuterHeight}; " +
                $"新的: 区域 ({condition.RegionX1},{condition.RegionY1})-({condition.RegionX2},{condition.RegionY2}) " +
                $"阈值 {condition.MatchThreshold}/{condition.MeanDiffThreshold} 参照 {condition.ReferenceOuterWidth}x{condition.ReferenceOuterHeight}。" +
                $"来自 {Path.GetFileName(scriptPath)} 的规则 '{ruleName}'，需要人工指定不同的 Id。");
        }

        _idByFingerprint[fingerprint] = condition.Id;
        _byId[condition.Id] = condition;
        return condition.Id;
    }

    /// <summary>
    /// Identity of a condition for merging purposes: everything that affects matching.
    /// Name and Description are excluded — they are display text, and two rules describing the
    /// same screen in different words are still the same detection.
    /// </summary>
    private static string Fingerprint(VisualConditionSpec c) => string.Join(
        '|',
        c.TemplatePath?.Replace('\\', '/').ToLowerInvariant() ?? string.Empty,
        c.MatchThreshold.ToString("R"),
        c.MeanDiffThreshold.ToString("R"),
        c.RegionX1, c.RegionY1, c.RegionX2, c.RegionY2,
        c.ReferenceOuterWidth, c.ReferenceOuterHeight);

    /// <summary>
    /// Copies a script-private image into the shared library so a condition file can resolve it.
    /// Refuses to overwrite a different existing file: silently replacing a library image would
    /// change what every other condition referencing it detects.
    /// </summary>
    private void PromoteImage(string templatePath, string scriptPath, string game, MigrationReport report)
    {
        var scriptDirectory = Path.GetDirectoryName(scriptPath)!;
        var source = Path.Combine(scriptDirectory, templatePath);
        var fileName = Path.GetFileName(templatePath.Replace('\\', '/'));
        var targetDirectory = TemplateLayout.ImagesDirectoryFor(_templateRoot, game);
        var target = Path.Combine(targetDirectory, fileName);

        if (!File.Exists(source))
        {
            throw new InvalidOperationException(
                $"图片不存在，无法提升到库内: {source}（来自 '{templatePath}'）。");
        }

        if (File.Exists(target))
        {
            if (!FilesEqual(source, target))
            {
                throw new InvalidOperationException(
                    $"图片名冲突: 库内已有 {fileName} 但内容与 {source} 不同。" +
                    $"直接覆盖会改变其他条件的判定结果，请人工重命名后再迁移。");
            }

            report.Log.Add($"图片 {fileName} 库内已存在且内容相同，跳过复制。");
            return;
        }

        if (!_dryRun)
        {
            Directory.CreateDirectory(targetDirectory);
            File.Copy(source, target);
        }

        report.ImagesPromoted++;
        report.Log.Add($"图片提升到库内: {fileName} → {game}/{TemplateLayout.ImagesDirectoryName}/");
    }

    private static bool FilesEqual(string a, string b)
    {
        var infoA = new FileInfo(a);
        var infoB = new FileInfo(b);
        if (infoA.Length != infoB.Length)
            return false;

        return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }

    /// <summary>
    /// Removes the inline condition fields now represented by the reference.
    ///
    /// Done here rather than in Phase 7 because leaving both would let them disagree: an editor
    /// saving a rule would write inline values the interpreter ignores. The originals stay
    /// recoverable in git history, which is what the plan relies on for rollback.
    /// </summary>
    private static void StripInlineCondition(JObject rule)
    {
        foreach (var property in new[]
                 {
                     nameof(Rule.TemplatePath),
                     nameof(Rule.MatchThreshold),
                     nameof(Rule.MeanDiffThreshold),
                     nameof(Rule.RegionX1),
                     nameof(Rule.RegionY1),
                     nameof(Rule.RegionX2),
                     nameof(Rule.RegionY2)
                 })
        {
            rule.Remove(property);
        }
    }

    private void WriteConditions(string game, MigrationReport report)
    {
        var library = new ConditionLibrary(_templateRoot);

        foreach (var condition in _byId.Values.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            library.Save(condition, game, out var warnings);
            report.ConditionsWritten++;
            report.Log.AddRange(warnings);
        }
    }
}
