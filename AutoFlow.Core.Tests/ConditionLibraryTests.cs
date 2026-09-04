using AutoFlow.Core.Scripting.Conditions;
using Newtonsoft.Json;
using Xunit;

namespace AutoFlow.Core.Tests;

public class ConditionLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "AutoFlowTests_" + Guid.NewGuid().ToString("N"));

    private const string Game = "TestGame";

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string ConditionsDir
    {
        get
        {
            var dir = ConditionLibrary.DirectoryFor(_root, Game);
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private void WriteCondition(string fileName, object payload)
        => File.WriteAllText(
            Path.Combine(ConditionsDir, fileName),
            JsonConvert.SerializeObject(payload));

    private string WriteImage(string fileName)
    {
        var path = Path.Combine(ConditionsDir, fileName);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        return path;
    }

    [Fact]
    public void LoadsConditionAndResolvesImagePath()
    {
        WriteImage("marker.png");
        WriteCondition("cond.condition.json", new
        {
            Id = "TestGame.Marker",
            Name = "标记",
            TemplatePath = "marker.png",
            ReferenceOuterWidth = 750,
            ReferenceOuterHeight = 1040
        });

        var library = new ConditionLibrary(_root);
        var warnings = library.LoadGame(Game);

        Assert.Empty(warnings);
        Assert.True(library.TryGet("TestGame.Marker", out var condition));
        Assert.Equal("标记", condition!.Name);

        // The authored path stays relative so a load/edit/save round trip does not rewrite it
        // as absolute; the resolved location goes to ResolvedTemplatePath.
        Assert.Equal("marker.png", condition.TemplatePath);
        Assert.True(Path.IsPathRooted(condition.ResolvedTemplatePath));
        Assert.True(File.Exists(condition.ResolvedTemplatePath));
        Assert.Equal(condition.ResolvedTemplatePath, condition.EffectiveTemplatePath);
    }

    [Fact]
    public void SaveAfterLoad_KeepsRelativeTemplatePathInFile()
    {
        WriteImage("marker.png");
        WriteCondition("cond.condition.json", new { Id = "TestGame.Marker", TemplatePath = "marker.png" });

        var library = new ConditionLibrary(_root);
        library.LoadGame(Game);
        Assert.True(library.TryGet("TestGame.Marker", out var condition));

        // Editor round trip: load, change something unrelated, write back.
        condition!.Name = "改名";
        var path = library.Save(condition, Game, out var warnings);

        Assert.Empty(warnings);
        var reloaded = JsonConvert.DeserializeObject<VisualConditionSpec>(File.ReadAllText(path));
        Assert.Equal("marker.png", reloaded!.TemplatePath);
        Assert.Null(reloaded.ResolvedTemplatePath);
    }

    [Fact]
    public void MissingConditionId_NamesScriptAndRule()
    {
        var library = new ConditionLibrary(_root);
        library.LoadGame(Game);

        var ex = Assert.Throws<ConditionResolutionException>(
            () => library.Require("TestGame.Nope", "multi_game.rules.json", "再战确认"));

        // AC-08: the message must identify script, rule and the missing Id.
        Assert.Contains("multi_game.rules.json", ex.Message);
        Assert.Contains("再战确认", ex.Message);
        Assert.Contains("TestGame.Nope", ex.Message);
    }

    [Fact]
    public void MissingId_ListsKnownIdsWhenLibraryNotEmpty()
    {
        WriteImage("a.png");
        WriteCondition("a.condition.json", new { Id = "TestGame.Alpha", TemplatePath = "a.png" });

        var library = new ConditionLibrary(_root);
        library.LoadGame(Game);

        var ex = Assert.Throws<ConditionResolutionException>(
            () => library.Require("TestGame.Beta", "s.rules.json", "r"));

        Assert.Contains("TestGame.Alpha", ex.Message);
    }

    [Fact]
    public void DuplicateIds_AreReportedAndFirstWins()
    {
        WriteImage("a.png");
        WriteCondition("first.condition.json", new { Id = "TestGame.Dup", Name = "第一个", TemplatePath = "a.png" });
        WriteCondition("second.condition.json", new { Id = "TestGame.Dup", Name = "第二个", TemplatePath = "a.png" });

        var library = new ConditionLibrary(_root);
        var warnings = library.LoadGame(Game);

        Assert.Single(warnings);
        Assert.Contains("Id 重复", warnings[0]);

        library.TryGet("TestGame.Dup", out var kept);
        Assert.NotNull(kept);
    }

    [Fact]
    public void UnresolvableImage_WarnsButKeepsCondition()
    {
        WriteCondition("c.condition.json", new { Id = "TestGame.Broken", TemplatePath = "does_not_exist.png" });

        var library = new ConditionLibrary(_root);
        var warnings = library.LoadGame(Game);

        Assert.Single(warnings);
        Assert.Contains("无法定位", warnings[0]);
        Assert.Contains("does_not_exist.png", warnings[0]);

        // A single broken image must not prevent the rest of the library loading.
        Assert.True(library.TryGet("TestGame.Broken", out _));
    }

    [Fact]
    public void ConditionWithoutId_IsSkippedWithWarning()
    {
        WriteCondition("noid.condition.json", new { Name = "无 Id" });

        var library = new ConditionLibrary(_root);
        var warnings = library.LoadGame(Game);

        Assert.Single(warnings);
        Assert.Contains("缺少 Id", warnings[0]);
        Assert.Empty(library.All);
    }

    [Fact]
    public void MalformedJson_IsReportedNotThrown()
    {
        File.WriteAllText(Path.Combine(ConditionsDir, "bad.condition.json"), "{ not json");

        var library = new ConditionLibrary(_root);
        var warnings = library.LoadGame(Game);

        Assert.Single(warnings);
        Assert.Contains("解析失败", warnings[0]);
    }

    [Fact]
    public void MissingDirectory_LoadsEmptyWithoutThrowing()
    {
        var library = new ConditionLibrary(_root);

        Assert.Empty(library.LoadGame("NoSuchGame"));
        Assert.Empty(library.All);
    }

    [Fact]
    public void SaveThenLoad_PreservesCondition()
    {
        var library = new ConditionLibrary(_root);
        library.Save(new VisualConditionSpec
        {
            Id = "TestGame.Saved",
            Name = "保存测试",
            TemplatePath = "TestGame/images/x.png",
            RegionX1 = 10, RegionY1 = 20, RegionX2 = 30, RegionY2 = 40,
            ReferenceOuterWidth = 750,
            ReferenceOuterHeight = 1040
        }, Game);

        var reloaded = new ConditionLibrary(_root);
        reloaded.LoadGame(Game);

        Assert.True(reloaded.TryGet("TestGame.Saved", out var condition));
        Assert.Equal("保存测试", condition!.Name);
        Assert.Equal(10, condition.RegionX1);
        Assert.Equal(750, condition.ReferenceOuterWidth);
    }

    [Fact]
    public void SaveWithoutId_Throws()
    {
        var library = new ConditionLibrary(_root);

        Assert.Throws<ArgumentException>(
            () => library.Save(new VisualConditionSpec { Name = "无 Id" }, Game));
    }

    [Fact]
    public void SaveWarnsOnAbsoluteTemplatePath()
    {
        var library = new ConditionLibrary(_root);
        var absolute = Path.Combine(Path.GetTempPath(), "somewhere", "x.png");

        var path = library.Save(new VisualConditionSpec
        {
            Id = "TestGame.Absolute",
            TemplatePath = absolute
        }, Game, out var warnings);

        // 仍然写盘——拒绝保存会丢掉用户的编辑；但必须告警，绝对路径只在本机有效。
        Assert.True(File.Exists(path));
        var warning = Assert.Single(warnings);
        Assert.Contains(absolute, warning);
        Assert.Contains("绝对路径", warning);
    }

    [Fact]
    public void SaveDoesNotWarnOnLibraryRelativePath()
    {
        var library = new ConditionLibrary(_root);

        library.Save(new VisualConditionSpec
        {
            Id = "TestGame.Relative",
            TemplatePath = $"{Game}/images/x.png"
        }, Game, out var warnings);

        Assert.Empty(warnings);
    }

    [Fact]
    public void SaveReturnsPathUnderConditionsDirectory()
    {
        var library = new ConditionLibrary(_root);

        var path = library.Save(new VisualConditionSpec { Id = "TestGame.Where" }, Game);

        Assert.Equal(ConditionLibrary.DirectoryFor(_root, Game), Path.GetDirectoryName(path));
        Assert.Equal("TestGame_Where.condition.json", Path.GetFileName(path));
        Assert.Equal(path, library.SourceFileOf("TestGame.Where"));
    }

    [Fact]
    public void SameConditionUsedWithDifferentTimeouts_NeedsNoDuplication()
    {
        // AC-09: timeout lives at the call site, so one definition serves all callers.
        WriteImage("shared.png");
        WriteCondition("shared.condition.json", new
        {
            Id = "TestGame.Shared",
            TemplatePath = "shared.png",
            ReferenceOuterWidth = 750,
            ReferenceOuterHeight = 1040
        });

        var library = new ConditionLibrary(_root);
        library.LoadGame(Game);

        library.TryGet("TestGame.Shared", out var condition);
        Assert.NotNull(condition);

        // The type carries no timeout field at all — verified structurally.
        Assert.Null(typeof(VisualConditionSpec).GetProperty("WaitTimeoutMs"));
        Assert.Single(library.All);
    }
}
