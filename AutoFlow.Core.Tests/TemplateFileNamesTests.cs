using AutoFlow.Core.Scripting;
using Xunit;

namespace AutoFlow.Core.Tests;

public class TemplateFileNamesTests
{
    [Fact]
    public void ConditionIdBecomesFileNameWithDotsReplaced()
    {
        // Id 里的 '.' 是命名空间分隔符，直接留着会和扩展名链混起来读。
        Assert.Equal(
            "LastCloudia_UnableToJoin.condition.json",
            TemplateFileNames.ForConditionId("LastCloudia.UnableToJoin"));
    }

    [Fact]
    public void ActionBlockIdBecomesFileNameWithDotsReplaced()
    {
        Assert.Equal(
            "LastCloudia_Retry.actionblock.json",
            TemplateFileNames.ForActionBlockId("LastCloudia.Retry"));
    }

    [Theory]
    [InlineData("a:b", "a_b")]
    [InlineData("a/b", "a_b")]
    [InlineData("plain", "plain")]
    public void SanitizeStripsInvalidCharacters(string input, string expected)
        => Assert.Equal(expected, TemplateFileNames.Sanitize(input));

    [Fact]
    public void SanitizeRejectsBlank()
        => Assert.Throws<ArgumentException>(() => TemplateFileNames.Sanitize("   "));

    [Fact]
    public void SanitizeRejectsAllInvalidCharacters()
    {
        // 全是非法字符时，静默返回空串会写出没有主名的文件。
        Assert.Throws<ArgumentException>(() => TemplateFileNames.Sanitize(":*?"));
    }
}
