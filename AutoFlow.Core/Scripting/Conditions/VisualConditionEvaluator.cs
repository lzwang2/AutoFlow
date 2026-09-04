using AutoFlow.Core.Models;
using OpenCvSharp;

namespace AutoFlow.Core.Scripting.Conditions;

/// <summary>
/// Where a condition matched, in captured-frame (window) pixel coordinates.
/// Region-scoped matches are reported in frame space, not ROI space — the ROI offset is
/// added back — so callers never need to know whether cropping happened.
/// </summary>
public readonly record struct ConditionMatch(int X, int Y, int Width, int Height, double Score)
{
    public int CenterX => X + Width / 2;
    public int CenterY => Y + Height / 2;
}

/// <summary>
/// Single-frame evaluation of a <see cref="VisualConditionSpec"/>.
///
/// This is the one place image detection lives. Previously the rule loop cropped to a
/// region before matching while <c>WaitImageHandler</c> always matched the whole frame — the
/// same condition would therefore behave differently depending on which interpreter ran it.
/// Both now call in here (Spec §12.1 step 4, §12.2 step 3).
///
/// <para>No timeout and no polling: those belong to the call site (FR-09). The rule loop
/// evaluates once per tick; the linear <c>WaitImage</c> polls this in a loop.</para>
/// </summary>
public static class VisualConditionEvaluator
{
    private static readonly TemplateMatchOptions DefaultOptions = new()
    {
        Method = TemplateMatchModes.CCoeffNormed,
        UseMask = false
    };

    /// <summary>
    /// Evaluates <paramref name="condition"/> against a single frame.
    /// Returns null when the template is absent, unreadable, or does not match.
    /// </summary>
    public static ConditionMatch? Evaluate(
        VisualConditionSpec condition,
        Mat frame,
        ScriptExecutionContext context)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(context);

        // EffectiveTemplatePath, not TemplatePath: the latter stays relative as authored, and
        // only the resolved form is openable.
        var templatePath = condition.EffectiveTemplatePath;

        if (string.IsNullOrWhiteSpace(templatePath))
        {
            context.Log($"  [条件] '{condition.Id}' 没有模板图片，无法判定");
            return null;
        }

        if (!File.Exists(templatePath))
        {
            context.Log($"  [条件] '{condition.Id}' 模板文件不存在: {templatePath}");
            return null;
        }

        if (!condition.HasRegion)
        {
            var whole = context.TemplateMatch.TryFindTemplate(
                frame,
                templatePath,
                DefaultOptions,
                condition.MatchThreshold,
                condition.MeanDiffThreshold);

            return whole is null
                ? null
                : new ConditionMatch(whole.Value.X, whole.Value.Y, whole.Value.Width, whole.Value.Height, whole.Value.Score);
        }

        var roiRect = ResolveRegion(condition, frame, context);
        using var roi = new Mat(frame, roiRect);

        var regionMatch = context.TemplateMatch.TryFindTemplate(
            roi,
            templatePath,
            DefaultOptions,
            condition.MatchThreshold,
            condition.MeanDiffThreshold);

        if (regionMatch is null)
            return null;

        // Translate ROI-local coordinates back into frame space so a caller that clicks the
        // match centre lands in the right place. Forgetting this offset would put every
        // region-scoped click near the top-left of the window.
        var m = regionMatch.Value;
        return new ConditionMatch(roiRect.X + m.X, roiRect.Y + m.Y, m.Width, m.Height, m.Score);
    }

    /// <summary>
    /// Converts a condition's region from the script's reference coordinate space into a
    /// clamped rectangle in frame pixels. Extracted from RuleLoopInterpreter unchanged.
    /// </summary>
    public static Rect ResolveRegion(
        VisualConditionSpec condition,
        Mat frame,
        ScriptExecutionContext context)
    {
        var windowInfo = context.GetWindowInfo();
        var topLeft = context.CoordinateTransform.ScriptToWindow(condition.RegionX1, condition.RegionY1, windowInfo);
        var bottomRight = context.CoordinateTransform.ScriptToWindow(condition.RegionX2, condition.RegionY2, windowInfo);

        var x = Math.Clamp(Math.Min(topLeft.X, bottomRight.X), 0, frame.Width - 1);
        var y = Math.Clamp(Math.Min(topLeft.Y, bottomRight.Y), 0, frame.Height - 1);
        var right = Math.Clamp(Math.Max(topLeft.X, bottomRight.X), x + 1, frame.Width);
        var bottom = Math.Clamp(Math.Max(topLeft.Y, bottomRight.Y), y + 1, frame.Height);

        return new Rect(x, y, right - x, bottom - y);
    }
}
