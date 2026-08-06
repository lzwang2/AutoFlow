using AutoFlow.Core.Scripting.Instructions;

namespace AutoFlow.Core.Scripting.Handlers;

/// <summary>
/// Handler for Loop instruction - jumps back to specified instruction index
/// </summary>
public sealed class LoopHandler : InstructionHandlerBase
{
    public override CommandType CommandType => CommandType.Loop;

    public override Task ExecuteAsync(Instruction instruction, ScriptExecutionContext context, CancellationToken cancellationToken)
    {
        var targetIndex = instruction.LoopBackToIndex;
        var maxIterations = instruction.LoopCount; // -1 = infinite

        // Track iteration count for this loop instruction
        if (!context.LoopCounters.TryGetValue(context.CurrentInstructionIndex, out var iterationCount))
        {
            iterationCount = 0;
        }

        iterationCount++;
        context.LoopCounters[context.CurrentInstructionIndex] = iterationCount;

        if (maxIterations > 0 && iterationCount >= maxIterations)
        {
            context.Log($"Loop: max iterations ({maxIterations}) reached, continuing");
            return Task.CompletedTask;
        }

        var iterationLabel = maxIterations > 0 ? $"iteration {iterationCount}/{maxIterations}" : $"iteration {iterationCount}";
        context.Log($"Loop: jump to index {targetIndex} ({iterationLabel})");
        context.JumpToIndex = targetIndex;

        return Task.CompletedTask;
    }
}
