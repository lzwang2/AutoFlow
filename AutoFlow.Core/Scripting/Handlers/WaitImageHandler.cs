using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Instructions;

namespace AutoFlow.Core.Scripting.Handlers;

/// <summary>
/// Handler for WaitImage instruction - polls until the template image is found, or times out.
///
/// <para>The visual check itself is delegated to <see cref="VisualConditionEvaluator"/>, the
/// same code the rule loop uses. Before this, the rule loop cropped to the rule's region while
/// this handler always matched the whole frame — so a shared condition would have matched
/// differently here than there. Region handling now comes from the condition, not the caller.</para>
///
/// <para>The timeout stays here, at the call site, and never moves into the condition (FR-09):
/// the same condition can be awaited for 5s in one script and 30s in another.</para>
/// </summary>
public sealed class WaitImageHandler : InstructionHandlerBase
{
    private const int DefaultPollIntervalMs = 500;

    public override CommandType CommandType => CommandType.WaitImage;

    public override async Task ExecuteAsync(Instruction instruction, ScriptExecutionContext context, CancellationToken cancellationToken)
    {
        var condition = ResolveCondition(instruction, context);
        var timeoutMs = instruction.WaitTimeoutMs;

        var regionNote = condition.HasRegion
            ? $", region=({condition.RegionX1},{condition.RegionY1})-({condition.RegionX2},{condition.RegionY2})"
            : ", region=整帧";

        context.Log(
            $"WaitImage: {condition.TemplatePath} (threshold={condition.MatchThreshold:F2}, " +
            $"meanDiff={condition.MeanDiffThreshold:F1}, timeout={timeoutMs}ms{regionNote})");

        var startTime = DateTime.UtcNow;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if ((DateTime.UtcNow - startTime).TotalMilliseconds > timeoutMs)
            {
                throw new TimeoutException(
                    $"WaitImage 超时 ({timeoutMs}ms): {condition.TemplatePath}");
            }

            using var frame = context.CaptureFrame();
            if (frame == null || frame.Empty())
            {
                context.Log("WaitImage: 截图失败，重试...");
                await DelayAsync(DefaultPollIntervalMs, cancellationToken);
                continue;
            }

            var match = VisualConditionEvaluator.Evaluate(condition, frame, context);

            if (match is not null)
            {
                var found = match.Value;
                context.Log($"WaitImage: 命中于 ({found.X}, {found.Y}) score={found.Score:F3}");

                if (instruction.ClickAfterMatch)
                {
                    // Match coords are frame (window) space — the evaluator already added back
                    // any ROI offset — so this converts window → script → screen.
                    var windowInfo = context.GetWindowInfo();
                    var scriptPoint = context.CoordinateTransform.WindowToScript(found.CenterX, found.CenterY, windowInfo);
                    var screenPos = context.CoordinateTransform.ScriptToScreen(scriptPoint.X, scriptPoint.Y, windowInfo);

                    context.Log($"WaitImage: 点击命中中心 → 屏幕 ({screenPos.X}, {screenPos.Y})");
                    context.Input.ClickAt(screenPos.X, screenPos.Y);
                    await DelayAsync(50, cancellationToken);
                }

                return;
            }

            await DelayAsync(DefaultPollIntervalMs, cancellationToken);
        }
    }

    /// <summary>
    /// Uses the shared condition the interpreter resolved, when there is one; otherwise builds
    /// an ad-hoc condition from the instruction's own fields, which is how a WaitImage authored
    /// with an inline TemplatePath keeps working. An inline WaitImage has no region, so it
    /// matches the whole frame exactly as before.
    /// </summary>
    private static VisualConditionSpec ResolveCondition(Instruction instruction, ScriptExecutionContext context)
    {
        if (context.CurrentCondition is { } shared)
            return shared;

        if (string.IsNullOrWhiteSpace(instruction.TemplatePath))
        {
            throw new InvalidOperationException(
                "WaitImage 需要 TemplatePath 或一个 ConditionRef，两者都没有提供。");
        }

        return new VisualConditionSpec
        {
            Id = "(内联)",
            TemplatePath = instruction.TemplatePath,
            MatchThreshold = instruction.MatchThreshold,
            MeanDiffThreshold = instruction.MeanDiffThreshold
            // Region left at -1: an inline WaitImage has never had a region, and inventing
            // one here would change existing behaviour.
        };
    }
}
