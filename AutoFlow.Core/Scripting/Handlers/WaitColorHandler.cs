using AutoFlow.Core.Scripting.Instructions;
using OpenCvSharp;
using System.Globalization;

namespace AutoFlow.Core.Scripting.Handlers;

/// <summary>
/// Handler for WaitColor instruction - waits until pixel at position matches expected color
/// </summary>
public sealed class WaitColorHandler : InstructionHandlerBase
{
    private const int DefaultPollIntervalMs = 500;
    private const int DefaultTimeoutMs = 30000;
    private const int DefaultColorTolerance = 10;

    public override CommandType CommandType => CommandType.WaitColor;

    public override async Task ExecuteAsync(Instruction instruction, ScriptExecutionContext context, CancellationToken cancellationToken)
    {
        var scriptX = (int)Math.Round(instruction.PositionX);
        var scriptY = (int)Math.Round(instruction.PositionY);

        if (string.IsNullOrWhiteSpace(instruction.WaitColorHex))
        {
            throw new InvalidOperationException("WaitColor instruction requires WaitColorHex");
        }

        var expectedColor = ParseHexColor(instruction.WaitColorHex);
        var tolerance = instruction.WaitColorTolerance;
        var timeoutMs = instruction.WaitTimeoutMs;
        var pollIntervalMs = DefaultPollIntervalMs;

        // Transform from script coordinates to window-relative coordinates
        var windowPos = context.ScriptToWindow(scriptX, scriptY);

        context.Log($"WaitColor: ({scriptX}, {scriptY}) = {instruction.WaitColorHex} (tolerance={tolerance}, timeout={timeoutMs}ms)");

        var startTime = DateTime.UtcNow;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Check timeout
            if ((DateTime.UtcNow - startTime).TotalMilliseconds > timeoutMs)
            {
                throw new TimeoutException($"WaitColor timed out after {timeoutMs}ms at ({scriptX}, {scriptY})");
            }

            // Capture frame
            using var frame = context.CaptureFrame();
            if (frame == null)
            {
                context.Log("WaitColor: capture failed, retrying...");
                await DelayAsync(pollIntervalMs, cancellationToken);
                continue;
            }

            // Check bounds
            if (windowPos.X < 0 || windowPos.Y < 0 || windowPos.X >= frame.Width || windowPos.Y >= frame.Height)
            {
                throw new InvalidOperationException($"WaitColor position out of bounds: ({windowPos.X}, {windowPos.Y}) in {frame.Width}x{frame.Height} frame");
            }

            // Get pixel color
            var actualColor = GetPixelColor(frame, windowPos.X, windowPos.Y);

            // Check if color matches within tolerance
            if (ColorsMatch(expectedColor, actualColor, tolerance))
            {
                context.Log($"WaitColor: matched #{actualColor.R:X2}{actualColor.G:X2}{actualColor.B:X2}");
                return;
            }

            // Not matched, wait and retry
            await DelayAsync(pollIntervalMs, cancellationToken);
        }
    }

    private static (byte R, byte G, byte B) ParseHexColor(string hex)
    {
        // Remove # prefix if present
        hex = hex.TrimStart('#');

        if (hex.Length != 6)
        {
            throw new ArgumentException($"Invalid hex color format: {hex}. Expected 6 hex digits.");
        }

        var r = byte.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
        var g = byte.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
        var b = byte.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);

        return (r, g, b);
    }

    private static (byte R, byte G, byte B) GetPixelColor(Mat frame, int x, int y)
    {
        // OpenCV uses BGR order
        var pixel = frame.At<Vec3b>(y, x);
        return (pixel.Item2, pixel.Item1, pixel.Item0); // Convert BGR to RGB
    }

    private static bool ColorsMatch((byte R, byte G, byte B) expected, (byte R, byte G, byte B) actual, int tolerance)
    {
        return Math.Abs(expected.R - actual.R) <= tolerance
            && Math.Abs(expected.G - actual.G) <= tolerance
            && Math.Abs(expected.B - actual.B) <= tolerance;
    }
}
