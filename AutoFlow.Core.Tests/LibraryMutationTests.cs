using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Instructions;
using Newtonsoft.Json;
using Xunit;

namespace AutoFlow.Core.Tests;

/// <summary>
/// Covers Clear and Delete on both shared libraries — the write side the new editor tabs need.
///
/// <para>Clear is not a convenience method: without it <c>LoadGame</c> accumulates, which is what
/// produced a flood of "条件 Id 重复" warnings naming one file as a duplicate of itself, left a
/// previous game's objects in the pickers after a game switch, and kept a deleted condition
/// resolvable until the process restarted. Each of those is pinned below.</para>
/// </summary>
public class LibraryMutationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "AutoFlowTests_" + Guid.NewGuid().ToString("N"));

    private const string Game = "TestGame";
    private const string OtherGame = "OtherGame";

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private void WriteCondition(string game, string id)
    {
        var dir = ConditionLibrary.DirectoryFor(_root, game);
        Directory.CreateDirectory(dir);

        // The image has to exist: a condition whose TemplatePath does not resolve makes LoadGame
        // emit an "图片无法定位" warning, which would show up in the duplicate-warning assertions
        // below as a false positive.
        var imagesDir = Path.Combine(_root, game, "images");
        Directory.CreateDirectory(imagesDir);
        File.WriteAllBytes(Path.Combine(imagesDir, "x.png"), new byte[] { 1, 2, 3 });

        File.WriteAllText(
            Path.Combine(dir, id.Replace('.', '_') + ".condition.json"),
            JsonConvert.SerializeObject(new
            {
                Id = id,
                Name = id,
                TemplatePath = $"{game}/images/x.png",
                ReferenceOuterWidth = 750,
                ReferenceOuterHeight = 1040
            }));
    }

    private void WriteBlock(string game, string id)
    {
        var dir = ActionBlockLibrary.DirectoryFor(_root, game);
        Directory.CreateDirectory(dir);
        File.WriteAllText(
            Path.Combine(dir, id.Replace('.', '_') + ".actionblock.json"),
            JsonConvert.SerializeObject(new { Id = id, Name = id, Steps = Array.Empty<object>() }));
    }

    // === The duplicate-warning flood ===

    [Fact]
    public void LoadingTheSameGameTwiceWithoutClearReportsEveryConditionAsItsOwnDuplicate()
    {
        WriteCondition(Game, "TestGame.Alpha");
        WriteCondition(Game, "TestGame.Beta");

        var library = new ConditionLibrary(_root);
        Assert.Empty(library.LoadGame(Game));

        // This is the bug, reproduced rather than fixed: the second load sees both Ids already
        // present and reports each file as colliding with itself. The message names the same path
        // twice, which is what made it baffling in the log.
        var second = library.LoadGame(Game);

        Assert.Equal(2, second.Count);
        Assert.All(second, warning => Assert.Contains("Id 重复", warning));
    }

    [Fact]
    public void ClearBeforeReloadingTheSameGameProducesNoWarnings()
    {
        WriteCondition(Game, "TestGame.Alpha");
        WriteCondition(Game, "TestGame.Beta");

        var library = new ConditionLibrary(_root);
        library.LoadGame(Game);

        library.Clear();
        var reload = library.LoadGame(Game);

        Assert.Empty(reload);
        Assert.Equal(2, library.All.Count);
    }

    [Fact]
    public void ClearDropsThePreviousGamesConditions()
    {
        WriteCondition(Game, "TestGame.Alpha");
        WriteCondition(OtherGame, "OtherGame.Gamma");

        var library = new ConditionLibrary(_root);
        library.LoadGame(Game);

        library.Clear();
        library.LoadGame(OtherGame);

        // Without the clear, a game switch left the old game's conditions selectable in the
        // picker — and referencing one produced a script that only resolved on this machine,
        // in this session.
        Assert.False(library.TryGet("TestGame.Alpha", out _));
        Assert.True(library.TryGet("OtherGame.Gamma", out _));
    }

    [Fact]
    public void ClearAlsoResetsSourceFileTracking()
    {
        WriteCondition(Game, "TestGame.Alpha");

        var library = new ConditionLibrary(_root);
        library.LoadGame(Game);
        Assert.NotNull(library.SourceFileOf("TestGame.Alpha"));

        library.Clear();

        // Both dictionaries have to be cleared. Clearing only _byId would leave the Id looking
        // unknown while still claiming a source file, so a later Save would not re-register it.
        Assert.Null(library.SourceFileOf("TestGame.Alpha"));
    }

    // === Delete ===

    [Fact]
    public void DeleteRemovesTheFileAndForgetsTheCondition()
    {
        WriteCondition(Game, "TestGame.Alpha");

        var library = new ConditionLibrary(_root);
        library.LoadGame(Game);
        var path = library.SourceFileOf("TestGame.Alpha");
        Assert.True(File.Exists(path));

        Assert.True(library.Delete("TestGame.Alpha"));

        Assert.False(File.Exists(path));
        Assert.False(library.TryGet("TestGame.Alpha", out _));
        Assert.Empty(library.All);
    }

    [Fact]
    public void DeletingAnUnknownIdReportsFailureRatherThanThrowing()
    {
        var library = new ConditionLibrary(_root);
        library.LoadGame(Game);

        // False, not an exception: the UI turns this into "not in the loaded library, nothing was
        // deleted", which is a truthful message. An exception would look like a failed delete of
        // something that existed.
        Assert.False(library.Delete("TestGame.Missing"));
        Assert.False(library.Delete(""));
        Assert.False(library.Delete("   "));
    }

    [Fact]
    public void DeleteSucceedsWhenTheFileWasAlreadyRemovedOutsideTheApp()
    {
        WriteCondition(Game, "TestGame.Alpha");

        var library = new ConditionLibrary(_root);
        library.LoadGame(Game);
        File.Delete(library.SourceFileOf("TestGame.Alpha")!);

        // The point of the delete is that the Id stops resolving. A file someone removed in
        // Explorer first must not leave the library permanently unable to forget the Id.
        Assert.True(library.Delete("TestGame.Alpha"));
        Assert.False(library.TryGet("TestGame.Alpha", out _));
    }

    [Fact]
    public void SaveAfterDeleteReinstatesTheCondition()
    {
        WriteCondition(Game, "TestGame.Alpha");

        var library = new ConditionLibrary(_root);
        library.LoadGame(Game);
        library.Delete("TestGame.Alpha");

        var path = library.Save(
            new VisualConditionSpec { Id = "TestGame.Alpha", Name = "回来了" }, Game);

        Assert.True(File.Exists(path));
        Assert.True(library.TryGet("TestGame.Alpha", out var condition));
        Assert.Equal("回来了", condition!.Name);
    }

    // === Action blocks: same contract ===

    [Fact]
    public void ActionBlockClearAndDeleteBehaveLikeConditions()
    {
        WriteBlock(Game, "TestGame.Close");
        WriteBlock(Game, "TestGame.Retry");

        var library = new ActionBlockLibrary(_root);
        Assert.Empty(library.LoadGame(Game));
        Assert.Equal(2, library.LoadGame(Game).Count); // accumulates without a clear

        library.Clear();
        Assert.Empty(library.LoadGame(Game));

        var path = library.SourceFileOf("TestGame.Close");
        Assert.True(library.Delete("TestGame.Close"));
        Assert.False(File.Exists(path));
        Assert.False(library.Delete("TestGame.Close")); // already gone
        Assert.Single(library.All);
    }

    [Fact]
    public void DeletedBlockStopsResolvingForReferencingScripts()
    {
        WriteBlock(Game, "TestGame.Close");

        var library = new ActionBlockLibrary(_root);
        library.LoadGame(Game);
        library.Delete("TestGame.Close");

        // A step still pointing at the deleted block must fail loudly at expansion time. Silently
        // expanding to nothing would let a script run with a missing step and look fine.
        var reference = new ActionBlockRef { BlockId = "TestGame.Close" };
        var ex = Assert.Throws<ActionBlockResolutionException>(
            () => library.Expand(new object[] { reference }, "测试脚本"));

        Assert.Contains("TestGame.Close", ex.Message);
    }

    [Fact]
    public void SavedBlockKeepsItsStepsThroughAJsonRoundTrip()
    {
        var block = new ActionBlock
        {
            Id = "TestGame.Tap",
            Name = "点一下",
            Steps =
            {
                ActionSpecMapper.ToActionSpec(new Instruction
                {
                    CommandType = CommandType.Click,
                    PositionX = 120,
                    PositionY = 240
                })
            }
        };

        var library = new ActionBlockLibrary(_root);
        library.Save(block, Game);

        // Reloaded through a fresh library, because the save path also keeps the object in memory
        // — asserting on that instance would not prove anything reached disk intact.
        var reloaded = new ActionBlockLibrary(_root);
        Assert.Empty(reloaded.LoadGame(Game));
        Assert.True(reloaded.TryGet("TestGame.Tap", out var loaded));

        var instruction = ActionSpecMapper.ToInstruction(loaded!.Steps.Single());
        Assert.Equal(CommandType.Click, instruction.CommandType);
        Assert.Equal(120, instruction.PositionX);
        Assert.Equal(240, instruction.PositionY);
    }
}
