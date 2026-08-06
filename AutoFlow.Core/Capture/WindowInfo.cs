using System.Drawing;

namespace AutoFlow.Core.Capture;

/// <summary>
/// Window bounds in screen coordinates
/// </summary>
public readonly record struct WindowBounds(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;
    public int Bottom => Top + Height;
}

/// <summary>
/// Information about the target window for automation
/// </summary>
public readonly record struct WindowInfo(
    nint Handle,
    string Title,
    WindowBounds WindowBounds,
    WindowBounds ClientBounds,
    double ScaleFactor)
{
    public int ClientWidth => ClientBounds.Width;
    public int ClientHeight => ClientBounds.Height;

    public Point ScreenToClient(Point screenPoint)
    {
        return new Point(
            screenPoint.X - ClientBounds.Left,
            screenPoint.Y - ClientBounds.Top);
    }

    public Point ClientToScreen(Point clientPoint)
    {
        return new Point(
            ClientBounds.Left + clientPoint.X,
            ClientBounds.Top + clientPoint.Y);
    }
}
