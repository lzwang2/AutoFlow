using AutoFlow.Core.Scripting;
using Newtonsoft.Json;
using Xunit;

namespace AutoFlow.Core.Tests;

public class TemplateIndexTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "AutoFlowTests_" + Guid.NewGuid().ToString("N"));

    private const string Game = "TestGame";

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Write(TemplateKind kind, string fileName, object payload)
    {
        var dir = TemplateLayout.DirectoryFor(_root, Game, kind);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, fileName);
        File.WriteAllText(path, JsonConvert.SerializeObject(payload));
        return path;
    }

    private static object RuleScriptPayload(string name, string description, int ruleCount) => new
    {
        Schema = "autoflow/rule-script",
        Metadata = new { Name = name, Description = description },
        Rules = Enumerable.Range(0, ruleCount).Select(i => new { Name = $"r{i}" }).ToArray()
    };

    [Fact]
    public void IndexesAllFourKindsSeparately()
    {
        Write(TemplateKind.Rule, "join.rules.json", RuleScriptPayload("加入房间", "规则说明", 2));
        Write(TemplateKind.Condition, "marker.condition.json", new
        {
            Id = "TestGame.Marker",
            Name = "标记",
            Description = "条件说明",
            Category = "网络"
        });
        Write(TemplateKind.ActionBlock, "retry.actionblock.json", new
        {
            Id = "TestGame.Retry",
            Name = "重试",
            Category = "通用",
            Steps = new[]
            {
                new { Id = "s1", Type = "Click" },
                new { Id = "s2", Type = "WaitTime" }
            }
            // ActionSpec 的类型字段本来就叫 Type，与 Instruction.CommandType 不同名，这里是对的。
        });
        Write(TemplateKind.LinearScript, "daily.json", new
        {
            Schema = "autoflow/script",
            Metadata = new { Description = "日常", Tags = new[] { "每日" } },
            Instructions = new[]
            {
                new { CommandType = "Click" },
                new { CommandType = "Comment" },
                new { CommandType = "WaitTime" }
            }
        });

        var entries = new TemplateIndex(_root).Build(Game, out var warnings);

        Assert.Empty(warnings);
        Assert.Equal(4, entries.Count);

        var rule = Assert.Single(entries, e => e.Kind == TemplateKind.Rule);
        Assert.Equal("加入房间", rule.Name);
        Assert.Equal("规则说明", rule.Description);
        Assert.Equal(2, rule.ItemCount);
        Assert.Equal(Game, rule.Game);

        var condition = Assert.Single(entries, e => e.Kind == TemplateKind.Condition);
        Assert.Equal("TestGame.Marker", condition.Id);
        Assert.Equal("网络", condition.Category);
        Assert.Equal(1, condition.ItemCount);

        var block = Assert.Single(entries, e => e.Kind == TemplateKind.ActionBlock);
        Assert.Equal("TestGame.Retry", block.Id);
        Assert.Equal(2, block.ItemCount);

        var script = Assert.Single(entries, e => e.Kind == TemplateKind.LinearScript);
        Assert.Equal("daily", script.Name);
        Assert.Equal("每日", script.Category);
        Assert.Equal(3, script.ItemCount);
    }

    [Fact]
    public void RuleTemplatesAreNeverConfusedWithLinearScriptTemplates()
    {
        // 自检项：「规则模板不会和线性脚本模板混淆」。
        Write(TemplateKind.Rule, "same_name.rules.json", RuleScriptPayload("规则版", "", 1));
        Write(TemplateKind.LinearScript, "same_name.json", new
        {
            Schema = "autoflow/script",
            Metadata = new { Description = "线性版" },
            Instructions = new[] { new { CommandType = "Click" } }
        });

        var entries = new TemplateIndex(_root).Build(Game, out _);

        Assert.Equal(2, entries.Count);
        Assert.Single(entries, e => e.Kind == TemplateKind.Rule);
        Assert.Single(entries, e => e.Kind == TemplateKind.LinearScript);
    }

    [Fact]
    public void FiltersByKind()
    {
        Write(TemplateKind.Rule, "a.rules.json", RuleScriptPayload("A", "", 1));
        Write(TemplateKind.Condition, "b.condition.json", new { Id = "TestGame.B", Name = "B" });

        var index = new TemplateIndex(_root);

        var onlyConditions = index.Build(Game, TemplateKind.Condition, out _);
        Assert.Equal(TemplateKind.Condition, Assert.Single(onlyConditions).Kind);

        var onlyRules = index.Build(Game, TemplateKind.Rule, out _);
        Assert.Equal(TemplateKind.Rule, Assert.Single(onlyRules).Kind);
    }

    [Fact]
    public void FindsRuleTemplatesInPreMigrationLayout()
    {
        // Phase 6 之前规则文件直接躺在 <Game>/ 下；迁移期间库不能显示为空。
        var gameDir = Path.Combine(_root, Game);
        Directory.CreateDirectory(gameDir);
        File.WriteAllText(
            Path.Combine(gameDir, "legacy.rules.json"),
            JsonConvert.SerializeObject(RuleScriptPayload("旧位置", "", 1)));

        var entries = new TemplateIndex(_root).Build(Game, out var warnings);

        Assert.Empty(warnings);
        Assert.Equal("旧位置", Assert.Single(entries).Name);
    }

    [Fact]
    public void BrokenFileIsReportedNotThrown()
    {
        var dir = TemplateLayout.DirectoryFor(_root, Game, TemplateKind.Condition);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "broken.condition.json"), "{ not json");

        Write(TemplateKind.Condition, "good.condition.json", new { Id = "TestGame.Good", Name = "好的" });

        var entries = new TemplateIndex(_root).Build(Game, out var warnings);

        // 一个坏文件不能让整个库不可浏览。
        Assert.Equal("TestGame.Good", Assert.Single(entries).Id);
        Assert.Contains("broken.condition.json", Assert.Single(warnings));
    }

    [Fact]
    public void FallsBackToIdWhenNameMissing()
    {
        Write(TemplateKind.Condition, "x.condition.json", new { Id = "TestGame.NoName" });

        var entry = Assert.Single(new TemplateIndex(_root).Build(Game, out _));
        Assert.Equal("TestGame.NoName", entry.Name);
    }

    [Fact]
    public void MissingGameDirectoryYieldsEmptyIndex()
    {
        Directory.CreateDirectory(_root);

        var entries = new TemplateIndex(_root).Build("NoSuchGame", out var warnings);

        Assert.Empty(entries);
        Assert.Empty(warnings);
    }

    [Fact]
    public void GamesListsDirectoriesUnderRoot()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Zebra"));
        Directory.CreateDirectory(Path.Combine(_root, "Alpha"));
        File.WriteAllText(Path.Combine(_root, "README.md"), "not a game");

        Assert.Equal(new[] { "Alpha", "Zebra" }, new TemplateIndex(_root).Games());
    }

    [Fact]
    public void BuildAllSpansEveryGame()
    {
        Write(TemplateKind.Rule, "a.rules.json", RuleScriptPayload("A", "", 1));

        var otherDir = TemplateLayout.DirectoryFor(_root, "OtherGame", TemplateKind.Rule);
        Directory.CreateDirectory(otherDir);
        File.WriteAllText(
            Path.Combine(otherDir, "b.rules.json"),
            JsonConvert.SerializeObject(RuleScriptPayload("B", "", 1)));

        var entries = new TemplateIndex(_root).BuildAll(out _);

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Game == Game);
        Assert.Contains(entries, e => e.Game == "OtherGame");
    }

    [Fact]
    public void DisplayTextDistinguishesKinds()
    {
        var rule = new TemplateIndexEntry { Kind = TemplateKind.Rule, Name = "R", ItemCount = 3 };
        var block = new TemplateIndexEntry { Kind = TemplateKind.ActionBlock, Name = "B", ItemCount = 2 };
        var script = new TemplateIndexEntry { Kind = TemplateKind.LinearScript, Name = "S", ItemCount = 5 };
        var condition = new TemplateIndexEntry { Kind = TemplateKind.Condition, Name = "C", Id = "G.C" };

        Assert.Equal("R (3 个规则)", rule.DisplayText);
        Assert.Equal("B (2 步)", block.DisplayText);
        Assert.Equal("S (5 条指令)", script.DisplayText);
        Assert.Equal("C [G.C]", condition.DisplayText);
    }
}
