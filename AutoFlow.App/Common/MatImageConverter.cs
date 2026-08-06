using System.IO;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace AutoFlow.App.Common;

/// <summary>
/// Converts OpenCV Mat to WPF BitmapSource for display
/// </summary>
public static class MatImageConverter
{
    /// <summary>
    /// Convert a Mat to a frozen BitmapSource (safe for cross-thread display)
    /// </summary>
    public static BitmapSource ToBitmapSource(Mat mat)
    {
        // Encode to PNG in memory then decode into a WPF bitmap.
        // Simple and robust across channel counts; performance is fine for preview.
        Cv2.ImEncode(".bmp", mat, out var buffer);

        using var stream = new MemoryStream(buffer);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }
}
