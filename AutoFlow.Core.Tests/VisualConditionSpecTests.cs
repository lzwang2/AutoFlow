using AutoFlow.Core.Scripting.Conditions;
using Newtonsoft.Json;
using Xunit;

namespace AutoFlow.Core.Tests;

public class VisualConditionSpecTests
{
    [Fact]
    public void RoundTripsThroughJson()
    {
        var original = new VisualConditionSpec
        {
            Id = "LastCloudia.UnableToJoin",
            Name = "无法加入房间",
            Description = "检测无法创建或加入房间弹窗",
            TemplatePath = "LastCloudia/images/unable_to_join.png",
            RegionX1 = 285,
            RegionY1 = 320,
            RegionX2 = 440,
            RegionY2 = 340,
            MatchThreshold = 0.9,
            MeanDiffThreshold = 25.0,
            ReferenceOuterWidth = 750,
            ReferenceOuterHeight = 1040,
            Tags = { "弹窗", "多人" },
            Category = "网络异常"
        };

        var restored = JsonConvert.DeserializeObject<VisualConditionSpec>(
            JsonConvert.SerializeObject(original));

        Assert.NotNull(restored);
        Assert.Equal(original.Id, restored!.Id);
        Assert.Equal(original.Name, restored.Name);
        Assert.Equal(original.TemplatePath, restored.TemplatePath);
        Assert.Equal(original.RegionX1, restored.RegionX1);
        Assert.Equal(original.RegionY2, restored.RegionY2);
        Assert.Equal(original.MatchThreshold, restored.MatchThreshold);
        Assert.Equal(original.MeanDiffThreshold, restored.MeanDiffThreshold);
        Assert.Equal(original.ReferenceOuterWidth, restored.ReferenceOuterWidth);
        Assert.Equal(original.ReferenceOuterHeight, restored.ReferenceOuterHeight);
        Assert.Equal(original.Tags, restored.Tags);
        Assert.Equal(original.Category, restored.Category);
    }

    [Fact]
    public void DefaultsMatchLegacyRuleDefaults()
    {
        // These are the v1 Rule defaults, asserted as literals because Rule no longer carries
        // the fields to compare against. A change here silently alters how every migrated
        // condition matches, which is exactly what this pins down.
        var condition = new VisualConditionSpec();

        Assert.Equal(0.85, condition.MatchThreshold);
        Assert.Equal(30.0, condition.MeanDiffThreshold);
        Assert.Equal(-1, condition.RegionX1);
        Assert.Equal(-1, condition.RegionY1);
        Assert.Equal(-1, condition.RegionX2);
        Assert.Equal(-1, condition.RegionY2);
        Assert.False(condition.HasRegion);
    }

    [Theory]
    // Same four-condition logic as Rule.HasRegion.
    [InlineData(-1, -1, -1, -1, false)]
    [InlineData(0, 0, 10, 10, true)]
    [InlineData(285, 320, 440, 340, true)]
    [InlineData(-1, 320, 440, 340, false)]   // negative origin
    [InlineData(285, -1, 440, 340, false)]
    [InlineData(285, 320, 285, 340, false)]  // zero width
    [InlineData(285, 320, 440, 320, false)]  // zero height
    [InlineData(440, 320, 285, 340, false)]  // inverted
    public void HasRegion_MirrorsRuleSemantics(int x1, int y1, int x2, int y2, bool expected)
    {
        // The four-condition test v1's Rule.HasRegion used: both corners non-negative and the
        // region non-empty. Kept verbatim so migrated regions match where they used to.
        var condition = new VisualConditionSpec { RegionX1 = x1, RegionY1 = y1, RegionX2 = x2, RegionY2 = y2 };

        Assert.Equal(expected, condition.HasRegion);
    }
}

public class ReferenceSpaceValidatorTests
{
    private static VisualConditionSpec WithRegion(int refW, int refH) => new()
    {
        Id = "Test.Condition",
        RegionX1 = 285, RegionY1 = 320, RegionX2 = 440, RegionY2 = 340,
        ReferenceOuterWidth = refW,
        ReferenceOuterHeight = refH
    };

    [Fact]
    public void MatchingReferenceSpace_IsOk()
    {
        Assert.True(ReferenceSpaceValidator.Check(WithRegion(750, 1040), 750, 1040).IsOk);
    }

    [Fact]
    public void MismatchedReferenceSpace_IsReported()
    {
        var result = ReferenceSpaceValidator.Check(WithRegion(750, 1040), 1920, 1080);

        Assert.Equal(ReferenceSpaceStatus.Mismatch, result.Status);
        Assert.False(result.IsOk);

        // The message must name both spaces so the user can act on it (AC-07).
        Assert.Contains("750x1040", result.Message);
        Assert.Contains("1920x1080", result.Message);
        Assert.Contains("Test.Condition", result.Message);
    }

    [Fact]
    public void WholeFrameCondition_IgnoresReferenceSpace()
    {
        // No region means no coordinates to misinterpret, so any script size is fine.
        var condition = new VisualConditionSpec { Id = "Test.WholeFrame" };

        Assert.True(ReferenceSpaceValidator.Check(condition, 1920, 1080).IsOk);
        Assert.True(ReferenceSpaceValidator.Check(condition, 750, 1040).IsOk);
    }

    [Fact]
    public void RegionWithoutReferenceSpace_IsUnspecified()
    {
        var result = ReferenceSpaceValidator.Check(WithRegion(0, 0), 750, 1040);

        Assert.Equal(ReferenceSpaceStatus.Unspecified, result.Status);
        Assert.Contains("未声明参照空间", result.Message);
    }
}
