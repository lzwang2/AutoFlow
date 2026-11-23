namespace AutoFlow.Core.Skills;

/// <summary>
/// Registry for locating skills by name. Singleton pattern; skills are registered
/// at application startup and remain available for the lifetime of the process.
/// </summary>
public sealed class SkillRegistry
{
    private static readonly Lazy<SkillRegistry> _instance = new(() => new SkillRegistry());
    public static SkillRegistry Instance => _instance.Value;

    private readonly Dictionary<string, ISkill> _skills = new();

    private SkillRegistry()
    {
        // Built-in skills registered on first access. Additional skills can be added
        // via explicit Register() calls at startup.
        Register(new LastCloudia.MultiPlayerCore.MultiPlayerCoreSkill());
        Register(new LastCloudia.SinglePlayerCore.SinglePlayerCoreSkill());
        Register(new LastCloudia.WeekRound.WeekRoundSkill());
    }

    /// <summary>
    /// Register a skill. Throws if a skill with the same name is already registered.
    /// </summary>
    public void Register(ISkill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        if (_skills.ContainsKey(skill.Name))
        {
            throw new InvalidOperationException($"Skill '{skill.Name}' is already registered.");
        }
        _skills[skill.Name] = skill;
    }

    /// <summary>
    /// Retrieve a skill by name. Returns null if not found.
    /// </summary>
    public ISkill? GetSkill(string name) => _skills.GetValueOrDefault(name);

    /// <summary>
    /// Get all registered skill names.
    /// </summary>
    public IReadOnlyCollection<string> GetAllSkillNames() => _skills.Keys;

    public void ResetForRun()
    {
        foreach (var skill in _skills.Values) skill.ResetForRun();
    }
}
