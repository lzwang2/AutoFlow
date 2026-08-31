using AutoFlow.Core.Scripting.Actions;

namespace AutoFlow.Core.Scripting;

/// <summary>
/// One entry in a script's serialized step list: either an inline atomic action or a
/// reference to a shared action block (Spec §11.5).
///
/// <para>Modelled as two nullable properties rather than a type-discriminator string, so
/// Newtonsoft handles it with no custom converter and the JSON stays self-describing —
/// <c>{"action": {...}}</c> versus <c>{"blockRef": {...}}</c>. Exactly one must be set;
/// <see cref="Validate"/> enforces that, because a step with both would leave the execution
/// order ambiguous and a step with neither would silently vanish.</para>
/// </summary>
public sealed class ScriptStep
{
    public ActionSpec? Action { get; set; }

    public ActionBlockRef? BlockRef { get; set; }

    public static ScriptStep FromAction(ActionSpec action) => new() { Action = action };

    public static ScriptStep FromBlock(string blockId, string? alias = null)
        => new() { BlockRef = new ActionBlockRef { BlockId = blockId, Alias = alias } };

    public bool IsAction => Action is not null;

    public bool IsBlockRef => BlockRef is not null;

    /// <summary>
    /// Throws when the step is empty or ambiguous. <paramref name="context"/> names the script
    /// and step index so the offending entry can be found.
    /// </summary>
    public void Validate(string context)
    {
        if (Action is not null && BlockRef is not null)
        {
            throw new InvalidOperationException(
                $"{context} 同时设置了 Action 和 BlockRef，无法确定执行内容。请只保留一个。");
        }

        if (Action is null && BlockRef is null)
        {
            throw new InvalidOperationException(
                $"{context} 既没有 Action 也没有 BlockRef，这一步没有任何内容。");
        }
    }

    public string Describe()
        => Action is not null
            ? (!string.IsNullOrWhiteSpace(Action.Name) ? Action.Name! : Action.Type.ToString())
            : BlockRef is not null
                ? $"动作块 '{BlockRef.BlockId}'"
                : "（空步骤）";
}
