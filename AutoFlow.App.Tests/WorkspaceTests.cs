using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AutoFlow.App.Services;
using AutoFlow.App.ViewModels;
using AutoFlow.App.Views;
using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Instructions;
using AutoFlow.Core.Scripting.Rules;
using Newtonsoft.Json;
using Xunit;

namespace AutoFlow.App.Tests;

public sealed class WorkspaceTests
{
    private sealed class Dialogs : IEditorInteraction
    {
        public MessageBoxResult Answer = MessageBoxResult.No;
        public string? Path;
        public string? Error;
        public MessageBoxResult ConfirmReplacement() => Answer;
        public string? ChooseSavePath(string name) => Path;
        public void ReportSaveFailure(string message) => Error = message;
    }

    private static void OnUi(Func<Task> test)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.BeginInvoke(async () =>
            {
                try { await test(); }
                catch (Exception ex) { error = ex; }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "STA test timed out");
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }

    [Fact]
    public void NavigationLoadsCompletePresetWithoutChangingEditorAndCopiesKeepMetadata() => OnUi(() =>
    {
        using var vm = new MainViewModel(new Dialogs());
        vm.SelectedGame = "OtherGame";
        var size = (vm.ResizeWidth, vm.ResizeHeight, vm.ScriptReferenceWidth, vm.ScriptReferenceHeight);
        vm.WorkspaceIndex = 1;
        Assert.Contains("已加载", vm.GameLoadStatus);
        Assert.Equal(2, vm.LastCloudia.Modules.Count);
        Assert.Equal("LastCloudia.MultiPlayerGame", vm.LastCloudia.SelectedModule!.Id);
        Assert.Equal(size, (vm.ResizeWidth, vm.ResizeHeight, vm.ScriptReferenceWidth, vm.ScriptReferenceHeight));
        Assert.False(vm.IsCapturing);
        Assert.False(vm.IsRunning);
        Assert.Empty(vm.Rules);
        Assert.Equal("OtherGame", vm.SelectedGame);
        vm.OpenGameInEditorCommand.Execute(null);
        Assert.Equal(0, vm.WorkspaceIndex);
        Assert.Equal(3, vm.Rules.Count);
        var presetPath = System.IO.Path.Combine(AppContext.BaseDirectory, "RuleTemplates", "LastCloudia", "Rules", "MultiPlayerGame.rules.json");
        Assert.Equal(presetPath, vm.LastCloudia.SelectedModule.ScriptPath);
        var entry = Assert.Single(new TemplateIndex().Build("LastCloudia", TemplateKind.Rule, out _),
            entry => string.Equals(entry.FilePath, presetPath, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(3, entry.ItemCount);
        var expected = GameTaskScript.Load(presetPath);
        var snapshot = vm.BuildRuleDocument();
        Assert.Equal(expected.IdleTickMs, snapshot.IdleTickMs);
        Assert.Equal(expected.Metadata.Description, snapshot.Metadata.Description);
        Assert.Equal(expected.Rules.Select(r => r.Name), snapshot.Rules.Select(r => r.Name));
        Assert.Equal(JsonConvert.SerializeObject(expected), JsonConvert.SerializeObject(snapshot));
        var copyPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AutoFlow-copy-" + Guid.NewGuid().ToString("N") + ".rules.json");
        try
        {
            new RuleScriptService().Save(snapshot, copyPath);
            var libraries = vm.LastCloudia.LoadLibraries();
            GameTaskScript.Validate(GameTaskScript.Load(copyPath), libraries.Conditions, libraries.Blocks, AppContext.BaseDirectory);
        }
        finally { File.Delete(copyPath); }
        vm.Rules[0].Enabled = false;
        vm.WorkspaceIndex = 1;
        vm.OpenGameInEditorCommand.Execute(null);
        Assert.True(vm.Rules[0].Enabled);
        return Task.CompletedTask;
    });

    [Fact]
    public void WeekRoundModuleOpensOneCallSkillInLinearEditor() => OnUi(() =>
    {
        using var vm = new MainViewModel(new Dialogs());
        vm.WorkspaceIndex = 1;
        var weekRound = Assert.Single(vm.LastCloudia.Modules, m => m.Id == "LastCloudia.WeekRound");
        Assert.True(weekRound.IsRunnable);
        vm.LastCloudia.SelectedModule = weekRound;
        Assert.Contains("已加载", vm.GameLoadStatus);

        vm.OpenGameInEditorCommand.Execute(null);

        Assert.Equal(0, vm.WorkspaceIndex);
        Assert.Equal(MainViewModel.LinearTabIndex, vm.SelectedTabIndex);
        var step = Assert.Single(vm.Steps);
        Assert.Equal(CommandType.CallSkill, step.Action!.CommandType);
        Assert.Equal("LastCloudia.WeekRound", step.Model!.SkillName);
        Assert.Empty(vm.Rules);
        Assert.Equal(750, vm.ResizeWidth);
        Assert.Equal(1040, vm.ResizeHeight);
        return Task.CompletedTask;
    });

    [Fact]
    public void ModuleSelectionIsLockedForTheWholeRun() => OnUi(async () =>
    {
        using var vm = new MainViewModel(new Dialogs());
        vm.WorkspaceIndex = 1;
        var multi = vm.LastCloudia.SelectedModule!;
        var weekRound = Assert.Single(vm.LastCloudia.Modules, m => m.Id == "LastCloudia.WeekRound");

        vm.RunScriptCommand.Execute(null);
        Assert.True(vm.IsRunning);
        Assert.True(vm.LastCloudia.SelectionLocked);
        vm.LastCloudia.SelectedModule = weekRound;
        Assert.Same(multi, vm.LastCloudia.SelectedModule);

        vm.StopScriptCommand.Execute(null);
        await vm.CurrentRun;
        Assert.False(vm.LastCloudia.SelectionLocked);
        vm.LastCloudia.SelectedModule = weekRound;
        Assert.Same(weekRound, vm.LastCloudia.SelectedModule);
    });

    [Theory]
    [InlineData("multi_clear_reward", "MultiClearReward")]
    [InlineData("unable_to_join", "UnableToJoin")]
    [InlineData("multi_game_refight", "MultiGameRefight")]
    [InlineData("first_clear_reward", "FirstClearReward")]
    [InlineData("first_meeting", "FirstMeeting")]
    [InlineData("refight_denied", "RefightDenied")]
    [InlineData("abandon_task", "AbandonTask")]
    [InlineData("server_connection_fail_retreat", "ServerConnectionFailRetreat")]
    [InlineData("server_connection_fail_return", "ServerConnectionFailReturn")]
    [InlineData("server_connection_fail_multiplayer", "ServerConnectionFailMultiplayer")]
    [InlineData("network_error", "NetworkError")]
    public void SharedActionReferenceSurvivesEditorAndSave(string file, string suffix) => OnUi(() =>
    {
        var source = Path.Combine(AppContext.BaseDirectory, "RuleTemplates", "LastCloudia", "Rules", file + ".rules.json");
        var service = new RuleScriptService();
        var rule = Assert.Single(service.Load(source).Rules);
        var editor = new RuleViewModel(rule);
        Assert.Null(Assert.Single(editor.Actions).Model); // a reference, not a copied Click
        editor.SyncActions();
        var path = Path.Combine(Path.GetTempPath(), "AutoFlow-reward-" + Guid.NewGuid().ToString("N") + ".rules.json");
        try
        {
            service.Save(new RuleScript { Rules = [editor.Model] }, path);
            var saved = Assert.Single(service.Load(path).Rules);
            Assert.Empty(saved.Actions);
            Assert.Equal("LastCloudia." + suffix, saved.ConditionRef);
            Assert.Equal("LastCloudia.Confirm" + suffix, Assert.Single(saved.Steps).BlockRef!.BlockId);
        }
        finally { File.Delete(path); }
        return Task.CompletedTask;
    });

    [Fact]
    public void ActionBlockClickCoordinatesCanBePickedFromPreview() => OnUi(() =>
    {
        using var vm = new MainViewModel(new Dialogs());
        var block = new ActionBlock
        {
            Id = "Test.Click",
            Steps =
            [
                new ActionSpec
                {
                    Type = CommandType.Click,
                    Parameters = new()
                    {
                        [nameof(Instruction.PositionX)] = 10,
                        [nameof(Instruction.PositionY)] = 20
                    }
                }
            ]
        };

        vm.SelectedTabIndex = MainViewModel.ActionBlockTabIndex;
        vm.SelectedLibraryActionBlock = new ActionBlockViewModel(block);
        vm.ScriptReferenceWidth = 750;
        vm.ScriptReferenceHeight = 1040;
        vm.IsPickingCoordinate = true;

        vm.OnPreviewClicked(0.5, 0.25);

        Assert.Equal(375, vm.SelectedBlockAction!.PositionX);
        Assert.Equal(260, vm.SelectedBlockAction.PositionY);
        Assert.False(vm.IsPickingCoordinate);

        vm.SelectedLibraryActionBlock.ApplyToModel();
        var saved = ActionSpecMapper.ToInstruction(Assert.Single(block.Steps));
        Assert.Equal(375, saved.PositionX);
        Assert.Equal(260, saved.PositionY);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveCancellationOrFailureDoesNotReplaceDocument(bool fail) => OnUi(() =>
    {
        var dialogs = new Dialogs();
        using var vm = new MainViewModel(dialogs);
        vm.WorkspaceIndex = 1;
        vm.OpenGameInEditorCommand.Execute(null);
        vm.Rules[0].Name = "unsaved marker";
        vm.WorkspaceIndex = 1;
        dialogs.Answer = MessageBoxResult.Yes;
        dialogs.Path = fail ? System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing", "copy.json") : null;
        vm.OpenGameInEditorCommand.Execute(null);
        Assert.Equal(1, vm.WorkspaceIndex);
        Assert.Equal("unsaved marker", vm.Rules[0].Name);
        Assert.Equal(fail, dialogs.Error is not null);
        return Task.CompletedTask;
    });

    [Fact]
    public void StartupLocksEnvironmentAndCanCancelBeforeWindowOrCaptureOperations() => OnUi(async () =>
    {
        using var vm = new MainViewModel(new Dialogs());
        vm.RunScriptCommand.Execute(null);
        var run = vm.CurrentRun;
        Assert.True(vm.IsRunning);
        Assert.False(vm.CanChangeEnvironment);
        Assert.True(vm.StopScriptCommand.CanExecute(null));
        Assert.False(vm.StartCaptureCommand.CanExecute(null));
        Assert.False(vm.StopCaptureCommand.CanExecute(null));
        vm.RunScriptCommand.Execute(null);
        Assert.Same(run, vm.CurrentRun);
        vm.WorkspaceIndex = 1;
        Assert.True(vm.IsRunning);
        vm.StopScriptCommand.Execute(null);
        await run;
        Assert.False(vm.IsRunning);
        Assert.True(vm.CanChangeEnvironment);
        Assert.False(vm.IsCapturing);
        Assert.Contains("已停止", vm.TaskStatus);
        vm.RunScriptCommand.Execute(null);
        vm.StopScriptCommand.Execute(null);
        await vm.CurrentRun;
        Assert.False(vm.IsRunning);
    });

    [Fact]
    public void GameLayoutBindsSharedControlsAndKeepsStopAvailableDuringStartup() => OnUi(async () =>
    {
        var window = new MainWindow();
        using var vm = (MainViewModel)window.DataContext;
        var layout = (FrameworkElement)window.Content;
        window.Content = null;
        layout.DataContext = vm;
        vm.WorkspaceIndex = 1;
        await Dispatcher.Yield(DispatcherPriority.DataBind);
        layout.Measure(new Size(1320, 820));
        layout.Arrange(new Rect(0, 0, 1320, 820));
        layout.UpdateLayout();
        var controls = Descendants(layout).ToList();
        Assert.Equal(Visibility.Visible, Assert.Single(controls.OfType<LastCloudiaView>()).Visibility);
        var start = Assert.Single(controls.OfType<Button>(), b => Equals(b.Content, "▶ 开始 (F9)"));
        var stop = Assert.Single(controls.OfType<Button>(), b => Equals(b.Content, "■ 停止任务 (F10)"));
        Assert.Same(vm.RunScriptCommand, start.Command);
        Assert.Same(vm.StopScriptCommand, stop.Command);
        Assert.False(start.IsEnabled); // no target window selected
        var snapshotPath = Environment.GetEnvironmentVariable("AUTOFLOW_UI_SNAPSHOT_PATH");
        if (!string.IsNullOrEmpty(snapshotPath))
        {
            var bitmap = new RenderTargetBitmap(1320, 820, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(layout);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(snapshotPath)!);
            using var stream = File.Create(snapshotPath);
            encoder.Save(stream);
        }
        vm.RunScriptCommand.Execute(null);
        Assert.True(stop.Command.CanExecute(null));
        Assert.All(controls.OfType<ComboBox>().Where(c => c.IsVisible), c => Assert.False(c.IsEnabled));
        vm.StopScriptCommand.Execute(null);
        await vm.CurrentRun;
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
