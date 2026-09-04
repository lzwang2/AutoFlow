using AutoFlow.Core.Scripting.Conditions;
using Newtonsoft.Json;

namespace AutoFlow.Core.Scripting.Rules;

/// <summary>
/// Loads and saves <see cref="RuleScript"/> documents as JSON.
/// </summary>
public sealed class RuleScriptService
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Include
    };

    public RuleScript Load(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var script = JsonConvert.DeserializeObject<RuleScript>(json, JsonSettings);
        if (script == null)
        {
            throw new InvalidOperationException($"Failed to deserialize rule script from '{filePath}'.");
        }

        ScriptFormatVersion.Validate(script.FormatVersion, filePath);

        // No per-rule image resolution here any more: a rule names a condition and the
        // ConditionLibrary resolves that condition's image when it loads the game. Keeping a
        // second resolution path here is exactly the duplication this refactor removes.
        return script;
    }

    public void Save(RuleScript script, string filePath)
    {
        ArgumentNullException.ThrowIfNull(script);
        var json = JsonConvert.SerializeObject(script, JsonSettings);
        File.WriteAllText(filePath, json);
    }

    public RuleScript CreateNew() => new();
}
