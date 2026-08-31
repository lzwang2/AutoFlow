using AutoFlow.Core.Scripting;
using Xunit;

namespace AutoFlow.Core.Tests;

public class TemplateLayoutTests
{
    [Theory]
    [InlineData(TemplateKind.Rule, "Rules", ".rules.json")]
    [InlineData(TemplateKind.Condition, "Conditions", ".condition.json")]
    [InlineData(TemplateKind.ActionBlock, "Actions", ".actionblock.json")]
    [InlineData(TemplateKind.LinearScript, "Scripts", ".json")]
    public void EveryKindHasDirectoryAndExtension(TemplateKind kind, string directory, string extension)
    {
        Assert.Equal(directory, TemplateLayout.SubDirectoryNameFor(kind));
        Assert.Equal(extension, TemplateLayout.ExtensionFor(kind));
    }

    [Fact]
    public void AllEnumMembersAreMapped()
    {
        // 新增 TemplateKind 却忘了补映射，这里立刻炸，而不是运行时 switch 落到 default。
        foreach (var kind in Enum.GetValues<TemplateKind>())
        {
            Assert.False(string.IsNullOrEmpty(TemplateLayout.SubDirectoryNameFor(kind)));
            Assert.False(string.IsNullOrEmpty(TemplateLayout.ExtensionFor(kind)));
        }
    }

    [Fact]
    public void RuleFileIsNotMisclassifiedAsLinearScript()
    {
        // ".rules.json" 也以 ".json" 结尾，顺序错了就会被当成线性脚本。
        var path = Path.Combine("root", "LastCloudia", "Rules", "unable_to_join.rules.json");
        Assert.Equal(TemplateKind.Rule, TemplateLayout.ClassifyFile(path));
    }

    [Fact]
    public void BareJsonCountsAsLinearScriptOnlyInsideScriptsDirectory()
    {
        var inScripts = Path.Combine("root", "LastCloudia", "Scripts", "daily.json");
        var elsewhere = Path.Combine("root", "LastCloudia", "daily.json");

        Assert.Equal(TemplateKind.LinearScript, TemplateLayout.ClassifyFile(inScripts));
        Assert.Null(TemplateLayout.ClassifyFile(elsewhere));
    }

    [Theory]
    [InlineData("x.condition.json", TemplateKind.Condition)]
    [InlineData("x.actionblock.json", TemplateKind.ActionBlock)]
    [InlineData("x.png", null)]
    [InlineData("README.md", null)]
    public void ClassifiesByExtension(string fileName, TemplateKind? expected)
    {
        var path = Path.Combine("root", "LastCloudia", "Conditions", fileName);
        Assert.Equal(expected, TemplateLayout.ClassifyFile(path));
    }

    [Fact]
    public void GameOfReadsFirstSegmentUnderRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "afroot");
        var file = Path.Combine(root, "LastCloudia", "Rules", "a.rules.json");

        Assert.Equal("LastCloudia", TemplateLayout.GameOf(root, file));
    }

    [Fact]
    public void GameOfReturnsNullForFileDirectlyInRoot()
    {
        // 迁移前有文件直接躺在根目录，这种情况没有 game 段，不能瞎猜一个。
        var root = Path.Combine(Path.GetTempPath(), "afroot");
        Assert.Null(TemplateLayout.GameOf(root, Path.Combine(root, "loose.rules.json")));
    }

    [Fact]
    public void GameOfReturnsNullForFileOutsideRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "afroot");
        var outside = Path.Combine(Path.GetTempPath(), "elsewhere", "a.rules.json");

        Assert.Null(TemplateLayout.GameOf(root, outside));
    }
}
