namespace AutoFlow.Core.Skills.LastCloudia.MultiPlayerCore;

/// <summary>
/// Cross-tick state for MultiPlayerCore skill.
/// Mirrors the global variables in MultiPlayerCoreProcess.ahk (emoji_sent, in_room_count).
/// </summary>
internal sealed class MultiPlayerCoreState
{
    /// <summary>
    /// Controls "send emoji once" logic. Set to true after first emoji click,
    /// reset by ResetMultiPlayerState().
    /// </summary>
    public bool EmojiSent { get; set; }

    /// <summary>
    /// Cumulative wait counter when in the "GO" room as a non-owner.
    /// Incremented each tick (sleep 2s), reset when >= 40 (timeout) or by ResetMultiPlayerState().
    /// </summary>
    public int InRoomCount { get; set; }

    /// <summary>
    /// Reset state after battle end / network error / abandon task (matches AHK ResetMultiPlayerState).
    /// </summary>
    public void Reset()
    {
        EmojiSent = false;
        InRoomCount = 0;
    }
}
