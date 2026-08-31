using Newtonsoft.Json;

namespace AutoFlow.Core.Scripting;

/// <summary>
/// Loads and saves linear instruction script templates under
/// &lt;TemplateRoot&gt;/&lt;Game&gt;/Scripts/*.json.
///
/// The linear editor currently has no shipped scripts, but its feature set is preserved in
/// full — so it gets the same template library the rule side has, rather than being left
/// with file-open dialogs only. Without this, "reuse a linear script as a template" would
/// have no home in the new layout.
/// </summary>
public sealed class ScriptTemplateLibrary
{
    private readonly ScriptDocumentService _documentService;
    private readonly string _templateRoot;

    public ScriptTemplateLibrary(string? templateRoot = null, ScriptDocumentService? documentService = null)
    {
        _templateRoot = templateRoot ?? Conditions.TemplatePathResolver.TemplateRoot;
        _documentService = documentService ?? new ScriptDocumentService();
    }

    public static string DirectoryFor(string templateRoot, string game)
        => TemplateLayout.DirectoryFor(templateRoot, game, TemplateKind.LinearScript);

    /// <summary>
    /// Lists the linear script templates for a game. Files that fail to parse are skipped
    /// and reported through <paramref name="warnings"/> rather than aborting the listing —
    /// one bad file should not hide the rest of the library.
    /// </summary>
    public IReadOnlyList<TemplateIndexEntry> List(string game, out IReadOnlyList<string> warnings)
    {
        var collected = new List<string>();
        var entries = new List<TemplateIndexEntry>();
        var directory = DirectoryFor(_templateRoot, game);

        if (!Directory.Exists(directory))
        {
            warnings = collected;
            return entries;
        }

        foreach (var file in Directory.GetFiles(directory, "*" + TemplateLayout.LinearScriptExtension))
        {
            // Defensive: a *.rules.json accidentally dropped into Scripts/ matches the
            // "*.json" pattern but is not a linear script.
            if (TemplateLayout.ClassifyFile(file) != TemplateKind.LinearScript)
                continue;

            try
            {
                var document = _documentService.Load(file);
                entries.Add(new TemplateIndexEntry
                {
                    Kind = TemplateKind.LinearScript,
                    FilePath = file,
                    Game = game,
                    Name = Path.GetFileNameWithoutExtension(file),
                    Description = document.Metadata.Description ?? "",
                    Category = document.Metadata.Tags.Count > 0 ? document.Metadata.Tags[0] : "",
                    ItemCount = document.Instructions.Count
                });
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException)
            {
                collected.Add($"线性脚本模板解析失败: {file} — {ex.Message}");
            }
        }

        warnings = collected;
        return entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Loads a linear script template. Returns a fresh document each call, so edits made
    /// after importing never write back into the template file.
    /// </summary>
    public ScriptDocument Load(string filePath) => _documentService.Load(filePath);

    /// <summary>
    /// Saves a document as a template under &lt;Game&gt;/Scripts/ and returns the written path.
    /// </summary>
    public string Save(ScriptDocument document, string game, string fileName)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("模板文件名不能为空。", nameof(fileName));

        var directory = DirectoryFor(_templateRoot, game);
        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            TemplateFileNames.Sanitize(fileName) + TemplateLayout.LinearScriptExtension);

        _documentService.Save(document, path);
        return path;
    }
}
