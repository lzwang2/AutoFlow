using System.IO;
using AutoFlow.Core.Scripting.Conditions;
using OpenCvSharp;

namespace AutoFlow.App.Services;

/// <summary>Replaces an existing condition image without changing its metadata.</summary>
public static class ConditionImageRecapture
{
    public static string Save(Mat frame, VisualConditionSpec condition, string templateRoot)
    {
        if (frame.Empty()) throw new InvalidOperationException("无法获取画面帧。");
        if (!condition.HasRegion || condition.ReferenceOuterWidth <= 0 || condition.ReferenceOuterHeight <= 0)
            throw new InvalidOperationException("条件必须具有裁剪区域和有效的参照尺寸。");

        var left = (int)Math.Round(Math.Min(condition.RegionX1, condition.RegionX2) * (double)frame.Width / condition.ReferenceOuterWidth);
        var top = (int)Math.Round(Math.Min(condition.RegionY1, condition.RegionY2) * (double)frame.Height / condition.ReferenceOuterHeight);
        var right = (int)Math.Round(Math.Max(condition.RegionX1, condition.RegionX2) * (double)frame.Width / condition.ReferenceOuterWidth);
        var bottom = (int)Math.Round(Math.Max(condition.RegionY1, condition.RegionY2) * (double)frame.Height / condition.ReferenceOuterHeight);
        if (left < 0 || top < 0 || right > frame.Width || bottom > frame.Height || right - left < 2 || bottom - top < 2)
            throw new InvalidOperationException("条件区域超出当前画面或区域太小，请检查区域和参照尺寸。");

        var path = condition.EffectiveTemplatePath;
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("条件没有图片路径。");
        if (!Path.IsPathRooted(path)) path = Path.Combine(templateRoot, path);
        if (!File.Exists(path)) throw new FileNotFoundException("条件图片不存在，无法覆盖。", path);

        using var crop = new Mat(frame, new Rect(left, top, right - left, bottom - top));
        crop.SaveImage(path);
        return Path.GetFullPath(path);
    }
}
