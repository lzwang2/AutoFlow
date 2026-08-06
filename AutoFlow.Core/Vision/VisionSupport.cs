using OpenCvSharp;
using System.Drawing;
using AutoFlow.Core.Models;
using OpenCvRect = OpenCvSharp.Rect;
using OpenCvSize = OpenCvSharp.Size;

namespace AutoFlow.Core.Vision;

/// <summary>
/// Vision support utilities: coordinate scaling, template loading, matching
/// </summary>
public static class VisionSupport
{
    public static readonly OpenCvSize Reference1080p = new(1920, 1080);

    public static void ValidateFrameSize(int frameWidth, int frameHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameHeight);
    }

    public static System.Drawing.Point ToReference1080p(System.Drawing.Point point, int frameWidth, int frameHeight)
    {
        ValidateFrameSize(frameWidth, frameHeight);
        return new System.Drawing.Point(
            point.X * Reference1080p.Width / frameWidth,
            point.Y * Reference1080p.Height / frameHeight);
    }

    public static OpenCvRect ScaleReferenceRect(OpenCvRect referenceRect, int frameWidth, int frameHeight)
    {
        ValidateFrameSize(frameWidth, frameHeight);

        var x = (int)Math.Round(referenceRect.X / (double)Reference1080p.Width * frameWidth);
        var y = (int)Math.Round(referenceRect.Y / (double)Reference1080p.Height * frameHeight);
        var right = (int)Math.Round(referenceRect.Right / (double)Reference1080p.Width * frameWidth);
        var bottom = (int)Math.Round(referenceRect.Bottom / (double)Reference1080p.Height * frameHeight);

        x = Math.Clamp(x, 0, Math.Max(0, frameWidth - 1));
        y = Math.Clamp(y, 0, Math.Max(0, frameHeight - 1));
        right = Math.Clamp(right, x + 1, frameWidth);
        bottom = Math.Clamp(bottom, y + 1, frameHeight);

        return new OpenCvRect(x, y, right - x, bottom - y);
    }

    public static RawTemplate LoadTemplate(string fullPath, string symbol)
    {
        var image = Cv2.ImRead(fullPath, ImreadModes.Unchanged);
        if (image.Empty())
        {
            throw new FileNotFoundException($"Failed to load template '{fullPath}'.", fullPath);
        }

        using var alphaMask = ExtractAlphaMask(image);
        var cropBounds = FindOpaqueBounds(alphaMask);
        var boundedImage = new Mat(image, cropBounds).Clone();
        using var boundedMaskSource = new Mat(alphaMask, cropBounds);
        var boundedMask = boundedMaskSource.Clone();
        return new RawTemplate(symbol, boundedImage, boundedMask);
    }

    private static Mat ExtractAlphaMask(Mat image)
    {
        if (image.Channels() < 4)
        {
            var mask = image.Channels() == 1
                ? image.Clone()
                : new Mat();

            if (image.Channels() == 3)
            {
                Cv2.CvtColor(image, mask, ColorConversionCodes.BGR2GRAY);
            }

            Cv2.Threshold(mask, mask, 0, 255, ThresholdTypes.Binary);
            return mask;
        }

        var alphaMask = new Mat();
        Cv2.ExtractChannel(image, alphaMask, 3);
        Cv2.Threshold(alphaMask, alphaMask, 0, 255, ThresholdTypes.Binary);
        return alphaMask;
    }

    private static OpenCvRect FindOpaqueBounds(Mat mask)
    {
        using var maskCopy = mask.Clone();
        Cv2.FindContours(maskCopy, out var contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (contours.Length == 0)
        {
            return new OpenCvRect(0, 0, mask.Width, mask.Height);
        }

        var left = mask.Width;
        var top = mask.Height;
        var right = 0;
        var bottom = 0;

        foreach (var contour in contours)
        {
            var bounds = Cv2.BoundingRect(contour);
            left = Math.Min(left, bounds.X);
            top = Math.Min(top, bounds.Y);
            right = Math.Max(right, bounds.Right);
            bottom = Math.Max(bottom, bounds.Bottom);
        }

        return new OpenCvRect(left, top, right - left, bottom - top);
    }
}

/// <summary>
/// Template matching with mean gray difference check to filter false positives
/// </summary>
public static class TemplateMatcher
{
    /// <summary>
    /// Try to find best template match with mean gray difference validation
    /// </summary>
    public static bool TryFindBestMatch(
        TemplateMatchService templateMatchService,
        Mat captureRegion,
        IReadOnlyList<PreparedTemplate> templates,
        TemplateMatchOptions matchOptions,
        double threshold,
        double maxMeanDiff,
        out TemplateMatchInfo matchInfo)
    {
        matchInfo = default;
        var found = false;

        foreach (var template in templates)
        {
            if (captureRegion.Width < template.Width || captureRegion.Height < template.Height)
            {
                continue;
            }

            var candidate = templateMatchService.Match(
                captureRegion,
                template.Image,
                matchOptions.UseMask ? template.Mask : null,
                threshold,
                matchOptions);

            if (!candidate.IsMatch)
            {
                continue;
            }

            // Apply mean gray difference check to filter false positives
            if (!ValidateMeanGrayDifference(captureRegion, template.Image, candidate, maxMeanDiff))
            {
                continue;
            }

            if (!found || candidate.Score > matchInfo.Score)
            {
                matchInfo = candidate;
                found = true;
            }
        }

        return found;
    }

    private static bool ValidateMeanGrayDifference(Mat captureRegion, Mat template, TemplateMatchInfo matchInfo, double maxMeanDiff)
    {
        // Extract the matched region from capture
        var matchRect = new OpenCvRect(matchInfo.X, matchInfo.Y, matchInfo.Width, matchInfo.Height);
        using var matchedRegion = new Mat(captureRegion, matchRect);

        // Convert both to grayscale if needed
        using var templateGray = template.Channels() == 1 ? template.Clone() : ConvertToGray(template);
        using var matchedGray = matchedRegion.Channels() == 1 ? matchedRegion.Clone() : ConvertToGray(matchedRegion);

        // Calculate mean gray values
        var templateMean = templateGray.Mean()[0];
        var matchedMean = matchedGray.Mean()[0];

        // Check if mean difference is within threshold
        var meanDiff = Math.Abs(templateMean - matchedMean);
        return meanDiff <= maxMeanDiff;
    }

    private static Mat ConvertToGray(Mat image)
    {
        var gray = new Mat();
        var colorConversionCode = image.Channels() switch
        {
            3 => ColorConversionCodes.BGR2GRAY,
            4 => ColorConversionCodes.BGRA2GRAY,
            _ => throw new NotSupportedException($"Unsupported channel count {image.Channels()} for grayscale conversion.")
        };
        Cv2.CvtColor(image, gray, colorConversionCode);
        return gray;
    }
}

/// <summary>
/// Prepared template ready for matching (scaled to target resolution)
/// </summary>
public sealed class PreparedTemplate
{
    public PreparedTemplate(string symbol, Mat image, Mat mask)
    {
        Symbol = symbol;
        Image = image;
        Mask = mask;
    }

    public string Symbol { get; }
    public Mat Image { get; }
    public Mat Mask { get; }
    public int Width => Image.Width;
    public int Height => Image.Height;
}

/// <summary>
/// Raw template loaded from disk (not yet scaled)
/// </summary>
public sealed class RawTemplate
{
    public RawTemplate(string symbol, Mat image, Mat mask)
    {
        Symbol = symbol;
        Image = image;
        Mask = mask;
    }

    public string Symbol { get; }
    public Mat Image { get; }
    public Mat Mask { get; }

    public PreparedTemplate Scale(double scaleX, double scaleY)
    {
        var width = Math.Max(1, (int)Math.Round(Image.Width * scaleX));
        var height = Math.Max(1, (int)Math.Round(Image.Height * scaleY));
        var interpolation = scaleX < 1d || scaleY < 1d
            ? InterpolationFlags.Area
            : InterpolationFlags.Linear;

        var scaledImage = new Mat();
        var scaledMask = new Mat();
        Cv2.Resize(Image, scaledImage, new OpenCvSize(width, height), 0d, 0d, interpolation);
        Cv2.Resize(Mask, scaledMask, new OpenCvSize(width, height), 0d, 0d, InterpolationFlags.Nearest);
        Cv2.Threshold(scaledMask, scaledMask, 0, 255, ThresholdTypes.Binary);
        return new PreparedTemplate(Symbol, scaledImage, scaledMask);
    }
}
