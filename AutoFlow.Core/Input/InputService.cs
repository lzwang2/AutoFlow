using AutoFlow.Core.Capture;
using Fischless.WindowsInput;

namespace AutoFlow.Core.Input;

/// <summary>
/// High-level input service for mouse and keyboard operations.
/// Screen coordinates are normalized to the virtual-desktop 0-65535 range
/// before being sent via SendInput, matching BetterBTD's working behavior.
/// </summary>
public sealed class InputService
{
    private readonly IInputSimulator _simulator;

    public InputService(IInputSimulator simulator)
    {
        _simulator = simulator;
    }

    /// <summary>
    /// Move mouse to absolute screen coordinates and click
    /// </summary>
    public void ClickAt(int screenX, int screenY)
    {
        var (absX, absY) = NativeWindowHelper.ToVirtualDesktopAbsoluteCoordinate(screenX, screenY);
        _simulator.Mouse
            .MoveMouseToPositionOnVirtualDesktop(absX, absY)
            .LeftButtonDown()
            .Sleep(50)
            .LeftButtonUp();
    }

    /// <summary>
    /// Click at current mouse position
    /// </summary>
    public void Click()
    {
        _simulator.Mouse.LeftButtonClick();
    }

    /// <summary>
    /// Move mouse to absolute screen coordinates
    /// </summary>
    public void MoveTo(int screenX, int screenY)
    {
        var (absX, absY) = NativeWindowHelper.ToVirtualDesktopAbsoluteCoordinate(screenX, screenY);
        _simulator.Mouse.MoveMouseToPositionOnVirtualDesktop(absX, absY);
    }

    public void RightClickAt(int screenX, int screenY)
    {
        var (absX, absY) = NativeWindowHelper.ToVirtualDesktopAbsoluteCoordinate(screenX, screenY);
        _simulator.Mouse
            .MoveMouseToPositionOnVirtualDesktop(absX, absY)
            .RightButtonDown()
            .Sleep(50)
            .RightButtonUp();
    }

    public void DoubleClickAt(int screenX, int screenY)
    {
        var (absX, absY) = NativeWindowHelper.ToVirtualDesktopAbsoluteCoordinate(screenX, screenY);
        _simulator.Mouse
            .MoveMouseToPositionOnVirtualDesktop(absX, absY)
            .LeftButtonDoubleClick();
    }
}
