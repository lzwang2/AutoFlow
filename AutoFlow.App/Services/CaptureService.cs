using Fischless.GameCapture;
using OpenCvSharp;

namespace AutoFlow.App.Services;

/// <summary>
/// Manages the game capture lifecycle for the target window
/// </summary>
public sealed class CaptureService : IDisposable
{
    private IGameCapture? _capture;
    private nint _hWnd;

    public bool IsCapturing => _capture?.IsCapturing ?? false;

    public IGameCapture? Current => _capture;

    /// <summary>
    /// Start capturing the given window with the given mode
    /// </summary>
    public void Start(nint hWnd, CaptureModes mode)
    {
        Stop();

        _hWnd = hWnd;
        _capture = GameCaptureFactory.Create(mode);
        _capture.Start(hWnd);
    }

    /// <summary>
    /// Capture a single frame (null if not capturing or frame unavailable)
    /// </summary>
    public Mat? CaptureFrame()
    {
        return _capture?.Capture();
    }

    public void Stop()
    {
        if (_capture != null)
        {
            try
            {
                _capture.Stop();
            }
            catch
            {
                // ignore stop errors
            }
            _capture.Dispose();
            _capture = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
