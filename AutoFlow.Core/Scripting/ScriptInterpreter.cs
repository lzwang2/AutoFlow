using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Handlers;

namespace AutoFlow.Core.Scripting;

/// <summary>
/// Interprets and executes a script document step by step.
///
/// <para>Execution semantics are unchanged (AC-03): steps run front to back, a Loop
/// instruction can jump backwards, and the script ends when the list is exhausted. What is new
/// is that the list may be produced by expanding <see cref="ScriptDocument.Steps"/> — inline
/// actions and action-block references — instead of being written out literally, and that a
/// step may point at a shared condition instead of carrying its own template path.</para>
/// </summary>
public sealed class ScriptInterpreter
{
    private readonly ActionBlockLibrary? _blocks;
    private readonly ConditionLibrary? _conditions;
    private readonly InstructionHandlerRegistry _handlerRegistry;

    public ScriptInterpreter(
        InstructionHandlerRegistry handlerRegistry,
        ConditionLibrary? conditions = null,
        ActionBlockLibrary? blocks = null)
    {
        _handlerRegistry = handlerRegistry;
        _conditions = conditions;
        _blocks = blocks;
    }

    /// <summary>
    /// Execute a script document from start to finish.
    /// </summary>
    public async Task ExecuteAsync(
        ScriptDocument script,
        ScriptExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);

        var scriptName = script.Name ?? script.Metadata?.Description ?? "未命名";

        // Expansion and condition resolution happen up front, so a missing block or condition
        // fails before any input is sent rather than halfway through a click sequence.
        var steps = new ScriptStepExpander(_conditions, _blocks).Expand(script, scriptName);

        context.SharedResources = new SharedScriptResources(_conditions, _blocks, _handlerRegistry);
        context.SharedResources.ValidateSkillDependencies(steps);

        if (steps.Count == 0)
        {
            context.Log("脚本没有任何步骤");
            return;
        }

        context.Log($"开始执行脚本: {scriptName} ({steps.Count} 步)");
        if (!string.IsNullOrWhiteSpace(script.Metadata?.TargetWindowTitle))
        {
            context.Log($"目标窗口: {script.Metadata!.TargetWindowTitle}");
        }

        WarnOnReferenceSpaceMismatch(steps, script, context);

        var index = 0;

        while (index < steps.Count)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var step = steps[index];
            var instruction = step.Instruction;

            context.CurrentInstructionIndex = index;
            context.JumpToIndex = null;
            context.CurrentCondition = step.Condition;

            var handler = _handlerRegistry.GetHandler(instruction.CommandType);
            if (handler == null)
            {
                context.CurrentCondition = null;
                throw new InvalidOperationException(
                    $"没有注册 {instruction.CommandType} 的处理器（{step.Origin}）");
            }

            try
            {
                await handler.ExecuteAsync(instruction, context, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                context.Log("脚本执行已取消");
                throw;
            }
            catch (Exception ex)
            {
                context.Log($"{step.Origin} ({instruction.CommandType}) 出错: {ex.Message}");
                throw;
            }
            finally
            {
                // Cleared unconditionally: a leftover condition would silently apply to the
                // next instruction, which is the kind of bug that only shows up at runtime.
                context.CurrentCondition = null;
            }

            if (context.JumpToIndex.HasValue)
            {
                var target = context.JumpToIndex.Value;

                if (target < 0 || target >= steps.Count)
                {
                    throw new InvalidOperationException(
                        $"循环跳转目标越界: {target}（脚本共 {steps.Count} 步）");
                }

                index = target;
            }
            else
            {
                index++;
            }
        }

        context.Log("脚本执行完成");
    }

    /// <summary>
    /// Reports conditions whose reference outer size differs from the script's (AC-07).
    /// Warns rather than throws: template matching is scale-sensitive, so a mismatch is very
    /// likely wrong, but the user may be mid-edit and refusing to run would be unhelpful.
    /// The point is that it cannot pass silently.
    /// </summary>
    private static void WarnOnReferenceSpaceMismatch(
        List<ExpandedStep> steps,
        ScriptDocument script,
        ScriptExecutionContext context)
    {
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in steps)
        {
            if (step.Condition is null || !reported.Add(step.Condition.Id))
                continue;

            var check = ReferenceSpaceValidator.Check(step.Condition, script.OuterWidth, script.OuterHeight);
            if (!check.IsOk)
            {
                context.Log($"  [警告] {check.Message}");
            }
        }
    }
}
