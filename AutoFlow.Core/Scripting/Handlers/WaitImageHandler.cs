using AutoFlow.Core.Models;
using AutoFlow.Core.Scripting.Instructions;
using OpenCvSharp;

namespace AutoFlow.Core.Scripting.Handlers;

/// <summary>
/// Handler for WaitImage instruction - waits until template image is found
/// </summary>
public sealed class WaitImageHandler : InstructionHandlerBase
{
    private const int DefaultPollIntervalMs = 500;
    private const int DefaultTimeoutMs = 30000;

    public override CommandType CommandType => CommandType.WaitImage;

    public override async Task ExecuteAsync(Instruction instruction, ScriptExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(instruction.TemplatePath))
        {
            throw new InvalidOperationException("WaitImage instruction requires TemplatePath");
        }

        var threshold = instruction.MatchThreshold;
        var meanDiffThreshold = instruction.MeanDiffThreshold;
        var timeoutMs = instruction.WaitTimeoutMs;
        var pollIntervalMs = DefaultPollIntervalMs;

        context.Log($"WaitImage: {instruction.TemplatePath} (threshold={threshold:F2}, meanDiff={meanDiffThreshold:F1}, timeout={timeoutMs}ms)");

        var startTime = DateTime.UtcNow;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check timeout
            if ((DateTime.UtcNow - startTime).TotalMilliseconds > timeoutMs)
            {
                throw new TimeoutException($"WaitImage timed out after {timeoutMs}ms: {instruction.TemplatePath}");
            }

            // Capture frame
            using var frame = context.CaptureFrame();
            if (frame == null)
            {
                context.Log("WaitImage: capture failed, retrying...");
                await DelayAsync(pollIntervalMs, cancellationToken);
                continue;
            }

            // Try to find template
            var matchInfo = context.TemplateMatch.TryFindTemplate(
                frame,
                instruction.TemplatePath,
                new TemplateMatchOptions
                {
                    Method = TemplateMatchModes.CCoeffNormed,
                    UseMask = false
                },
                threshold,
                meanDiffThreshold
            );

            if (matchInfo != null)
            {
                var match = matchInfo.Value;
                context.Log($"WaitImage: found at ({match.X}, {match.Y}) score={match.Score:F3}");

                if (instruction.ClickAfterMatch)
                {
                    // Match coords are in captured-frame (window) space; convert to script then screen
                    var windowInfo = context.GetWindowInfo();
                    var centerX = match.X + match.Width / 2;
                    var centerY = match.Y + match.Height / 2;
                    var scriptPoint = context.CoordinateTransform.WindowToScript(centerX, centerY, windowInfo);
                    var screenPos = context.CoordinateTransform.ScriptToScreen(scriptPoint.X, scriptPoint.Y, windowInfo);

                    context.Log($"WaitImage: clicking match center → screen ({screenPos.X}, {screenPos.Y})");
                    context.Input.ClickAt(screenPos.X, screenPos.Y);
                    await DelayAsync(50, cancellationToken);
                }

                return;
            }

            // Not found, wait and retry
            await DelayAsync(pollIntervalMs, cancellationToken);
        }
    }
}
