using AutoFlow.Core.Scripting.Conditions;
using Xunit;

namespace AutoFlow.Core.Tests;

/// <summary>
/// Region-resolution and coordinate-translation tests. Actual template matching needs a live
/// capture source and a real window, so it is covered by the manual runtime-equivalence pass
/// (AC-06) rather than here.
/// </summary>
public class VisualConditionEvaluatorTests
{
    [Fact]
    public void InlineWaitImageConditionMatchesWholeFrame()
    {
        // 保证行为等价：内联 WaitImage 从来没有区域，改造后必须仍是整帧匹配。
        var inline = new VisualConditionSpec
        {
            Id = "(内联)",
            TemplatePath = "x.png",
            MatchThreshold = 0.85,
            MeanDiffThreshold = 30.0
        };

        Assert.False(inline.HasRegion);
    }

    [Fact]
    public void ConditionMatchReportsCentre()
    {
        var match = new ConditionMatch(100, 200, 40, 20, 0.93);

        Assert.Equal(120, match.CenterX);
        Assert.Equal(210, match.CenterY);
    }

    [Theory]
    [InlineData(285, 320, 440, 340, true)]
    [InlineData(-1, -1, -1, -1, false)]
    [InlineData(0, 0, 100, 100, true)]
    [InlineData(10, 10, 10, 10, false)]  // 零宽零高不是区域
    [InlineData(10, 10, 5, 20, false)]   // x2 < x1
    public void RegionDetectionMatchesRuleSemantics(int x1, int y1, int x2, int y2, bool expected)
    {
        var condition = new VisualConditionSpec
        {
            RegionX1 = x1, RegionY1 = y1, RegionX2 = x2, RegionY2 = y2
        };

        Assert.Equal(expected, condition.HasRegion);
    }
}
