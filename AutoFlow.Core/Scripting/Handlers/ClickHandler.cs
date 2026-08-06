using AutoFlow.Core.Scripting.Instructions;

namespace AutoFlow.Core.Scripting.Handlers;

/// <summary>
/// Handler for Click instruction - clicks at specified position
/// </summary>
public sealed class ClickHandler : InstructionHandlerBase
{
    public override CommandType CommandType => CommandType.Click;

    public override async Task ExecuteAsync(Instruction instruction, ScriptExecutionContext context, CancellationToken cancellationToken)
    {
        var scriptX = (int)Math.Round(instruction.PositionX);
        var scriptY = (int)Math.Round(instruction.PositionY);

        // Transform from the script's reference coordinate space to screen coordinates
        var screenPos = context.ScriptToScreen(scriptX, scriptY);

        context.Log($"Click: ({scriptX}, {scriptY}) → screen ({screenPos.X}, {screenPos.Y})");

        context.Input.ClickAt(screenPos.X, screenPos.Y);

        // Small delay after click to let UI respond
        await DelayAsync(50, cancellationToken);
    }
}
