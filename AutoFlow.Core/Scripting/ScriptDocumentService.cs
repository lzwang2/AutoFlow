using Newtonsoft.Json;

namespace AutoFlow.Core.Scripting;

/// <summary>
/// Service for loading and saving script documents
/// </summary>
public sealed class ScriptDocumentService
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
        NullValueHandling = NullValueHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.Include
    };

    /// <summary>
    /// Load script from JSON file
    /// </summary>
    public ScriptDocument Load(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var document = JsonConvert.DeserializeObject<ScriptDocument>(json, JsonSettings);
        return document ?? throw new InvalidOperationException($"Failed to deserialize script from '{filePath}'.");
    }

    /// <summary>
    /// Save script to JSON file
    /// </summary>
    public void Save(ScriptDocument document, string filePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        var json = JsonConvert.SerializeObject(document, JsonSettings);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Create new empty script
    /// </summary>
    public ScriptDocument CreateNew()
    {
        return new ScriptDocument();
    }
}
