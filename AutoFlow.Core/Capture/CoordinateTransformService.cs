using System.Drawing;

namespace AutoFlow.Core.Capture;

/// <summary>
/// Coordinate transformation service: script coordinates ↔ actual window coordinates.
/// The script-side coordinate space is configurable via <see cref="SetReferenceResolution"/>
/// and defaults to 1920x1080.
/// </summary>
public sealed class CoordinateTransformService
{
    /// <summary>Default reference resolution when a script does not declare one.</summary>
    public const int DefaultReferenceWidth = 1920;
    public const int DefaultReferenceHeight = 1080;

    /// <summary>
    /// The coordinate space the script's numbers are expressed in. Scripts adapted from
    /// tools that pin the game window to a specific size (e.g. an AHK script using
    /// WinMove 750x1040) declare that size here, so their raw coordinates stay usable
    /// verbatim instead of being pre-scaled into a 1920x1080 space they never lived in.
    /// </summary>
    public int ReferenceWidth { get; private set; } = DefaultReferenceWidth;
    public int ReferenceHeight { get; private set; } = DefaultReferenceHeight;

    /// <summary>
    /// Point the transform at a script's declared reference resolution. Values &lt;= 0 fall
    /// back to the 1920x1080 default so a malformed script cannot divide by zero.
    /// </summary>
    public void SetReferenceResolution(int width, int height)
    {
        ReferenceWidth = width > 0 ? width : DefaultReferenceWidth;
        ReferenceHeight = height > 0 ? height : DefaultReferenceHeight;
    }

    /// <summary>
    /// Convert script coordinate (in the current reference space) to window-relative coordinate
    /// </summary>
    public Point ScriptToWindow(int scriptX, int scriptY, WindowInfo windowInfo)
    {
        var clientBounds = windowInfo.ClientBounds;
        var safeWidth = clientBounds.Width <= 0 ? 1d : clientBounds.Width;
        var safeHeight = clientBounds.Height <= 0 ? 1d : clientBounds.Height;

        var windowX = (int)Math.Round(scriptX / (double)ReferenceWidth * safeWidth);
        var windowY = (int)Math.Round(scriptY / (double)ReferenceHeight * safeHeight);

        return new Point(windowX, windowY);
    }

    /// <summary>
    /// Convert window-relative coordinate to script coordinate (in the current reference space)
    /// </summary>
    public Point WindowToScript(int windowX, int windowY, WindowInfo windowInfo)
    {
        var clientBounds = windowInfo.ClientBounds;
        var safeWidth = clientBounds.Width <= 0 ? 1d : clientBounds.Width;
        var safeHeight = clientBounds.Height <= 0 ? 1d : clientBounds.Height;

        var scriptX = (int)Math.Round(windowX / safeWidth * ReferenceWidth);
        var scriptY = (int)Math.Round(windowY / safeHeight * ReferenceHeight);

        return new Point(scriptX, scriptY);
    }

    /// <summary>
    /// Convert script coordinate to screen coordinate (absolute)
    /// </summary>
    public Point ScriptToScreen(int scriptX, int scriptY, WindowInfo windowInfo)
    {
        var windowCoordinate = ScriptToWindow(scriptX, scriptY, windowInfo);
        return windowInfo.ClientToScreen(windowCoordinate);
    }

    /// <summary>
    /// Convert screen coordinate (absolute) to script coordinate
    /// </summary>
    public Point ScreenToScript(int screenX, int screenY, WindowInfo windowInfo)
    {
        var windowCoordinate = windowInfo.ScreenToClient(new Point(screenX, screenY));
        return WindowToScript(windowCoordinate.X, windowCoordinate.Y, windowInfo);
    }

    /// <summary>
    /// Check if the window's aspect ratio matches the current reference resolution's
    /// </summary>
    public bool HasReferenceAspectRatio(WindowInfo windowInfo, double tolerance = 0.01)
    {
        var clientBounds = windowInfo.ClientBounds;
        if (clientBounds.Width <= 0 || clientBounds.Height <= 0)
        {
            return false;
        }

        var referenceAspectRatio = ReferenceWidth / (double)ReferenceHeight;
        var currentAspectRatio = clientBounds.Width / (double)clientBounds.Height;
        return Math.Abs(currentAspectRatio - referenceAspectRatio) <= tolerance;
    }
}

