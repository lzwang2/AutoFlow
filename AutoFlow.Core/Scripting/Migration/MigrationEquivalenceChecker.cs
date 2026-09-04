using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Rules;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace AutoFlow.Core.Scripting.Migration;

/// <summary>One rule's effective behaviour, independent of how it is stored.</summary>
public sealed record RuleBehaviour(
    string Name,
    bool Enabled,
    bool TriggerOnce,
    string? TemplateFileName,
    double MatchThreshold,
    double MeanDiffThreshold,
    int RegionX1,
    int RegionY1,
    int RegionX2,
    int RegionY2,
    string ActionsJson)
{
    public bool IsCatchAll => TemplateFileName is null;
}

/// <summary>
/// Verifies that migration preserved behaviour (Spec AC-06, first condition).
///
/// <para>Compares <i>effective</i> behaviour, not JSON text: the whole point of the migration is
/// that the text changes. For each rule it reduces both the before and after forms to the values
/// the interpreter actually uses — image file, thresholds, region, action list, dispatch flags —
/// and requires them to be equal.</para>
///
/// <para>Only the image <b>filename</b> is compared, not the path. Migration intentionally
/// rewrites file-relative paths to library-relative ones and promotes the image, so comparing
/// paths would report every such rule as broken when it is in fact correct.</para>
/// </summary>
public static class MigrationEquivalenceChecker
{
    /// <summary>Extracts behaviour from a v1 script's raw JSON (inline condition fields).</summary>
    public static List<RuleBehaviour> ReadLegacy(string json)
    {
        var root = JObject.Parse(json);
        var result = new List<RuleBehaviour>();

        if (root[nameof(RuleScript.Rules)] is not JArray rules)
            return result;

        foreach (var token in rules)
        {
            if (token is not JObject rule)
                continue;

            var templatePath = rule.Value<string>(LegacyRuleFields.TemplatePath);

            result.Add(new RuleBehaviour(
                Name: rule.Value<string>(nameof(Rule.Name)) ?? string.Empty,
                Enabled: rule.Value<bool?>(nameof(Rule.Enabled)) ?? true,
                TriggerOnce: rule.Value<bool?>(nameof(Rule.TriggerOnce)) ?? false,
                TemplateFileName: FileNameOf(templatePath),
                MatchThreshold: rule.Value<double?>(LegacyRuleFields.MatchThreshold) ?? 0.85,
                MeanDiffThreshold: rule.Value<double?>(LegacyRuleFields.MeanDiffThreshold) ?? 30.0,
                RegionX1: rule.Value<int?>(LegacyRuleFields.RegionX1) ?? -1,
                RegionY1: rule.Value<int?>(LegacyRuleFields.RegionY1) ?? -1,
                RegionX2: rule.Value<int?>(LegacyRuleFields.RegionX2) ?? -1,
                RegionY2: rule.Value<int?>(LegacyRuleFields.RegionY2) ?? -1,
                ActionsJson: NormalizeActions(rule[nameof(Rule.Actions)])));
        }

        return result;
    }

    /// <summary>
    /// Extracts behaviour from a migrated script, resolving each <c>ConditionRef</c> through
    /// <paramref name="conditions"/>. A reference that does not resolve is itself a failure.
    /// </summary>
    public static List<RuleBehaviour> ReadMigrated(
        string json, IReadOnlyDictionary<string, VisualConditionSpec> conditions)
    {
        var root = JObject.Parse(json);
        var result = new List<RuleBehaviour>();

        if (root[nameof(RuleScript.Rules)] is not JArray rules)
            return result;

        foreach (var token in rules)
        {
            if (token is not JObject rule)
                continue;

            var name = rule.Value<string>(nameof(Rule.Name)) ?? string.Empty;
            var conditionRef = rule.Value<string>(nameof(Rule.ConditionRef));

            VisualConditionSpec? condition = null;
            if (!string.IsNullOrWhiteSpace(conditionRef))
            {
                if (!conditions.TryGetValue(conditionRef, out condition))
                {
                    throw new InvalidOperationException(
                        $"规则 '{name}' 引用的条件 '{conditionRef}' 在条件库中不存在。");
                }
            }

            result.Add(new RuleBehaviour(
                Name: name,
                Enabled: rule.Value<bool?>(nameof(Rule.Enabled)) ?? true,
                TriggerOnce: rule.Value<bool?>(nameof(Rule.TriggerOnce)) ?? false,
                TemplateFileName: FileNameOf(condition?.TemplatePath),
                MatchThreshold: condition?.MatchThreshold ?? 0.85,
                MeanDiffThreshold: condition?.MeanDiffThreshold ?? 30.0,
                RegionX1: condition?.RegionX1 ?? -1,
                RegionY1: condition?.RegionY1 ?? -1,
                RegionX2: condition?.RegionX2 ?? -1,
                RegionY2: condition?.RegionY2 ?? -1,
                ActionsJson: NormalizeActions(rule[nameof(Rule.Actions)])));
        }

        return result;
    }

    /// <summary>
    /// Compares two behaviour lists, returning one message per difference. An empty result
    /// means the migration preserved behaviour for that script.
    /// </summary>
    public static List<string> Compare(
        string scriptName, List<RuleBehaviour> before, List<RuleBehaviour> after)
    {
        var differences = new List<string>();

        if (before.Count != after.Count)
        {
            differences.Add($"{scriptName}: 规则条数变化 {before.Count} → {after.Count}");
            return differences;
        }

        for (var i = 0; i < before.Count; i++)
        {
            // Rule order is the dispatch priority (first match wins), so index-wise comparison
            // is the correct pairing — a reordered list is a behaviour change, not a match.
            if (before[i] != after[i])
            {
                differences.Add(
                    $"{scriptName} 规则 #{i}: 行为不一致{Environment.NewLine}" +
                    $"    迁移前: {before[i]}{Environment.NewLine}" +
                    $"    迁移后: {after[i]}");
            }
        }

        return differences;
    }

    private static string? FileNameOf(string? templatePath)
        => string.IsNullOrWhiteSpace(templatePath)
            ? null
            : Path.GetFileName(templatePath.Replace('\\', '/'));

    /// <summary>
    /// Canonical form of an action list: property order and whitespace must not count as a
    /// difference, but values and ordering of the actions themselves must.
    /// </summary>
    private static string NormalizeActions(JToken? actions)
    {
        if (actions is not JArray array)
            return "[]";

        var normalized = new JArray(array.Select(item =>
            item is JObject obj
                ? new JObject(obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                : item));

        return normalized.ToString(Formatting.None);
    }
}
