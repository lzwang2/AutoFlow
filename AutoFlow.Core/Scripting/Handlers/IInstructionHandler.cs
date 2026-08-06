using AutoFlow.Core.Scripting.Instructions;

namespace AutoFlow.Core.Scripting.Handlers;

/// <summary>
/// Handler interface for executing instructions
/// </summary>
public interface IInstructionHandler
{
    CommandType CommandType { get; }
    Task ExecuteAsync(Instruction instruction, ScriptExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Base class for instruction handlers
/// </summary>
public abstract class InstructionHandlerBase : IInstructionHandler
{
    public abstract CommandType CommandType { get; }

    public abstract Task ExecuteAsync(Instruction instruction, ScriptExecutionContext context, CancellationToken cancellationToken);

    protected async Task DelayAsync(int milliseconds, CancellationToken cancellationToken)
    {
        if (milliseconds > 0)
        {
            await Task.Delay(milliseconds, cancellationToken);
        }
    }
}
