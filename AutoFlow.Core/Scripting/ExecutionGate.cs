namespace AutoFlow.Core.Scripting;

/// <summary>Owns the single task slot, including asynchronous startup and cleanup.</summary>
public sealed class ExecutionGate
{
    private int _occupied;
    public bool TryEnter() => Interlocked.CompareExchange(ref _occupied, 1, 0) == 0;
    public void Exit() => Interlocked.Exchange(ref _occupied, 0);
}
