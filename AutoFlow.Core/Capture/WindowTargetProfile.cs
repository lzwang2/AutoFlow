namespace AutoFlow.Core.Capture;

/// <summary>
/// How the target window should be sized before a script runs, and which coordinate
/// space that script's numbers live in.
/// </summary>
/// <remarks>
/// This exists to support scripts adapted from tools that pin the game window to a fixed
/// size before matching — e.g. an AHK script calling <c>WinMove 1174, 3, 750, 1040</c> and
/// then cropping client-relative regions. Template images captured that way are only
/// matchable at that window size (OpenCV template matching is not scale-invariant), and
/// the recorded coordinates are only meaningful in that same space.
/// </remarks>
public sealed class WindowTargetProfile
{
    /// <summary>Human-readable label, e.g. "Last Cloudia (AHK 750x1040)".</summary>
    public string Name { get; set; } = "";

    /// <summary>Window title substring used to locate the target window.</summary>
    public string? WindowTitle { get; set; }

    /// <summary>
    /// When true, the target window is resized to <see cref="ResizeWidth"/> x
    /// <see cref="ResizeHeight"/> before a run.
    /// </summary>
    public bool ResizeBeforeRun { get; set; }

    /// <summary>
    /// Desired OUTER window size, matching AutoHotkey's WinMove semantics so the same
    /// numbers an AHK script used can be pasted here directly.
    /// </summary>
    public int ResizeWidth { get; set; }
    public int ResizeHeight { get; set; }

    /// <summary>Optional window origin. Null leaves the current position untouched.</summary>
    public int? ResizeX { get; set; }
    public int? ResizeY { get; set; }

    /// <summary>
    /// The coordinate space this profile's scripts are authored in. For AHK-derived
    /// scripts this is the measured CLIENT size that results from the resize above —
    /// not necessarily equal to <see cref="ResizeWidth"/>/<see cref="ResizeHeight"/>,
    /// which describe the outer rect including borders and title bar.
    /// </summary>
    public int ReferenceWidth { get; set; } = CoordinateTransformService.DefaultReferenceWidth;
    public int ReferenceHeight { get; set; } = CoordinateTransformService.DefaultReferenceHeight;

    public WindowTargetProfile Clone() => new()
    {
        Name = Name,
        WindowTitle = WindowTitle,
        ResizeBeforeRun = ResizeBeforeRun,
        ResizeWidth = ResizeWidth,
        ResizeHeight = ResizeHeight,
        ResizeX = ResizeX,
        ResizeY = ResizeY,
        ReferenceWidth = ReferenceWidth,
        ReferenceHeight = ReferenceHeight,
    };
}
