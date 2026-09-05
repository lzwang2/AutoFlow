namespace AutoFlow.Core.Capture;

/// <summary>
/// Service for finding and tracking target window information
/// </summary>
public sealed class WindowInfoService
{
    private string _targetWindowTitle = string.Empty;

    /// <summary>
    /// Set target window title to search for
    /// </summary>
    public void SetTargetWindowTitle(string title)
    {
        _targetWindowTitle = title ?? string.Empty;
    }

    /// <summary>
    /// Get current target window title
    /// </summary>
    public string GetTargetWindowTitle()
    {
        return _targetWindowTitle;
    }

    /// <summary>
    /// Try to get window info for the target window
    /// </summary>
    public bool TryGetWindowInfo(out WindowInfo windowInfo)
    {
        windowInfo = default;

        if (string.IsNullOrEmpty(_targetWindowTitle))
            return false;

        var hWnd = NativeWindowHelper.FindWindowByTitle(_targetWindowTitle, exactMatch: false);
        if (hWnd == IntPtr.Zero)
            return false;

        return TryGetWindowInfo(hWnd, out windowInfo);
    }

    /// <summary>
    /// Try to get window info for a specific window handle
    /// </summary>
    public bool TryGetWindowInfo(nint hWnd, out WindowInfo windowInfo)
    {
        windowInfo = default;

        if (hWnd == IntPtr.Zero)
            return false;

        var title = NativeWindowHelper.GetWindowTitle(hWnd);
        if (!NativeWindowHelper.TryGetWindowBounds(hWnd, out var windowBounds))
            return false;

        if (!NativeWindowHelper.TryGetClientBounds(hWnd, out var clientBounds))
            return false;

        var scaleFactor = NativeWindowHelper.GetWindowDpiScale(hWnd);

        windowInfo = new WindowInfo(hWnd, title, windowBounds, clientBounds, scaleFactor);
        return true;
    }

    /// <summary>
    /// Check if target window is available
    /// </summary>
    public bool IsTargetWindowAvailable()
    {
        return TryGetWindowInfo(out _);
    }

    /// <summary>
    /// Resize the target window to the profile's requested OUTER size and report the
    /// CLIENT bounds that resulted.
    /// </summary>
    /// <remarks>
    /// The distinction matters: the requested size describes the outer rect (matching
    /// AutoHotkey's WinMove), but scripts crop and click in client coordinates. The client
    /// area is smaller by the borders and title bar, so the caller must use the measured
    /// value returned here as the coordinate reference rather than the requested one.
    /// </remarks>
    public bool TryApplyResize(nint hWnd, WindowTargetProfile profile, out WindowBounds measuredClientBounds)
        => TryApplyResize(hWnd, profile, out measuredClientBounds, out _);

    /// <summary>
    /// Same as <see cref="TryApplyResize(nint, WindowTargetProfile, out WindowBounds)"/>, but
    /// reports why it failed.
    /// </summary>
    /// <remarks>
    /// The bool-only overload has four distinct failure paths — no window, resize disabled,
    /// the resize itself rejected by Windows, and the follow-up client measurement failing —
    /// and they look identical to the caller. That is why the UI could only ever say
    /// "调整窗口尺寸失败" without saying what to do about it.
    /// </remarks>
    public bool TryApplyResize(
        nint hWnd, WindowTargetProfile profile,
        out WindowBounds measuredClientBounds, out string failureReason)
    {
        measuredClientBounds = default;
        failureReason = string.Empty;

        if (hWnd == IntPtr.Zero)
        {
            failureReason = "未找到目标窗口（句柄为空），请先选择目标窗口。";
            return false;
        }

        if (!profile.ResizeBeforeRun)
        {
            failureReason = "当前窗口配置未启用「运行前调整窗口尺寸」。";
            return false;
        }

        if (!NativeWindowHelper.TryResizeWindow(
                hWnd, profile.ResizeWidth, profile.ResizeHeight,
                out var resizeFailure, profile.ResizeX, profile.ResizeY))
        {
            failureReason = resizeFailure;
            return false;
        }

        if (!NativeWindowHelper.TryGetClientBounds(hWnd, out measuredClientBounds))
        {
            failureReason = "窗口尺寸已调整，但读取客户区尺寸失败，无法确定坐标基准。";
            return false;
        }

        return true;
    }
}
