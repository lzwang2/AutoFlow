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

        // Resolve relative template paths:
        // 1. Try relative to script file directory (for user scripts next to their own images)
        // 2. Try relative to app's RuleTemplates directory (for shared template images)
        var scriptDirectory = Path.GetDirectoryName(filePath);
        var appBaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var appTemplatesDirectory = Path.Combine(appBaseDirectory, "RuleTemplates");

        Console.WriteLine($"[RuleScriptService] AppDomain.CurrentDomain.BaseDirectory = '{appBaseDirectory}'");
        Console.WriteLine($"[RuleScriptService] RuleTemplates directory = '{appTemplatesDirectory}'");

        foreach (var rule in script.Rules)
        {
            if (string.IsNullOrWhiteSpace(rule.TemplatePath) || Path.IsPathRooted(rule.TemplatePath))
            {
                continue; // Already absolute or empty
            }

            var originalPath = rule.TemplatePath;

            // Try relative to script directory first
            if (!string.IsNullOrEmpty(scriptDirectory))
            {
                var candidatePath = Path.Combine(scriptDirectory, rule.TemplatePath);
                if (File.Exists(candidatePath))
                {
                    rule.TemplatePath = candidatePath;
                    Console.WriteLine($"[RuleScriptService] Resolved '{originalPath}' -> '{candidatePath}' (relative to script)");
                    continue;
                }
            }

            // Try relative to app's RuleTemplates directory
            var appTemplatePath = Path.Combine(appTemplatesDirectory, rule.TemplatePath);
            if (File.Exists(appTemplatePath))
            {
                rule.TemplatePath = appTemplatePath;
                Console.WriteLine($"[RuleScriptService] Resolved '{originalPath}' -> '{appTemplatePath}' (relative to app RuleTemplates)");
                continue;
            }

            // Log failed resolution
            Console.WriteLine($"[RuleScriptService] Failed to resolve '{originalPath}':");
            Console.WriteLine($"  Tried: {Path.Combine(scriptDirectory ?? "null", rule.TemplatePath)}");
            Console.WriteLine($"  Tried: {appTemplatePath}");
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
