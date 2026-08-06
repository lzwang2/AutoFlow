using AutoFlow.Core.Scripting.Instructions;

namespace AutoFlow.Core.Scripting.Handlers;

/// <summary>
/// Handler for Comment instruction - does nothing
/// </summary>
public sealed class CommentHandler : InstructionHandlerBase
{
    public override CommandType CommandType => CommandType.Comment;

    public override Task ExecuteAsync(Instruction instruction, ScriptExecutionContext context, CancellationToken cancellationToken)
    {
        // Comments are no-ops, just log if there's content
        if (!string.IsNullOrWhiteSpace(instruction.CommentContent))
        {
            context.Log($"// {instruction.CommentContent}");
        }
        return Task.CompletedTask;
    }
}
