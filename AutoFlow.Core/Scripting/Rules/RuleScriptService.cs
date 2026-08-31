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

        // Path resolution lives in TemplatePathResolver so that shared conditions resolve
        // images exactly the same way inline ones do; keeping a second copy here would let
        // the two drift apart, which is the very duplication this refactor removes.
        var scriptDirectory = Path.GetDirectoryName(filePath);

        foreach (var rule in script.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.TemplatePath) || Path.IsPathRooted(rule.TemplatePath))
            {
                continue; // Already absolute or empty
            }

            var originalPath = rule.TemplatePath;
            var resolved = TemplatePathResolver.Resolve(originalPath, scriptDirectory);

            if (resolved is not null)
            {
                rule.TemplatePath = resolved;
                Console.WriteLine($"[RuleScriptService] Resolved '{originalPath}' -> '{resolved}'");
            }
            else
            {
                Console.WriteLine($"[RuleScriptService] Failed to resolve '{originalPath}':");
                foreach (var attempted in TemplatePathResolver.Candidates(originalPath, scriptDirectory))
                    Console.WriteLine($"  Tried: {attempted}");
            }
        }

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
