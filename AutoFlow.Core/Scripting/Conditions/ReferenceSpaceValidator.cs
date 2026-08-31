namespace AutoFlow.Core.Scripting.Conditions;

/// <summary>
/// Outcome of checking a shared condition's reference space against the script using it.
/// </summary>
public enum ReferenceSpaceStatus
{
    /// <summary>Reference space matches, or the condition has no region so it does not matter.</summary>
    Ok,

    /// <summary>
    /// The condition has a region but declares no reference space. Coordinates cannot be
    /// verified; the caller must decide whether to trust them.
    /// </summary>
    Unspecified,

    /// <summary>The condition's region belongs to a different reference space. Do not match.</summary>
    Mismatch
}

public sealed record ReferenceSpaceCheck(ReferenceSpaceStatus Status, string? Message)
{
    public bool IsOk => Status == ReferenceSpaceStatus.Ok;
}

/// <summary>
/// Verifies that a shared <see cref="VisualConditionSpec"/>'s region coordinates belong to
/// the reference space of the script referencing it (Spec FR-10).
///
/// No automatic scaling is performed: OpenCV template matching is sensitive to scale, so a
/// template authored at one window size cannot be reliably matched at another. Reusing a
/// condition across reference spaces is not technically sound, so the only correct response
/// to a mismatch is to report it rather than silently match at wrong coordinates.
/// </summary>
public static class ReferenceSpaceValidator
{
    public static ReferenceSpaceCheck Check(
        VisualConditionSpec condition,
        int scriptOuterWidth,
        int scriptOuterHeight)
    {
        ArgumentNullException.ThrowIfNull(condition);

        // Whole-frame conditions carry no coordinates, so there is nothing to misinterpret.
        if (!condition.HasRegion)
            return new ReferenceSpaceCheck(ReferenceSpaceStatus.Ok, null);

        if (condition.ReferenceOuterWidth <= 0 || condition.ReferenceOuterHeight <= 0)
        {
            return new ReferenceSpaceCheck(
                ReferenceSpaceStatus.Unspecified,
                $"条件 '{Describe(condition)}' 定义了匹配区域 " +
                $"({condition.RegionX1},{condition.RegionY1})-({condition.RegionX2},{condition.RegionY2})，" +
                $"但未声明参照空间 (ReferenceOuterWidth/Height)。无法确认这组坐标是否适用于当前脚本 " +
                $"({scriptOuterWidth}x{scriptOuterHeight})。");
        }

        if (condition.ReferenceOuterWidth == scriptOuterWidth &&
            condition.ReferenceOuterHeight == scriptOuterHeight)
        {
            return new ReferenceSpaceCheck(ReferenceSpaceStatus.Ok, null);
        }

        return new ReferenceSpaceCheck(
            ReferenceSpaceStatus.Mismatch,
            $"条件 '{Describe(condition)}' 的区域坐标属于参照空间 " +
            $"{condition.ReferenceOuterWidth}x{condition.ReferenceOuterHeight}，" +
            $"而引用它的脚本是 {scriptOuterWidth}x{scriptOuterHeight}。" +
            $"模板匹配对缩放敏感，不做自动换算；请为该分辨率单独准备条件，或改用整帧匹配。");
    }

    private static string Describe(VisualConditionSpec condition)
        => !string.IsNullOrWhiteSpace(condition.Id) ? condition.Id
            : !string.IsNullOrWhiteSpace(condition.Name) ? condition.Name
            : condition.TemplatePath ?? "(未命名)";
}
