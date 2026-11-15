using AutoFlow.Core.Capture;
using AutoFlow.Core.Input;
using AutoFlow.Core.Vision;
using Fischless.GameCapture;
using OpenCvSharp;

namespace AutoFlow.Core.Scripting;

/// <summary>
/// Execution context passed to instruction handlers.
/// Provides capture, input, coordinate transform, logging, and loop control.
/// </summary>
public sealed class ScriptExecutionContext
{
    public required IGameCapture Capture { get; init; }
    public required InputService Input { get; init; }
    public required CoordinateTransformService CoordinateTransform { get; init; }
    public required WindowInfoService WindowInfo { get; init; }
    public required TemplateMatchService TemplateMatch { get; init; }

    /// <summary>Library snapshots prepared by the interpreter before any actions run.</summary>
    public SharedScriptResources? SharedResources { get; internal set; }

    /// <summary>Log callback for progress messages</summary>
    public Action<string>? Logger { get; init; }

    /// <summary>Include per-tick dispatch and skill-call boundary diagnostics.</summary>
    public bool EnableVerboseLogging { get; init; }

    /// <summary>Debug: enable frame capture for debugging rule matching</summary>
    public bool EnableFrameDebug { get; init; }

    /// <summary>Debug: directory path to save captured frames</summary>
    public string? FrameDebugPath { get; init; }

    /// <summary>Debug: frame capture mode (0=first only, 1=matched only, 2=all)</summary>
    public int FrameDebugMode { get; init; }

    /// <summary>
    /// Loop jump request: when set, interpreter jumps to this instruction index.
    /// Null means continue to next instruction.
    /// </summary>
    public int? JumpToIndex { get; set; }

    /// <summary>Index of the instruction currently executing (set by interpreter)</summary>
    public int CurrentInstructionIndex { get; set; }

    /// <summary>
    /// The shared visual condition the current instruction should use, or null when the
    /// instruction carries inline values.
    ///
    /// <para>Set by the interpreter immediately before invoking a handler and cleared right
    /// after, so it is never read by a later instruction. It lives here rather than on
    /// <see cref="Instructions.Instruction"/> because §11.2 keeps that DTO unchanged, and it
    /// has no region fields to hold a condition anyway.</para>
    /// </summary>
    public Conditions.VisualConditionSpec? CurrentCondition { get; set; }

    /// <summary>Tracks loop iteration counts, keyed by instruction index</summary>
    public Dictionary<int, int> LoopCounters { get; } = new();

    public void Log(string message)
    {
        Logger?.Invoke(message);
    }

    public void LogVerbose(string message)
    {
        if (EnableVerboseLogging) Log("[DEBUG] " + message);
    }

    /// <summary>
    /// Capture a frame from the target window
    /// </summary>
    public Mat? CaptureFrame()
    {
        return Capture.Capture();
    }

    /// <summary>
    /// Get current window info (refreshes on each call)
    /// </summary>
    public WindowInfo GetWindowInfo()
    {
        if (!WindowInfo.TryGetWindowInfo(out var windowInfo))
        {
            throw new InvalidOperationException($"Target window not found: {WindowInfo.GetTargetWindowTitle()}");
        }
        return windowInfo;
    }

    /// <summary>
    /// Transform script coordinates to screen coordinates (helper for handlers)
    /// </summary>
    public System.Drawing.Point ScriptToScreen(int scriptX, int scriptY)
    {
        var windowInfo = GetWindowInfo();
        return CoordinateTransform.ScriptToScreen(scriptX, scriptY, windowInfo);
    }

    /// <summary>
    /// Transform script coordinates to window-relative coordinates (helper for handlers)
    /// </summary>
    public System.Drawing.Point ScriptToWindow(int scriptX, int scriptY)
    {
        var windowInfo = GetWindowInfo();
        return CoordinateTransform.ScriptToWindow(scriptX, scriptY, windowInfo);
    }
}
