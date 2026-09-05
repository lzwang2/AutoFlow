using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Instructions;
using AutoFlow.Core.Scripting.Rules;
using Newtonsoft.Json;
using Xunit;

namespace AutoFlow.Core.Tests;

/// <summary>
/// Covers <see cref="RuleTemplateService.OverwriteTemplate"/>: the write-back path that makes the
/// rule editor behave like the condition editor — a rule imported from the library is saved back
/// to the file it came from, in place.
///
/// <para>Overwriting is the risky part. The save-as path derives a file name from the condition's
/// image and hardcodes 750x1040, so reusing it for write-back would rename the file whenever the
/// rule was renamed and reset the reference size of any template authored at another resolution.
/// Both are pinned here.</para>
/// </summary>
public class RuleTemplateWriteBackTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "AutoFlowTests_" + Guid.NewGuid().ToString("N"));

    private const string Game = "TestGame";

    private readonly RuleTemplateService _service;

    public RuleTemplateWriteBackTests() => _service = new RuleTemplateService(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static Rule SampleRule(string name = "示例规则", string? conditionRef = "TestGame.Alpha")
        => new()
        {
            Name = name,
            Enabled = true,
            ConditionRef = conditionRef,
            Actions = new List<Instruction>
            {
                new() { CommandType = CommandType.Click, PositionX = 10, PositionY = 20 }
            }
        };

    private VisualConditionSpec SampleCondition()
    {
        var imagesDir = TemplateLayout.ImagesDirectoryFor(_root, Game);
        Directory.CreateDirectory(imagesDir);
        File.WriteAllBytes(Path.Combine(imagesDir, "alpha.png"), new byte[] { 1, 2, 3 });

        return new VisualConditionSpec
        {
            Id = "TestGame.Alpha",
            Name = "Alpha",
            TemplatePath = $"{Game}/images/alpha.png",
            RegionX1 = 5,
            RegionY1 = 6,
            RegionX2 = 50,
            RegionY2 = 60,
            ReferenceOuterWidth = 1280,
            ReferenceOuterHeight = 720
        };
    }

    private static RuleScript Read(string path)
        => JsonConvert.DeserializeObject<RuleScript>(File.ReadAllText(path))!;

    [Fact]
    public void OverwriteWritesToTheGivenPathInsteadOfDerivingANewName()
    {
        var condition = SampleCondition();
        var original = _service.SaveAsTemplate(
            SampleRule(), Game, "alpha", "原始名称", null, condition);

        // Renaming the rule must not strand the old file next to a new one: that is exactly what
        // the save-as path would do, because it derives the file name from the condition's image.
        var written = _service.OverwriteTemplate(
            SampleRule("改过的名字"), Game, original, "改过的名字", null, condition);

        Assert.Equal(original, written);

        var rulesDir = TemplateLayout.DirectoryFor(_root, Game, TemplateKind.Rule);
        Assert.Single(Directory.GetFiles(rulesDir, "*" + TemplateLayout.RuleExtension));
        Assert.Equal("改过的名字", Read(original).Rules[0].Name);
    }

    [Fact]
    public void OverwritePreservesTheTemplatesOwnOuterSize()
    {
        // A template authored at 1280x720. The save-as path hardcodes 750x1040, so if write-back
        // reused it, every region coordinate in this file would lose its reference space.
        var rulesDir = TemplateLayout.DirectoryFor(_root, Game, TemplateKind.Rule);
        Directory.CreateDirectory(rulesDir);
        var path = Path.Combine(rulesDir, "wide" + TemplateLayout.RuleExtension);

        File.WriteAllText(path, JsonConvert.SerializeObject(new RuleScript
        {
            Metadata = new RuleScriptMetadata { Name = "宽屏模板", Author = "原作者", CreatedAt = "2020-01-01" },
            Rules = new List<Rule> { SampleRule() },
            IdleTickMs = 250,
            OuterWidth = 1280,
            OuterHeight = 720
        }));

        _service.OverwriteTemplate(SampleRule("改过"), Game, path, "改过", null, SampleCondition());

        var reloaded = Read(path);
        Assert.Equal(1280, reloaded.OuterWidth);
        Assert.Equal(720, reloaded.OuterHeight);
        Assert.Equal(250, reloaded.IdleTickMs);
    }

    [Fact]
    public void OverwriteKeepsTheOriginalAuthorAndCreationDate()
    {
        var rulesDir = TemplateLayout.DirectoryFor(_root, Game, TemplateKind.Rule);
        Directory.CreateDirectory(rulesDir);
        var path = Path.Combine(rulesDir, "owned" + TemplateLayout.RuleExtension);

        File.WriteAllText(path, JsonConvert.SerializeObject(new RuleScript
        {
            Metadata = new RuleScriptMetadata { Name = "原名", Author = "原作者", CreatedAt = "2019-05-06" },
            Rules = new List<Rule> { SampleRule() }
        }));

        _service.OverwriteTemplate(SampleRule(), Game, path, "新名", "新描述", SampleCondition());

        var reloaded = Read(path);
        Assert.Equal("原作者", reloaded.Metadata!.Author);
        Assert.Equal("2019-05-06", reloaded.Metadata.CreatedAt);
        Assert.Equal("新名", reloaded.Metadata.Name);
        Assert.Equal("新描述", reloaded.Metadata.Description);
    }

    [Fact]
    public void OverwriteExportsTheReferencedConditionAlongsideTheRule()
    {
        var condition = SampleCondition();
        var rulesDir = TemplateLayout.DirectoryFor(_root, Game, TemplateKind.Rule);
        Directory.CreateDirectory(rulesDir);
        var path = Path.Combine(rulesDir, "withcond" + TemplateLayout.RuleExtension);
        File.WriteAllText(path, JsonConvert.SerializeObject(new RuleScript
        {
            Rules = new List<Rule> { SampleRule() }
        }));

        _service.OverwriteTemplate(SampleRule(), Game, path, "带条件", null, condition);

        // Without this the template carries a ConditionRef that resolves to nothing on another
        // machine — the same reason SaveAsTemplate exports it.
        var library = new ConditionLibrary(_root);
        library.LoadGame(Game);
        Assert.True(library.TryGet("TestGame.Alpha", out var exported));
        Assert.Equal(50, exported!.RegionX2);
    }

    [Fact]
    public void OverwriteStillWritesWhenTheExistingFileIsCorrupt()
    {
        // A save that refused to run because the target was unreadable would leave the user's
        // edit with nowhere to go. It falls back to defaults instead.
        var rulesDir = TemplateLayout.DirectoryFor(_root, Game, TemplateKind.Rule);
        Directory.CreateDirectory(rulesDir);
        var path = Path.Combine(rulesDir, "broken" + TemplateLayout.RuleExtension);
        File.WriteAllText(path, "{ this is not json");

        _service.OverwriteTemplate(SampleRule(), Game, path, "修好了", null, SampleCondition());

        var reloaded = Read(path);
        Assert.Equal("修好了", reloaded.Metadata!.Name);
        Assert.Equal(750, reloaded.OuterWidth);
        Assert.Equal(500, reloaded.IdleTickMs);
    }

    [Fact]
    public void OverwriteCreatesTheFileWhenItIsMissing()
    {
        // The editor remembers a source path; the file can still be deleted outside the app
        // between import and save. Recreating it is better than failing the save.
        var rulesDir = TemplateLayout.DirectoryFor(_root, Game, TemplateKind.Rule);
        var path = Path.Combine(rulesDir, "gone" + TemplateLayout.RuleExtension);

        _service.OverwriteTemplate(SampleRule(), Game, path, "回来了", null, SampleCondition());

        Assert.True(File.Exists(path));
        Assert.Equal("回来了", Read(path).Metadata!.Name);
    }

    [Fact]
    public void OverwrittenTemplateStaysImportableWithItsRuleIntact()
    {
        // The round trip that matters: write back, then re-import. A file that saved fine but no
        // longer loads would only surface the next time the user opened it.
        var condition = SampleCondition();
        var original = _service.SaveAsTemplate(SampleRule(), Game, "alpha", "原始", null, condition);

        _service.OverwriteTemplate(
            SampleRule("改过的名字"), Game, original, "改过的名字", null, condition);

        var rules = _service.LoadRulesFromTemplate(original);
        Assert.Single(rules);
        Assert.Equal("改过的名字", rules[0].Name);
        Assert.Equal("TestGame.Alpha", rules[0].ConditionRef);
        Assert.True(rules[0].UsesSharedCondition);
    }

    [Fact]
    public void OverwriteRejectsAnEmptyPathRatherThanGuessingOne()
    {
        Assert.Throws<ArgumentException>(() =>
            _service.OverwriteTemplate(SampleRule(), Game, "", "名字", null, SampleCondition()));
    }

    [Fact]
    public void OverwriteHandlesACatchAllRuleWithNoCondition()
    {
        var rulesDir = TemplateLayout.DirectoryFor(_root, Game, TemplateKind.Rule);
        Directory.CreateDirectory(rulesDir);
        var path = Path.Combine(rulesDir, "catchall" + TemplateLayout.RuleExtension);
        File.WriteAllText(path, JsonConvert.SerializeObject(new RuleScript
        {
            Rules = new List<Rule> { SampleRule("兜底", conditionRef: null) }
        }));

        _service.OverwriteTemplate(
            SampleRule("兜底", conditionRef: null), Game, path, "兜底", null, condition: null);

        var reloaded = Read(path);
        Assert.False(reloaded.Rules[0].UsesSharedCondition);
    }
}
