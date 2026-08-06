using AutoFlow.Core.Scripting.Instructions;

namespace AutoFlow.Core.Scripting.Handlers;

/// <summary>
/// Handler for WaitTime instruction - delays execution
/// </summary>
public sealed class WaitTimeHandler : InstructionHandlerBase
{
    public override CommandType CommandType => CommandType.WaitTime;

    public override async Task ExecuteAsync(Instruction instruction, ScriptExecutionContext context, CancellationToken cancellationToken)
    {
        var delayMs = instruction.WaitTimeMilliseconds > 0 ? instruction.WaitTimeMilliseconds : 1000;
        context.Log($"WaitTime: {delayMs}ms");
        await DelayAsync(delayMs, cancellationToken);
    }
}
