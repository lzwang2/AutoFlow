using AutoFlow.Core.Scripting.Handlers;

namespace AutoFlow.Core.Scripting;

/// <summary>
/// Interprets and executes a script document instruction by instruction
/// </summary>
public sealed class ScriptInterpreter
{
    private readonly InstructionHandlerRegistry _handlerRegistry;

    public ScriptInterpreter(InstructionHandlerRegistry handlerRegistry)
    {
        _handlerRegistry = handlerRegistry;
    }

    /// <summary>
    /// Execute a script document from start to finish
    /// </summary>
    public async Task ExecuteAsync(
        ScriptDocument script,
        ScriptExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        if (script.Instructions == null || script.Instructions.Count == 0)
        {
            context.Log("Script has no instructions");
            return;
        }

        context.Log($"Starting script: {script.Metadata?.Description ?? "Untitled"}");
        context.Log($"Target window: {script.Metadata?.TargetWindowTitle}");

        var instructionIndex = 0;

        while (instructionIndex < script.Instructions.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var instruction = script.Instructions[instructionIndex];
            context.CurrentInstructionIndex = instructionIndex;
            context.JumpToIndex = null;

            // Get handler for this instruction type
            var handler = _handlerRegistry.GetHandler(instruction.CommandType);
            if (handler == null)
            {
                throw new InvalidOperationException($"No handler registered for CommandType: {instruction.CommandType}");
            }

            // Execute instruction
            try
            {
                await handler.ExecuteAsync(instruction, context, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                context.Log("Script execution cancelled");
                throw;
            }
            catch (Exception ex)
            {
                context.Log($"Error at instruction {instructionIndex} ({instruction.CommandType}): {ex.Message}");
                throw;
            }

            // Check if handler requested a jump (for Loop instructions)
            if (context.JumpToIndex.HasValue)
            {
                var targetIndex = context.JumpToIndex.Value;

                if (targetIndex < 0 || targetIndex >= script.Instructions.Count)
                {
                    throw new InvalidOperationException($"Loop jump target out of bounds: {targetIndex} (script has {script.Instructions.Count} instructions)");
                }

                instructionIndex = targetIndex;
            }
            else
            {
                // Normal flow: advance to next instruction
                instructionIndex++;
            }
        }

        context.Log("Script execution completed");
    }
}
