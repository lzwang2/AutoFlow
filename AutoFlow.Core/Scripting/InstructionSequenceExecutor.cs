using AutoFlow.Core.Scripting.Handlers;

namespace AutoFlow.Core.Scripting;

/// <summary>Executes fixed action sequences; callers own dispatch and error policy.</summary>
public sealed class InstructionSequenceExecutor(InstructionHandlerRegistry handlers)
{
    public async Task ExecuteStepAsync(ExpandedStep step, ScriptExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var handler = handlers.GetHandler(step.Instruction.CommandType)
            ?? throw new InvalidOperationException($"没有注册 {step.Instruction.CommandType} 的处理器（{step.Origin}）");
        var previousCondition = context.CurrentCondition;
        var previousJump = context.JumpToIndex;
        context.CurrentCondition = step.Condition;
        context.JumpToIndex = null;
        try
        {
            await handler.ExecuteAsync(step.Instruction, context, ct);
            if (step.Instruction.IntervalToNextInstructionMs > 0)
                await Task.Delay(step.Instruction.IntervalToNextInstructionMs, ct);
        }
        finally
        {
            context.CurrentCondition = previousCondition;
            context.JumpToIndex = previousJump;
        }
    }

    public async Task ExecuteAsync(IEnumerable<ExpandedStep> steps, ScriptExecutionContext context, CancellationToken ct)
    {
        foreach (var step in steps) await ExecuteStepAsync(step, context, ct);
    }
}
