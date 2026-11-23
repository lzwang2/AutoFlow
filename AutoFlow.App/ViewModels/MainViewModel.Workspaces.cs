using System.IO;
using System.Windows;
using AutoFlow.App.Common;
using AutoFlow.Core.Capture;
using AutoFlow.Core.Input;
using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Handlers;
using AutoFlow.Core.Scripting.Rules;
using AutoFlow.Core.Vision;
using Fischless.WindowsInput;

namespace AutoFlow.App.ViewModels;

public sealed partial class MainViewModel
{
    private readonly ExecutionGate _executionGate = new();
    public LastCloudiaViewModel LastCloudia { get; private set; } = null!;
    private RuleScript _ruleDocument = new();
    private int _workspaceIndex;
    private string _taskStatus = "待机";
    public Task CurrentRun { get; private set; } = Task.CompletedTask;
    public bool CanChangeEnvironment => !IsRunning;
    public bool IsEditorWorkspace => WorkspaceIndex == 0;
    public bool IsGameWorkspace => WorkspaceIndex == 1;
    public string TaskStatus { get => _taskStatus; private set => SetProperty(ref _taskStatus, value); }
    public string GameLoadStatus => LastCloudia.SelectedModule?.StatusText ?? "未选择功能";
    public RelayCommand ReloadGameCommand { get; private set; } = null!;
    public RelayCommand OpenGameInEditorCommand { get; private set; } = null!;

    public int WorkspaceIndex
    {
        get => _workspaceIndex;
        set
        {
            if (!SetProperty(ref _workspaceIndex, value)) return;
            OnPropertyChanged(nameof(IsEditorWorkspace));
            OnPropertyChanged(nameof(IsGameWorkspace));
            if (IsGameWorkspace && !LastCloudia.HasLoaded) LoadGameTasks();
            CommandManagerRefresh();
        }
    }

    private void InitializeWorkspaces()
    {
        LastCloudia = new LastCloudiaViewModel(Log);
        LastCloudia.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName != nameof(LastCloudiaViewModel.SelectedModule)) return;
            OnPropertyChanged(nameof(GameLoadStatus));
            CommandManagerRefresh();
        };
        ReloadGameCommand = new RelayCommand(ReloadSelectedGameTask, () => !IsRunning && LastCloudia.SelectedModule is not null);
        OpenGameInEditorCommand = new RelayCommand(OpenGameInEditor,
            () => !IsRunning && LastCloudia.SelectedModule?.IsRunnable == true);
    }

    private void LoadGameTasks()
    {
        LastCloudia.ReloadAll();
        OnPropertyChanged(nameof(GameLoadStatus));
        CommandManagerRefresh();
    }

    private void ReloadSelectedGameTask()
    {
        LastCloudia.ReloadSelected();
        OnPropertyChanged(nameof(GameLoadStatus));
        CommandManagerRefresh();
    }

    internal RuleScript BuildRuleDocument()
    {
        foreach (var rule in Rules) rule.SyncActions();
        var script = GameTaskScript.Copy(_ruleDocument);
        script.Rules = Rules.Select(r => GameTaskScript.Copy(r.Model)).ToList();
        script.OuterWidth = ResizeWidth;
        script.OuterHeight = ResizeHeight;
        return script;
    }

    private void OpenGameInEditor()
    {
        var module = LastCloudia.SelectedModule;
        if (IsRunning || module?.IsRunnable != true) return;
        // All potentially replaced working sets are handled before changing any page or model.
        if (!ProtectEditorWork()) return;
        SelectedGame = "LastCloudia";
        RebuildAvailableConditions();
        _currentFilePath = null;
        _currentLinearTemplatePath = null;
        ScriptName = module.DisplayName;
        if (module.Kind == LastCloudiaTaskKind.RuleScript)
        {
            var script = GameTaskScript.Copy(module.RuleScript!);
            _ruleDocument = script;
            Rules.Clear();
            Steps.Clear();
            foreach (var rule in script.Rules) Rules.Add(CreateRuleViewModel(rule));
            SelectedRule = Rules.FirstOrDefault();
            _suppressProfileSave = true;
            try { ResizeWidth = script.OuterWidth; ResizeHeight = script.OuterHeight; }
            finally { _suppressProfileSave = false; }
            SelectedTabIndex = RuleTabIndex;
        }
        else
        {
            Rules.Clear();
            _ruleDocument = new RuleScript();
            var script = GameTaskScript.Copy(module.LinearScript!);
            SelectedTabIndex = LinearTabIndex;
            LoadLinearDocument(script, module.DisplayName, applyWindowSize: false);
            _suppressProfileSave = true;
            try { ResizeWidth = script.OuterWidth; ResizeHeight = script.OuterHeight; }
            finally { _suppressProfileSave = false; }
        }
        WorkspaceIndex = 0;
        Log($"已在编辑器中打开{module.DisplayName}副本；保存时请选择新位置，修改不会同步回专用面板。");
    }

    private bool ProtectEditorWork()
    {
        if (Rules.Count == 0 && Steps.Count == 0 && LibraryConditions.Count == 0 && LibraryActionBlocks.Count == 0)
            return true;
        var answer = _editorInteraction.ConfirmReplacement();
        if (answer == MessageBoxResult.Cancel) return false;
        if (answer == MessageBoxResult.No) return true;
        try
        {
            // Use explicit save dialogs: neither document can accidentally overwrite a preset.
            if (Steps.Count > 0)
            {
                var path = _editorInteraction.ChooseSavePath("当前线性脚本.json");
                if (path is null) return false;
                var document = BuildLinearDocument();
                document.OuterWidth = ResizeWidth;
                document.OuterHeight = ResizeHeight;
                _documentService.Save(document, path);
            }
            if (Rules.Count > 0)
            {
                var path = _editorInteraction.ChooseSavePath("当前规则脚本.rules.json");
                if (path is null) return false;
                _ruleService.Save(BuildRuleDocument(), path);
            }
            foreach (var condition in LibraryConditions.ToArray())
                if (!TrySaveCondition(condition.Model, out _)) return false;
            foreach (var block in LibraryActionBlocks.ToArray())
            {
                block.ApplyToModel();
                _actionBlockLibrary.Save(block.Model, SelectedGame);
            }
            return true;
        }
        catch (Exception ex)
        {
            _editorInteraction.ReportSaveFailure(ex.Message);
            return false;
        }
    }

    private void BeginRun()
    {
        if (!_executionGate.TryEnter()) return;
        LastCloudiaTaskSnapshot? gameTask = null;
        if (IsGameWorkspace)
        {
            var module = LastCloudia.SelectedModule;
            if (module?.IsRunnable != true)
            {
                TaskStatus = "Last Cloudia · 当前功能不可运行，请重新加载。";
                Log(TaskStatus);
                _executionGate.Exit();
                return;
            }
            try { gameTask = module.CreateSnapshot(); }
            catch (Exception ex)
            {
                TaskStatus = $"Last Cloudia · {module.DisplayName} · 快照失败：{ex.Message}";
                Log(TaskStatus);
                _executionGate.Exit();
                return;
            }
        }
        _runCts = new CancellationTokenSource();
        IsRunning = true;
        var taskName = gameTask is null ? "脚本编辑器" : $"Last Cloudia · {gameTask.Module.DisplayName}";
        gameTask?.Module.SetRunStatus("启动中");
        TaskStatus = taskName + " · 启动中";
        CurrentRun = ExecuteCurrentTaskAsync(IsGameWorkspace, gameTask, _runCts);
    }

    private async Task ExecuteCurrentTaskAsync(bool game, LastCloudiaTaskSnapshot? gameTask, CancellationTokenSource cancellation)
    {
        var ct = cancellation.Token;
        var newCapture = false;
        var enteredLoop = false;
        var taskName = gameTask is null ? "脚本编辑器" : $"Last Cloudia · {gameTask.Module.DisplayName}";
        try
        {
            // Give the dispatcher a chance to paint the locked environment and accept F10.
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            var target = SelectedWindow ?? throw new InvalidOperationException("请先选择目标窗口。");
            var windowInfo = new WindowInfoService();
            windowInfo.SetTargetWindow(target.Handle, target.Title);
            if (!windowInfo.TryGetWindowInfo(out _)) throw new InvalidOperationException("目标窗口已失效，请刷新并重新选择。");
            var mode = SelectedCaptureMode;
            RuleScript? ruleScript = null;
            ScriptDocument? document = null;
            ConditionLibrary conditions;
            ActionBlockLibrary blocks;
            if (game)
            {
                if (gameTask is null) throw new InvalidOperationException("请重新选择并加载功能。");
                ruleScript = gameTask.RuleScript;
                document = gameTask.LinearScript;
                (conditions, blocks) = LastCloudia.LoadLibraries();
                if (ruleScript is not null) GameTaskScript.Validate(ruleScript, conditions, blocks, AppContext.BaseDirectory);
                else GameTaskScript.Validate(document!, conditions, blocks, AppContext.BaseDirectory);
            }
            else
            {
                conditions = _conditionLibrary.Snapshot();
                blocks = _actionBlockLibrary.Snapshot();
                if (IsLinearMode) document = GameTaskScript.Copy(BuildLinearDocument());
                else ruleScript = BuildRuleDocument();
            }

            // Check skill-only references before resizing or starting a capture, including editor runs.
            var skillResources = new SharedScriptResources(conditions, blocks, InstructionHandlerRegistry.CreateDefault());
            var expander = new ScriptStepExpander(conditions, blocks);
            var taskSteps = ruleScript is not null
                ? ruleScript.Rules.SelectMany(r => r.Steps.Count > 0
                    ? expander.Expand(r.Steps, taskName, r.Name)
                    : r.Actions.Select(a => new ExpandedStep { Instruction = a, Origin = r.Name }).ToList())
                : expander.Expand(document!, taskName);
            skillResources.ValidateSkillDependencies(taskSteps);

            ct.ThrowIfCancellationRequested();
            var transform = new CoordinateTransformService();
            if (game || ResizeWindowBeforeRun)
            {
                var width = game ? ruleScript?.OuterWidth ?? document!.OuterWidth : ResizeWidth;
                var height = game ? ruleScript?.OuterHeight ?? document!.OuterHeight : ResizeHeight;
                var profile = new WindowTargetProfile { ResizeBeforeRun = true, ResizeWidth = width, ResizeHeight = height };
                if (!windowInfo.TryApplyResize(target.Handle, profile, out var client, out var reason))
                {
                    if (game || MessageBox.Show(reason + "\n是否继续运行？", "调整窗口失败",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                        throw new InvalidOperationException(reason);
                    transform.SetReferenceResolution(ScriptReferenceWidth, ScriptReferenceHeight);
                }
                else
                {
                    if (client.Width <= 0 || client.Height <= 0) throw new InvalidOperationException("实测客户区尺寸无效。");
                    MeasuredClientSize = $"{client.Width}×{client.Height}";
                    transform.SetReferenceResolution(game ? client.Width : ScriptReferenceWidth, game ? client.Height : ScriptReferenceHeight);
                    Log($"目标外框 {width}×{height}，实测客户区 {client.Width}×{client.Height}");
                }
            }
            else transform.SetReferenceResolution(ScriptReferenceWidth, ScriptReferenceHeight);

            ct.ThrowIfCancellationRequested();
            if (!_captureService.Matches(target.Handle, mode))
            {
                newCapture = true;
                _captureService.Start(target.Handle, mode);
                IsCapturing = true;
                _previewTimer.Start();
            }
            if (_captureService.Current is null || !_captureService.IsCapturing)
                throw new InvalidOperationException("捕获初始化失败。");
            var frameReady = false;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(100, ct);
                using var frame = _captureService.CaptureFrame();
                if (frame is not null && !frame.Empty()) { frameReady = true; break; }
            }
            if (!frameReady) throw new InvalidOperationException("无法捕获游戏画面，请检查窗口状态及捕获方式。");
            if (!NativeWindowHelper.TryActivateWindow(target.Handle)) Log("警告：未能激活目标窗口。");
            await Task.Delay(300, ct);
            ct.ThrowIfCancellationRequested();
            var context = new ScriptExecutionContext
            {
                Capture = _captureService.Current,
                Input = new InputService(new InputSimulator()),
                CoordinateTransform = transform,
                WindowInfo = windowInfo,
                TemplateMatch = TemplateMatchService.Instance,
                Logger = msg => Application.Current.Dispatcher.Invoke(() => Log(msg)),
                ProgressReporter = progress => Application.Current.Dispatcher.Invoke(() =>
                {
                    TaskStatus = taskName + " · " + progress;
                    gameTask?.Module.SetRunStatus(progress);
                }),
                EnableFrameDebug = EnableFrameDebug, FrameDebugPath = FrameDebugPath, FrameDebugMode = FrameDebugMode,
                EnableVerboseLogging = EnableVerboseLogging
            };
            var registry = InstructionHandlerRegistry.CreateDefault();
            AutoFlow.Core.Skills.SkillRegistry.Instance.ResetForRun();
            TaskStatus = taskName + " · 运行中";
            gameTask?.Module.SetRunStatus("运行中");
            enteredLoop = true;
            Log("=== 开始运行 " + taskName + " ===");
            if (ruleScript is not null)
                await Task.Run(() => new RuleLoopInterpreter(registry, conditions, blocks).ExecuteAsync(ruleScript, context, ct), ct);
            else
                await Task.Run(() => new ScriptInterpreter(registry, conditions, blocks).ExecuteAsync(document!, context, ct), ct);
            TaskStatus = taskName + " · 已完成";
            gameTask?.Module.SetRunStatus("已完成");
        }
        catch (OperationCanceledException)
        {
            TaskStatus = taskName + " · 已停止";
            gameTask?.Module.SetRunStatus("已停止");
        }
        catch (Exception ex)
        {
            TaskStatus = taskName + " · 失败：" + ex.Message;
            gameTask?.Module.SetRunStatus("失败：" + ex.Message);
        }
        finally
        {
            try
            {
                if (newCapture && !enteredLoop) StopCapture();
                Log(TaskStatus);
            }
            finally
            {
                cancellation.Dispose();
                _runCts = null;
                _executionGate.Exit();
                IsRunning = false;
            }
        }
    }
}
