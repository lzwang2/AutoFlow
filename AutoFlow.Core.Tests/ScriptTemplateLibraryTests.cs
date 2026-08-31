using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Instructions;
using Newtonsoft.Json;
using Xunit;

namespace AutoFlow.Core.Tests;

public class ScriptTemplateLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "AutoFlowTests_" + Guid.NewGuid().ToString("N"));

    private const string Game = "TestGame";

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static ScriptDocument SampleDocument() => new()
    {
        Metadata = new ScriptMetadata { Description = "样例", Tags = { "测试" } },
        OuterWidth = 750,
        OuterHeight = 1040,
        Instructions =
        {
            new Instruction { CommandType = CommandType.Click, PositionX = 10, PositionY = 20 },
            new Instruction { CommandType = CommandType.WaitTime, WaitTimeMilliseconds = 500 }
        }
    };

    [Fact]
    public void SaveWritesUnderScriptsAndReturnsPath()
    {
        var library = new ScriptTemplateLibrary(_root);

        var path = library.Save(SampleDocument(), Game, "daily");

        Assert.True(File.Exists(path));
        Assert.Equal(
            ScriptTemplateLibrary.DirectoryFor(_root, Game),
            Path.GetDirectoryName(path));
        Assert.Equal("daily.json", Path.GetFileName(path));
    }

    [Fact]
    public void RoundTripsDocument()
    {
        var library = new ScriptTemplateLibrary(_root);
        var path = library.Save(SampleDocument(), Game, "daily");

        var loaded = library.Load(path);

        Assert.Equal(750, loaded.OuterWidth);
        Assert.Equal(1040, loaded.OuterHeight);
        Assert.Equal(2, loaded.Instructions.Count);
        Assert.Equal(CommandType.Click, loaded.Instructions[0].CommandType);
        Assert.Equal(500, loaded.Instructions[1].WaitTimeMilliseconds);
    }

    [Fact]
    public void LoadReturnsFreshInstanceEachCall()
    {
        // 导入模板后编辑，不能写回模板文件。
        var library = new ScriptTemplateLibrary(_root);
        var path = library.Save(SampleDocument(), Game, "daily");

        var first = library.Load(path);
        first.Instructions.Clear();
        var second = library.Load(path);

        Assert.Equal(2, second.Instructions.Count);
    }

    [Fact]
    public void ListReportsMetadataAndInstructionCount()
    {
        var library = new ScriptTemplateLibrary(_root);
        library.Save(SampleDocument(), Game, "daily");

        var entry = Assert.Single(library.List(Game, out var warnings));

        Assert.Empty(warnings);
        Assert.Equal(TemplateKind.LinearScript, entry.Kind);
        Assert.Equal("daily", entry.Name);
        Assert.Equal("样例", entry.Description);
        Assert.Equal("测试", entry.Category);
        Assert.Equal(2, entry.ItemCount);
    }

    [Fact]
    public void ListSkipsRuleScriptDroppedIntoScriptsDirectory()
    {
        // "*.json" 通配也会命中 "*.rules.json"，必须按类型再筛一次。
        var dir = ScriptTemplateLibrary.DirectoryFor(_root, Game);
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, "stray.rules.json"),
            JsonConvert.SerializeObject(new { Schema = "autoflow/rule-script", Rules = Array.Empty<object>() }));

        Assert.Empty(new ScriptTemplateLibrary(_root).List(Game, out _));
    }

    [Fact]
    public void ListReportsBrokenFileWithoutThrowing()
    {
        var library = new ScriptTemplateLibrary(_root);
        library.Save(SampleDocument(), Game, "good");

        var dir = ScriptTemplateLibrary.DirectoryFor(_root, Game);
        File.WriteAllText(Path.Combine(dir, "broken.json"), "{ not json");

        var entries = library.List(Game, out var warnings);

        Assert.Equal("good", Assert.Single(entries).Name);
        Assert.Contains("broken.json", Assert.Single(warnings));
    }

    [Fact]
    public void ListOnMissingDirectoryIsEmpty()
    {
        var entries = new ScriptTemplateLibrary(_root).List(Game, out var warnings);

        Assert.Empty(entries);
        Assert.Empty(warnings);
    }

    [Fact]
    public void SaveRejectsBlankFileName()
    {
        var library = new ScriptTemplateLibrary(_root);
        Assert.Throws<ArgumentException>(() => library.Save(SampleDocument(), Game, "  "));
    }

    [Fact]
    public void SaveSanitizesFileName()
    {
        var library = new ScriptTemplateLibrary(_root);

        var path = library.Save(SampleDocument(), Game, "a:b*c");

        Assert.True(File.Exists(path));
        Assert.DoesNotContain(':', Path.GetFileName(path));
        Assert.DoesNotContain('*', Path.GetFileName(path));
    }
}
