using AutoFlow.Core.Scripting;

namespace AutoFlow.Core.Skills;

/// <summary>IDs of a shared visual condition and fixed action sequence used by a skill branch.</summary>
public sealed record SkillResourceReference(string ConditionId, string ActionBlockId);

/// <summary>Shared visual condition used directly by a skill without a fixed action block.</summary>
public sealed record SkillConditionReference(string ConditionId);

/// <summary>
/// Base interface for all hardcoded automation skills.
/// Skills are complex state machines implemented in C#, callable from rule scripts via
/// CallSkill instructions. Use skills for flows too complex to express with rule scripts
/// (e.g. cross-tick state, nested conditions, loops, random selection).
/// </summary>
public interface ISkill
{
    /// <summary>
    /// Unique skill identifier, typically "GameName.SkillName" (e.g. "LastCloudia.MultiPlayerCore").
    /// Must match the SkillName field in CallSkill instructions.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable description shown in UI and logs.
    /// </summary>
    string Description { get; }

    /// <summary>Shared dependencies validated before capture or input begins.</summary>
    IReadOnlyList<SkillResourceReference> SharedResources => Array.Empty<SkillResourceReference>();

    /// <summary>Shared visual conditions used by skill-owned flow branches.</summary>
    IReadOnlyList<SkillConditionReference> SharedConditions => Array.Empty<SkillConditionReference>();

    /// <summary>Reset cross-tick state before a new script run; keep it within that run.</summary>
    void ResetForRun() { }

    /// <summary>
    /// Execute the skill's main loop until completion or cancellation.
    /// The skill has full access to the execution context (capture, input, transform, etc.)
    /// and can run arbitrary logic (state machines, counters, nested conditions).
    /// </summary>
    /// <param name="context">Execution context with capture/input/logging services</param>
    /// <param name="parameters">Skill-specific parameters from the CallSkill instruction (may be null)</param>
    /// <param name="cancellationToken">Cancellation token to stop the skill mid-execution</param>
    Task ExecuteAsync(
        ScriptExecutionContext context,
        Dictionary<string, object>? parameters,
        CancellationToken cancellationToken);
}
