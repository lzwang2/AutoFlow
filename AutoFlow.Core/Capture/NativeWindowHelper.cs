using System.Runtime.InteropServices;
using System.Text;

namespace AutoFlow.Core.Capture;

/// <summary>
/// Native Windows API helpers for window operations
/// </summary>
public static partial class NativeWindowHelper
{
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(nint hWnd, [Out] StringBuilder lpString, int nMaxCount);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetrics(int nIndex);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    /// <summary>
    /// Find window by title (exact or contains match)
    /// </summary>
    public static nint FindWindowByTitle(string title, bool exactMatch = false)
    {
        nint foundHandle = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            var windowTitle = GetWindowTitle(hWnd);
            if (string.IsNullOrEmpty(windowTitle))
                return true;

            bool matches = exactMatch
                ? string.Equals(windowTitle, title, StringComparison.Ordinal)
                : windowTitle.Contains(title, StringComparison.OrdinalIgnoreCase);

            if (matches)
            {
                foundHandle = hWnd;
                return false; // Stop enumeration
            }

            return true;
        }, IntPtr.Zero);

        return foundHandle;
    }

    /// <summary>
    /// Get window title
    /// </summary>
    public static string GetWindowTitle(nint hWnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>
    /// Enumerate all visible top-level windows that have a title
    /// </summary>
    public static IReadOnlyList<(nint Handle, string Title)> EnumerateVisibleWindows()
    {
        var results = new List<(nint, string)>();
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd))
                return true;

            var title = GetWindowTitle(hWnd);
            if (!string.IsNullOrWhiteSpace(title))
            {
                results.Add((hWnd, title));
            }

            return true;
        }, IntPtr.Zero);

        return results;
    }

    /// <summary>
    /// Get window bounds (screen coordinates)
    /// </summary>
    public static bool TryGetWindowBounds(nint hWnd, out WindowBounds bounds)
    {
        bounds = default;
        if (!GetWindowRect(hWnd, out var rect))
            return false;

        bounds = new WindowBounds(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
        return true;
    }

    /// <summary>
    /// Get client bounds (screen coordinates)
    /// </summary>
    public static bool TryGetClientBounds(nint hWnd, out WindowBounds bounds)
    {
        bounds = default;
        if (!GetClientRect(hWnd, out var rect))
            return false;

        // Client rect is in window coordinates, convert to screen
        var topLeft = new POINT { X = rect.Left, Y = rect.Top };
        if (!ClientToScreen(hWnd, ref topLeft))
            return false;

        bounds = new WindowBounds(topLeft.X, topLeft.Y, rect.Right - rect.Left, rect.Bottom - rect.Top);
        return true;
    }

    /// <summary>
    /// Resize (and optionally move) a window by setting its OUTER window rect.
    /// This matches AutoHotkey's WinMove semantics, which also operates on the outer
    /// rect — so passing the same numbers an AHK script used reproduces its layout.
    /// </summary>
    /// <remarks>
    /// Note the outer rect includes the title bar and borders, so the resulting CLIENT
    /// area is smaller than <paramref name="width"/> x <paramref name="height"/>. Callers
    /// that need the coordinate reference should measure it via
    /// <see cref="TryGetClientBounds"/> afterwards rather than assuming these values.
    /// </remarks>
    public static bool TryResizeWindow(nint hWnd, int width, int height, int? x = null, int? y = null)
        => TryResizeWindow(hWnd, width, height, out _, x, y);

    /// <summary>
    /// Same as <see cref="TryResizeWindow(nint, int, int, int?, int?)"/>, but reports why it
    /// failed.
    /// </summary>
    /// <remarks>
    /// <paramref name="failureReason"/> exists because <c>SetWindowPos</c> declares
    /// <c>SetLastError = true</c> yet the bool-only overload discarded that error, leaving
    /// "resize failed" as the only thing the log could ever say. Diagnosing it then meant
    /// guessing from outside the program — maximised? not resizable? elevated target? — when
    /// Windows had already answered.
    /// </remarks>
    public static bool TryResizeWindow(
        nint hWnd, int width, int height, out string failureReason, int? x = null, int? y = null)
    {
        failureReason = string.Empty;

        if (hWnd == IntPtr.Zero)
        {
            failureReason = "窗口句柄为空。";
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            failureReason = $"目标尺寸无效: {width}×{height}。";
            return false;
        }

        // Preserve the current position when no explicit origin was requested.
        var left = x ?? 0;
        var top = y ?? 0;
        if (x is null || y is null)
        {
            if (!TryGetWindowBounds(hWnd, out var current))
            {
                failureReason = $"读取当前窗口位置失败 (GetWindowRect: {DescribeLastError()})。";
                return false;
            }
            left = x ?? current.Left;
            top = y ?? current.Top;
        }

        if (SetWindowPos(hWnd, IntPtr.Zero, left, top, width, height, SwpNoZOrder | SwpNoActivate))
            return true;

        // Read the code before anything else can overwrite it.
        var error = Marshal.GetLastWin32Error();

        var hint = error switch
        {
            // "Access is denied" / "Invalid window handle" are the two that actually show up
            // here, and they mean very different fixes, so they are spelled out rather than
            // left as a bare number.
            5 => "目标进程的权限高于本程序，需以管理员身份运行 AutoFlow。",
            1400 => "窗口句柄已失效，请刷新窗口列表后重新选择。",
            0 => "系统未报告错误码；窗口可能处于最大化/全屏状态，或其样式不允许调整大小"
                 + "（缺少 WS_THICKFRAME）。请先还原窗口再试。",
            _ => "可能原因: 窗口已最大化/全屏、窗口不可调整大小、或目标进程权限更高。"
        };

        failureReason = $"SetWindowPos 失败 (Win32 错误 {error}: {DescribeError(error)})。{hint}";
        return false;
    }

    private static string DescribeLastError() => DescribeError(Marshal.GetLastWin32Error());

    private static string DescribeError(int error)
        => error == 0 ? "无错误码" : new System.ComponentModel.Win32Exception(error).Message;

    /// <summary>
    /// Check if window is foreground
    /// </summary>
    public static bool IsForegroundWindow(nint hWnd)
    {
        return GetForegroundWindow() == hWnd;
    }

    /// <summary>
    /// Try to activate (bring to foreground) window
    /// </summary>
    public static bool TryActivateWindow(nint hWnd)
    {
        return SetForegroundWindow(hWnd);
    }

    /// <summary>
    /// Get DPI scale factor for window
    /// </summary>
    public static double GetWindowDpiScale(nint hWnd)
    {
        // For now, return 1.0 (TODO: implement proper DPI awareness if needed)
        return 1.0;
    }

    /// <summary>
    /// Get virtual screen bounds (spans all monitors)
    /// </summary>
    public static WindowBounds GetVirtualScreenBounds()
    {
        var left = GetSystemMetrics(SmXVirtualScreen);
        var top = GetSystemMetrics(SmYVirtualScreen);
        var width = GetSystemMetrics(SmCxVirtualScreen);
        var height = GetSystemMetrics(SmCyVirtualScreen);

        if (width <= 0 || height <= 0)
        {
            return new WindowBounds(0, 0, 1, 1);
        }

        return new WindowBounds(left, top, width, height);
    }

    /// <summary>
    /// Convert screen coordinate to virtual-desktop absolute coordinate (0-65535 range)
    /// required by SendInput with MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK.
    /// </summary>
    public static (double X, double Y) ToVirtualDesktopAbsoluteCoordinate(int screenX, int screenY)
    {
        var bounds = GetVirtualScreenBounds();
        var x = ScaleToAbsoluteCoordinate(screenX, bounds.Left, bounds.Width);
        var y = ScaleToAbsoluteCoordinate(screenY, bounds.Top, bounds.Height);
        return (x, y);
    }

    private static double ScaleToAbsoluteCoordinate(double coordinate, double origin, double length)
    {
        if (length <= 1d)
        {
            return 0d;
        }

        var normalized = (coordinate - origin) * 65535d / (length - 1d);
        return Math.Clamp(normalized, 0d, 65535d);
    }
}
