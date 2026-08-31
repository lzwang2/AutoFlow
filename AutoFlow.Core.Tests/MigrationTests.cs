using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Migration;
using Newtonsoft.Json.Linq;
using Xunit;

namespace AutoFlow.Core.Tests;

public class ConditionIdGeneratorTests
{
    [Theory]
    [InlineData("LastCloudia/images/unable_to_join.png", "LastCloudia.UnableToJoin")]
    [InlineData("LastCloudia/images/refight_denied.png", "LastCloudia.RefightDenied")]
    [InlineData("LastCloudia/images/server_connection_fail_multiplayer.png", "LastCloudia.ServerConnectionFailMultiplayer")]
    [InlineData("images/welcome.png", "LastCloudia.Welcome")]
    [InlineData("images/no_room.png", "LastCloudia.NoRoom")]
    public void GeneratesPascalCaseIdFromFileName(string templatePath, string expected)
        => Assert.Equal(expected, ConditionIdGenerator.FromTemplatePath("LastCloudia", templatePath));

    [Theory]
    [InlineData("LastCloudia/images/abandon_task_area.png", "LastCloudia.AbandonTask")]
    [InlineData("LastCloudia/images/network_error_area.png", "LastCloudia.NetworkError")]
    public void StripsAreaSuffixViaExplicitOverride(string templatePath, string expected)
    {
        // _area 是截图习惯不是语义，机械转换会得到误导性的 Id。
        Assert.Equal(expected, ConditionIdGenerator.FromTemplatePath("LastCloudia", templatePath));
    }

    [Fact]
    public void UnregisteredAreaImageThrowsRatherThanGuessing()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => ConditionIdGenerator.FromTemplatePath("LastCloudia", "images/something_new_area.png"));

        Assert.Contains("_area", ex.Message);
    }

    [Fact]
    public void BackslashPathsAreHandled()
        => Assert.Equal(
            "LastCloudia.UnableToJoin",
            ConditionIdGenerator.FromTemplatePath("LastCloudia", @"LastCloudia\images\unable_to_join.png"));
}

public class TemplatePathClassifierTests
{
    [Fact]
    public void ClassifiesLibraryRelative()
        => Assert.Equal(
            TemplatePathKind.LibraryRelative,
            TemplatePathClassifier.Classify("LastCloudia/images/x.png", "LastCloudia"));

    [Fact]
    public void ClassifiesFileRelative()
        => Assert.Equal(
            TemplatePathKind.FileRelative,
            TemplatePathClassifier.Classify("images/welcome.png", "LastCloudia"));

    [Fact]
    public void ClassifiesAbsolute()
        => Assert.Equal(
            TemplatePathKind.Absolute,
            TemplatePathClassifier.Classify(@"D:\build\RuleTemplates\LastCloudia\images\x.png", "LastCloudia"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ClassifiesEmptyAsNone(string? path)
        => Assert.Equal(TemplatePathKind.None, TemplatePathClassifier.Classify(path, "LastCloudia"));

    [Theory]
    [InlineData("x.png")]
    [InlineData("SomeOtherGame/images/x.png")]
    public void RefusesUnrecognisedShapes(string path)
        => Assert.Throws<InvalidOperationException>(
            () => TemplatePathClassifier.Classify(path, "LastCloudia"));

    [Theory]
    [InlineData("images/welcome.png", "LastCloudia/images/welcome.png")]
    [InlineData("LastCloudia/images/x.png", "LastCloudia/images/x.png")]
    [InlineData(@"D:\b\RuleTemplates\LastCloudia\images\x.png", "LastCloudia/images/x.png")]
    public void RewritesToLibraryRelative(string input, string expected)
        => Assert.Equal(expected, TemplatePathClassifier.ToLibraryRelative(input, "LastCloudia"));
}

public class RuleScriptMigratorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "af_mig_" + Guid.NewGuid().ToString("N"));

    public RuleScriptMigratorTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string WriteScript(string name, string json)
    {
        var dir = Path.Combine(_root, "LastCloudia");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, json);
        return path;
    }

    private static string LegacyScript(string ruleName, string templatePath, int x1 = 285, int y1 = 320, int x2 = 440, int y2 = 340)
        => $$"""
        {
          "Schema": "autoflow/rule-script",
          "FormatVersion": 1,
          "OuterWidth": 750,
          "OuterHeight": 1040,
          "Rules": [
            {
              "Name": "{{ruleName}}",
              "Enabled": true,
              "TemplatePath": "{{templatePath}}",
              "MatchThreshold": 0.85,
              "MeanDiffThreshold": 30.0,
              "RegionX1": {{x1}}, "RegionY1": {{y1}}, "RegionX2": {{x2}}, "RegionY2": {{y2}},
              "Actions": [ { "CommandType": "Click", "PositionX": 368, "PositionY": 645 } ]
            }
          ]
        }
        """;

    [Fact]
    public void RewritesRuleToConditionRefAndBumpsVersion()
    {
        var path = WriteScript("unable_to_join.rules.json",
            LegacyScript("无法加入房间", "LastCloudia/images/unable_to_join.png"));

        var report = new RuleScriptMigrator(_root).Migrate(new[] { path }, "LastCloudia");

        Assert.True(report.Success, report.Summarize());
        var root = JObject.Parse(File.ReadAllText(path));
        Assert.Equal(2, root.Value<int>("FormatVersion"));

        var rule = (JObject)root["Rules"]![0]!;
        Assert.Equal("LastCloudia.UnableToJoin", rule.Value<string>("ConditionRef"));
        Assert.Null(rule["TemplatePath"]);
        Assert.Null(rule["RegionX1"]);
        Assert.Null(rule["MatchThreshold"]);
        Assert.NotNull(rule["Actions"]);
        Assert.Equal("无法加入房间", rule.Value<string>("Name"));
    }

    [Fact]
    public void ConditionCapturesReferenceSpaceFromOwningScript()
    {
        var path = WriteScript("a.rules.json", LegacyScript("A", "LastCloudia/images/unable_to_join.png"));

        var migrator = new RuleScriptMigrator(_root);
        migrator.Migrate(new[] { path }, "LastCloudia");

        var condition = migrator.Conditions["LastCloudia.UnableToJoin"];
        Assert.Equal(750, condition.ReferenceOuterWidth);
        Assert.Equal(1040, condition.ReferenceOuterHeight);
        Assert.Equal(285, condition.RegionX1);
        Assert.Equal("LastCloudia/images/unable_to_join.png", condition.TemplatePath);
    }

    [Fact]
    public void IdenticalConditionsInDifferentScriptsMergeToOne()
    {
        // network_error.rules.json 与 test.rules.json 用的是同一张图、同一区域，这是去重的实际收益。
        var a = WriteScript("network_error.rules.json",
            LegacyScript("网络错误", "LastCloudia/images/network_error_area.png", 285, 430, 435, 455));
        var b = WriteScript("test.rules.json",
            LegacyScript("网络错误-发生了网络错误", "LastCloudia/images/network_error_area.png", 285, 430, 435, 455));

        var migrator = new RuleScriptMigrator(_root);
        var report = migrator.Migrate(new[] { a, b }, "LastCloudia");

        Assert.True(report.Success, report.Summarize());
        Assert.Equal(1, report.DuplicatesMerged);
        Assert.Single(migrator.Conditions);

        foreach (var path in new[] { a, b })
        {
            var rule = (JObject)JObject.Parse(File.ReadAllText(path))["Rules"]![0]!;
            Assert.Equal("LastCloudia.NetworkError", rule.Value<string>("ConditionRef"));
        }
    }

    [Fact]
    public void SameImageDifferentRegionIsAnIdClashAndStops()
    {
        // 同名 Id 但内容不同必须停下，否则两个不同的判定会互相覆盖。
        var a = WriteScript("a.rules.json",
            LegacyScript("A", "LastCloudia/images/unable_to_join.png", 285, 320, 440, 340));
        var b = WriteScript("b.rules.json",
            LegacyScript("B", "LastCloudia/images/unable_to_join.png", 100, 100, 200, 200));

        var report = new RuleScriptMigrator(_root).Migrate(new[] { a, b }, "LastCloudia");

        Assert.False(report.Success);
        Assert.Contains(report.Errors, e => e.Contains("Id 冲突"));
    }

    [Fact]
    public void CatchAllRuleKeepsNoConditionRef()
    {
        var path = WriteScript("multi.rules.json", """
        {
          "FormatVersion": 1, "OuterWidth": 750, "OuterHeight": 1040,
          "Rules": [
            { "Name": "其他情况", "Enabled": true,
              "Actions": [ { "CommandType": "CallSkill", "SkillName": "X" } ] }
          ]
        }
        """);

        var report = new RuleScriptMigrator(_root).Migrate(new[] { path }, "LastCloudia");

        Assert.True(report.Success, report.Summarize());
        Assert.Equal(1, report.CatchAllRules);

        var rule = (JObject)JObject.Parse(File.ReadAllText(path))["Rules"]![0]!;
        Assert.Null(rule["ConditionRef"]);
        Assert.Null(rule["TemplatePath"]);
    }

    [Fact]
    public void FileRelativeImageIsPromotedIntoLibrary()
    {
        // 脚本必须放在库外（模拟 TestScripts/），否则源和目标是同一个文件，压根不需要复制。
        var scriptDir = Path.Combine(_root, "TestScripts");
        Directory.CreateDirectory(Path.Combine(scriptDir, "images"));
        File.WriteAllBytes(Path.Combine(scriptDir, "images", "welcome.png"), new byte[] { 1, 2, 3 });

        var path = Path.Combine(scriptDir, "mp.rules.json");
        File.WriteAllText(path, LegacyScript("找到房间", "images/welcome.png"));

        var migrator = new RuleScriptMigrator(_root);
        var report = migrator.Migrate(new[] { path }, "LastCloudia");

        Assert.True(report.Success, report.Summarize());
        Assert.Equal(1, report.ImagesPromoted);
        Assert.True(File.Exists(Path.Combine(_root, "LastCloudia", "images", "welcome.png")));
        Assert.Equal(
            "LastCloudia/images/welcome.png",
            migrator.Conditions["LastCloudia.Welcome"].TemplatePath);
    }

    [Fact]
    public void PromotingOverADifferentImageOfTheSameNameStops()
    {
        var scriptDir = Path.Combine(_root, "TestScripts");
        Directory.CreateDirectory(Path.Combine(scriptDir, "images"));
        File.WriteAllBytes(Path.Combine(scriptDir, "images", "welcome.png"), new byte[] { 1, 2, 3 });

        var libraryImages = Path.Combine(_root, "LastCloudia", "images");
        Directory.CreateDirectory(libraryImages);
        File.WriteAllBytes(Path.Combine(libraryImages, "welcome.png"), new byte[] { 9, 9, 9 });

        var path = Path.Combine(scriptDir, "mp.rules.json");
        File.WriteAllText(path, LegacyScript("找到房间", "images/welcome.png"));

        var report = new RuleScriptMigrator(_root).Migrate(new[] { path }, "LastCloudia");

        Assert.False(report.Success);
        Assert.Contains(report.Errors, e => e.Contains("图片名冲突"));
    }

    [Fact]
    public void AlreadyMigratedScriptIsSkipped()
    {
        var path = WriteScript("done.rules.json", """
        { "FormatVersion": 2, "Rules": [ { "Name": "A", "ConditionRef": "LastCloudia.X" } ] }
        """);
        var before = File.ReadAllText(path);

        var report = new RuleScriptMigrator(_root).Migrate(new[] { path }, "LastCloudia");

        Assert.Equal(1, report.ScriptsAlreadyCurrent);
        Assert.Equal(0, report.ScriptsMigrated);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void DryRunWritesNothing()
    {
        var path = WriteScript("a.rules.json", LegacyScript("A", "LastCloudia/images/unable_to_join.png"));
        var before = File.ReadAllText(path);

        var report = new RuleScriptMigrator(_root, dryRun: true).Migrate(new[] { path }, "LastCloudia");

        Assert.True(report.Success, report.Summarize());
        Assert.Equal(before, File.ReadAllText(path));
        Assert.False(Directory.Exists(Path.Combine(_root, "LastCloudia", "Conditions")));
    }

    [Fact]
    public void MigratedScriptLoadsThroughRuleScriptServiceAndResolvesCondition()
    {
        // 端到端：迁移产物必须能被真正的加载路径读出来并解析引用。
        var imagesDir = Path.Combine(_root, "LastCloudia", "images");
        Directory.CreateDirectory(imagesDir);
        File.WriteAllBytes(Path.Combine(imagesDir, "unable_to_join.png"), new byte[] { 1 });

        var path = WriteScript("a.rules.json", LegacyScript("无法加入房间", "LastCloudia/images/unable_to_join.png"));
        new RuleScriptMigrator(_root).Migrate(new[] { path }, "LastCloudia");

        var library = new ConditionLibrary(_root);
        var warnings = library.LoadGame("LastCloudia");
        Assert.Empty(warnings);

        var script = new AutoFlow.Core.Scripting.Rules.RuleScriptService().Load(path);
        Assert.Equal(2, script.FormatVersion);
        Assert.True(script.Rules[0].UsesSharedCondition);

        var condition = library.Require(script.Rules[0].ConditionRef!, "a", "规则 #0");
        Assert.Equal(285, condition.RegionX1);
        Assert.True(Path.IsPathRooted(condition.TemplatePath));
    }
}

public class MigrationEquivalenceCheckerTests
{
    private const string Legacy = """
    {
      "FormatVersion": 1, "OuterWidth": 750, "OuterHeight": 1040,
      "Rules": [
        { "Name": "A", "Enabled": true, "TriggerOnce": false,
          "TemplatePath": "images/welcome.png",
          "MatchThreshold": 0.85, "MeanDiffThreshold": 30.0,
          "RegionX1": 1, "RegionY1": 2, "RegionX2": 3, "RegionY2": 4,
          "Actions": [ { "CommandType": "Click", "PositionX": 10 } ] }
      ]
    }
    """;

    private const string Migrated = """
    {
      "FormatVersion": 2, "OuterWidth": 750, "OuterHeight": 1040,
      "Rules": [
        { "Name": "A", "Enabled": true, "TriggerOnce": false,
          "ConditionRef": "LastCloudia.Welcome",
          "Actions": [ { "PositionX": 10, "CommandType": "Click" } ] }
      ]
    }
    """;

    private static Dictionary<string, VisualConditionSpec> Library(
        string templatePath = "LastCloudia/images/welcome.png",
        int x1 = 1, int y1 = 2, int x2 = 3, int y2 = 4)
        => new()
        {
            ["LastCloudia.Welcome"] = new VisualConditionSpec
            {
                Id = "LastCloudia.Welcome",
                TemplatePath = templatePath,
                MatchThreshold = 0.85,
                MeanDiffThreshold = 30.0,
                RegionX1 = x1, RegionY1 = y1, RegionX2 = x2, RegionY2 = y2
            }
        };

    [Fact]
    public void EquivalentMigrationReportsNoDifferences()
    {
        var differences = MigrationEquivalenceChecker.Compare(
            "a",
            MigrationEquivalenceChecker.ReadLegacy(Legacy),
            MigrationEquivalenceChecker.ReadMigrated(Migrated, Library()));

        Assert.Empty(differences);
    }

    [Fact]
    public void PathRewriteAloneIsNotADifference()
    {
        // images/welcome.png → LastCloudia/images/welcome.png 是迁移的正常动作，不能报差异。
        var differences = MigrationEquivalenceChecker.Compare(
            "a",
            MigrationEquivalenceChecker.ReadLegacy(Legacy),
            MigrationEquivalenceChecker.ReadMigrated(Migrated, Library("LastCloudia/images/welcome.png")));

        Assert.Empty(differences);
    }

    [Fact]
    public void ChangedRegionIsReported()
    {
        var differences = MigrationEquivalenceChecker.Compare(
            "a",
            MigrationEquivalenceChecker.ReadLegacy(Legacy),
            MigrationEquivalenceChecker.ReadMigrated(Migrated, Library(x1: 99)));

        Assert.Single(differences);
        Assert.Contains("行为不一致", differences[0]);
    }

    [Fact]
    public void DifferentImageIsReported()
    {
        var differences = MigrationEquivalenceChecker.Compare(
            "a",
            MigrationEquivalenceChecker.ReadLegacy(Legacy),
            MigrationEquivalenceChecker.ReadMigrated(Migrated, Library("LastCloudia/images/other.png")));

        Assert.Single(differences);
    }

    [Fact]
    public void ActionPropertyOrderIsNotADifference()
    {
        // 迁移会重写 JSON，属性顺序变化不能算行为差异。
        var differences = MigrationEquivalenceChecker.Compare(
            "a",
            MigrationEquivalenceChecker.ReadLegacy(Legacy),
            MigrationEquivalenceChecker.ReadMigrated(Migrated, Library()));

        Assert.Empty(differences);
    }

    [Fact]
    public void UnresolvableConditionRefThrows()
        => Assert.Throws<InvalidOperationException>(
            () => MigrationEquivalenceChecker.ReadMigrated(Migrated, new Dictionary<string, VisualConditionSpec>()));

    [Fact]
    public void RuleCountChangeIsReported()
    {
        var differences = MigrationEquivalenceChecker.Compare(
            "a",
            MigrationEquivalenceChecker.ReadLegacy(Legacy),
            new List<RuleBehaviour>());

        Assert.Single(differences);
        Assert.Contains("规则条数变化", differences[0]);
    }
}
