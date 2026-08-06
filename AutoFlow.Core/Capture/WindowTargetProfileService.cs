using Newtonsoft.Json;

namespace AutoFlow.Core.Capture;

/// <summary>
/// Loads and saves <see cref="WindowTargetProfile"/> entries to disk so a window size,
/// position and reference resolution only has to be configured once.
/// </summary>
public sealed class WindowTargetProfileService
{
    private readonly string _filePath;

    public WindowTargetProfileService(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "AutoFlow",
            "window-profiles.json");
    }

    public string FilePath => _filePath;

    /// <summary>
    /// Load all saved profiles. Returns an empty list when nothing has been saved yet, or
    /// when the file is unreadable/corrupt — a bad settings file should not block startup.
    /// </summary>
    public List<WindowTargetProfile> Load()
    {
        if (!File.Exists(_filePath))
        {
            return new List<WindowTargetProfile>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonConvert.DeserializeObject<List<WindowTargetProfile>>(json)
                   ?? new List<WindowTargetProfile>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new List<WindowTargetProfile>();
        }
    }

    public void Save(IEnumerable<WindowTargetProfile> profiles)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonConvert.SerializeObject(profiles, Formatting.Indented,
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });
        File.WriteAllText(_filePath, json);
    }

    /// <summary>
    /// Insert or replace a profile, matching on <see cref="WindowTargetProfile.Name"/>.
    /// </summary>
    public void Upsert(WindowTargetProfile profile)
    {
        var profiles = Load();
        var index = profiles.FindIndex(p =>
            string.Equals(p.Name, profile.Name, StringComparison.OrdinalIgnoreCase));

        if (index >= 0)
        {
            profiles[index] = profile;
        }
        else
        {
            profiles.Add(profile);
        }

        Save(profiles);
    }

    public void Delete(string name)
    {
        var profiles = Load();
        profiles.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        Save(profiles);
    }
}
