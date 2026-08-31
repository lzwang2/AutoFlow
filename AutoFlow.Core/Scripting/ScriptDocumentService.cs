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
    /// Load script from JSON file.
    ///
    /// <para>Rejects any <c>FormatVersion</c> other than <see cref="ScriptFormatVersion.Current"/>
    /// (FR-11). Without this gate a version 1 file would parse "successfully" into the version 2
    /// model with its inline condition fields silently dropped — an error is strictly better
    /// than running with the wrong coordinates.</para>
    /// </summary>
    public ScriptDocument Load(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var document = JsonConvert.DeserializeObject<ScriptDocument>(json, JsonSettings)
            ?? throw new InvalidOperationException($"无法反序列化脚本: '{filePath}'。");

        ScriptFormatVersion.Validate(document.FormatVersion, filePath);
        return document;
    }

    /// <summary>
    /// Save script to JSON file. The version is always stamped to
    /// <see cref="ScriptFormatVersion.Current"/> — writing anything else would produce a file
    /// this same build refuses to load.
    /// </summary>
    public void Save(ScriptDocument document, string filePath)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.FormatVersion = ScriptFormatVersion.Current;
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
