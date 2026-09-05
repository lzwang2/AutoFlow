using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AutoFlow.App.Common;
using AutoFlow.App.Services;
using AutoFlow.App.Views;
using AutoFlow.Core.Capture;
using AutoFlow.Core.Input;
using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Handlers;
using AutoFlow.Core.Scripting.Instructions;
using AutoFlow.Core.Scripting.Rules;
using AutoFlow.Core.Vision;
using Fischless.GameCapture;
using Fischless.WindowsInput;
using Microsoft.Win32;

namespace AutoFlow.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly CaptureService _captureService = new();
    private readonly ScriptDocumentService _documentService = new();
    private readonly RuleScriptService _ruleService = new();
    private readonly RuleTemplateService _templateService = new();
    private readonly ConditionLibrary _conditionLibrary = new();
    private readonly ActionBlockLibrary _actionBlockLibrary = new();
    private readonly ScriptTemplateLibrary _scriptTemplateLibrary = new();
    private readonly TemplateIndex _templateIndex = new();
    private readonly CoordinateTransformService _coordinateTransform = new();
    private readonly WindowInfoService _windowInfoService = new();
    private readonly WindowTargetProfileService _profileService = new();
    private readonly DispatcherTimer _previewTimer;

    /// <summary>
    /// The on-disk log. Created before anything else runs so startup lines — library loading,
    /// condition warnings — land in the file rather than being lost before it opens.
    /// </summary>
    private readonly SessionLogWriter _logWriter = new();

    private CancellationTokenSource? _runCts;
    private string? _currentFilePath;
    private bool _suppressProfileSave;

    public MainViewModel()
    {
        Windows = new ObservableCollection<WindowOption>();
        Steps = new ObservableCollection<StepViewModel>();
        Rules = new ObservableCollection<RuleViewModel>();
        Rules.CollectionChanged += (_, _) => CommandManagerRefresh();
        AvailableConditions = new ObservableCollection<ConditionViewModel>();
        AvailableActionBlocks = new ObservableCollection<ActionBlock>();
        TemplateEntries = new ObservableCollection<TemplateIndexEntry>();
        TemplateKindFilters = new ObservableCollection<string>(TemplateKindFilterOptions);
        CaptureModeNames = new ObservableCollection<CaptureModes>(
            Enum.GetValues<CaptureModes>());
        SelectedCaptureMode = CaptureModes.WindowsGraphicsCapture;

        LogLines = new ObservableCollection<string>();

        RefreshWindowsCommand = new RelayCommand(RefreshWindows);
        StartCaptureCommand = new RelayCommand(StartCapture, () => SelectedWindow != null && !IsCapturing);
        StopCaptureCommand = new RelayCommand(StopCapture, () => IsCapturing);

        AddClickCommand = new RelayCommand(() => AddInstruction(CommandType.Click));
        AddWaitImageCommand = new RelayCommand(() => AddInstruction(CommandType.WaitImage));
        AddWaitColorCommand = new RelayCommand(() => AddInstruction(CommandType.WaitColor));
        AddWaitTimeCommand = new RelayCommand(() => AddInstruction(CommandType.WaitTime));
        AddLoopCommand = new RelayCommand(() => AddInstruction(CommandType.Loop));
        AddCommentCommand = new RelayCommand(() => AddInstruction(CommandType.Comment));

        RemoveInstructionCommand = new RelayCommand(RemoveSelectedInstruction, () => SelectedStep != null);
        MoveUpCommand = new RelayCommand(MoveInstructionUp, () => CanMoveUp);
        MoveDownCommand = new RelayCommand(MoveInstructionDown, () => CanMoveDown);

        // Reuse from the shared libraries: insert a block reference, or point a
        // condition-capable step at a shared condition.
        InsertActionBlockCommand = new RelayCommand(InsertActionBlock, () => AvailableActionBlocks.Count > 0);
        ClearStepConditionRefCommand = new RelayCommand(
            ClearStepConditionRef, () => SelectedStep?.ConditionRef != null);
        OpenLinearTemplateCommand = new RelayCommand(OpenLinearTemplate);
        SaveLinearAsTemplateCommand = new RelayCommand(SaveLinearAsTemplate, () => Steps.Count > 0);

        // Template library browsing
        RefreshTemplateLibraryCommand = new RelayCommand(RefreshTemplateLibrary);
        RevealSelectedTemplateCommand = new RelayCommand(RevealSelectedTemplate, () => SelectedTemplateEntry != null);
        OpenLogFolderCommand = new RelayCommand(OpenLogFolder);

        NewScriptCommand = new RelayCommand(NewScript);
        OpenScriptCommand = new RelayCommand(OpenScript);
        SaveScriptCommand = new RelayCommand(SaveScript);
        SaveAsScriptCommand = new RelayCommand(SaveScriptAs);

        RunScriptCommand = new RelayCommand(async () => await RunScriptAsync(),
            () => !IsRunning && (IsLinearMode ? Steps.Count > 0 : Rules.Count > 0));
        StopScriptCommand = new RelayCommand(StopScript, () => IsRunning);

        PickCoordinateCommand = new RelayCommand(() => IsPickingCoordinate = true,
            () => IsLinearMode && SelectedInstruction != null && IsCapturing);
        StartRegionSelectCommand = new RelayCommand(StartRegionSelect, () => CanStartRegionSelect);
        ConfirmRegionCommand = new RelayCommand(ConfirmRegion, () => IsConfirmingRegion);
        ReselectRegionCommand = new RelayCommand(ReselectRegion, () => IsConfirmingRegion);

        // Rule commands
        AddRuleCommand = new RelayCommand(AddRule);
        RemoveRuleCommand = new RelayCommand(RemoveSelectedRule, () => SelectedRule != null);
        MoveRuleUpCommand = new RelayCommand(MoveRuleUp, () => CanMoveRuleUp);
        MoveRuleDownCommand = new RelayCommand(MoveRuleDown, () => CanMoveRuleDown);
        AddClickActionCommand = new RelayCommand(() => AddAction(CommandType.Click), () => SelectedRule != null);
        AddWaitTimeActionCommand = new RelayCommand(() => AddAction(CommandType.WaitTime), () => SelectedRule != null);
        AddActionBlockToRuleCommand = new RelayCommand(AddActionBlockToRule,
            () => SelectedRule != null && SelectedActionBlock != null);
        RemoveActionCommand = new RelayCommand(RemoveSelectedAction, () => SelectedActionStep != null);
        MoveActionUpCommand = new RelayCommand(MoveActionUp, () => CanMoveActionUp);
        MoveActionDownCommand = new RelayCommand(MoveActionDown, () => CanMoveActionDown);
        PickActionCoordinateCommand = new RelayCommand(() => IsPickingCoordinate = true,
            () => IsRuleMode && SelectedAction?.CommandType == CommandType.Click && IsCapturing);

        // Rule template commands
        SaveRuleAsTemplateCommand = new RelayCommand(SaveRuleAsTemplate, () => SelectedRule != null);
        ImportRuleFromTemplateCommand = new RelayCommand(ImportRuleFromTemplate);
        AddGameCommand = new RelayCommand(AddGame);

        // Shared condition commands. Editing lives in the condition library tab; the editors can
        // only reference, so what is left here is clearing a reference.
        ClearConditionRefCommand = new RelayCommand(ClearConditionRef, () => SelectedRule != null);
        InitializeLibraryCommands();

        // Window sizing commands
        ApplyWindowSizeCommand = new RelayCommand(ApplyWindowSizeNow, () => SelectedWindow != null);
        UseReferenceAsWindowSizeCommand = new RelayCommand(UseReferenceAsWindowSize);

        // Debug commands
        BrowseFrameDebugPathCommand = new RelayCommand(BrowseFrameDebugPath);

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _previewTimer.Tick += (_, _) => UpdatePreview();

        LoadWindowSizeSettings();
        RefreshWindows();
        LoadGames();
    }

    // === Collections ===
    public ObservableCollection<WindowOption> Windows { get; }
    public ObservableCollection<CaptureModes> CaptureModeNames { get; }
    /// <summary>
    /// The linear editor's step list. A step is an action or a reference to a shared action
    /// block, which is why this is not a plain instruction list any more — an expanded copy
    /// would lose the reference on the next save.
    /// </summary>
    public ObservableCollection<StepViewModel> Steps { get; }

    public ObservableCollection<RuleViewModel> Rules { get; }
    public ObservableCollection<string> LogLines { get; }
    public ObservableCollection<string> Games { get; } = new();

    /// <summary>Action blocks available for insertion, from the selected game's library.</summary>
    public ObservableCollection<ActionBlock> AvailableActionBlocks { get; }

    /// <summary>Every template in the selected game, all four kinds, for the library browser.</summary>
    public ObservableCollection<TemplateIndexEntry> TemplateEntries { get; }

    public ObservableCollection<string> TemplateKindFilters { get; }

    private string _logText = string.Empty;
    public string LogText
    {
        get => _logText;
        private set => SetProperty(ref _logText, value);
    }

    /// <summary>This session's log file, shown in the debug tab so it can be found without the button.</summary>
    public string LogFilePath => _logWriter.FilePath;

    // === Commands ===
    public RelayCommand RefreshWindowsCommand { get; }
    public RelayCommand StartCaptureCommand { get; }
    public RelayCommand StopCaptureCommand { get; }
    public RelayCommand AddClickCommand { get; }
    public RelayCommand AddWaitImageCommand { get; }
    public RelayCommand AddWaitColorCommand { get; }
    public RelayCommand AddWaitTimeCommand { get; }
    public RelayCommand AddLoopCommand { get; }
    public RelayCommand AddCommentCommand { get; }
    public RelayCommand RemoveInstructionCommand { get; }
    public RelayCommand MoveUpCommand { get; }
    public RelayCommand MoveDownCommand { get; }
    public RelayCommand InsertActionBlockCommand { get; }
    public RelayCommand ClearStepConditionRefCommand { get; }
    public RelayCommand OpenLinearTemplateCommand { get; }
    public RelayCommand SaveLinearAsTemplateCommand { get; }
    public RelayCommand RefreshTemplateLibraryCommand { get; }
    public RelayCommand RevealSelectedTemplateCommand { get; }
    public RelayCommand OpenLogFolderCommand { get; }
    public RelayCommand NewScriptCommand { get; }
    public RelayCommand OpenScriptCommand { get; }
    public RelayCommand SaveScriptCommand { get; }
    public RelayCommand SaveAsScriptCommand { get; }
    public RelayCommand RunScriptCommand { get; }
    public RelayCommand StopScriptCommand { get; }
    public RelayCommand PickCoordinateCommand { get; }
    public RelayCommand StartRegionSelectCommand { get; }
    public RelayCommand ConfirmRegionCommand { get; }
    public RelayCommand ReselectRegionCommand { get; }
    public RelayCommand AddRuleCommand { get; }
    public RelayCommand RemoveRuleCommand { get; }
    public RelayCommand MoveRuleUpCommand { get; }
    public RelayCommand MoveRuleDownCommand { get; }
    public RelayCommand AddClickActionCommand { get; }
    public RelayCommand AddWaitTimeActionCommand { get; }
    public RelayCommand AddActionBlockToRuleCommand { get; }
    public RelayCommand RemoveActionCommand { get; }
    public RelayCommand MoveActionUpCommand { get; }
    public RelayCommand MoveActionDownCommand { get; }
    public RelayCommand PickActionCoordinateCommand { get; }
    public RelayCommand SaveRuleAsTemplateCommand { get; }
    public RelayCommand ImportRuleFromTemplateCommand { get; }
    public RelayCommand AddGameCommand { get; }
    public RelayCommand ClearConditionRefCommand { get; }
    public RelayCommand ApplyWindowSizeCommand { get; }
    public RelayCommand UseReferenceAsWindowSizeCommand { get; }
    public RelayCommand BrowseFrameDebugPathCommand { get; }

    // === Bindable properties ===
    private WindowOption? _selectedWindow;
    public WindowOption? SelectedWindow
    {
        get => _selectedWindow;
        set
        {
            if (!SetProperty(ref _selectedWindow, value)) return;
            if (value is not null)
            {
                LoadWindowSizeSettings(value.Title);
                AutoMeasureClientSize(value.Handle);
            }
            CommandManagerRefresh();
        }
    }

    private CaptureModes _selectedCaptureMode;
    public CaptureModes SelectedCaptureMode
    {
        get => _selectedCaptureMode;
        set => SetProperty(ref _selectedCaptureMode, value);
    }

    private bool _isCapturing;
    public bool IsCapturing
    {
        get => _isCapturing;
        private set { if (SetProperty(ref _isCapturing, value)) CommandManagerRefresh(); }
    }

    private BitmapSource? _previewImage;
    public BitmapSource? PreviewImage
    {
        get => _previewImage;
        private set => SetProperty(ref _previewImage, value);
    }

    private StepViewModel? _selectedStep;

    /// <summary>The selected row of the linear step list, action or block reference.</summary>
    public StepViewModel? SelectedStep
    {
        get => _selectedStep;
        set
        {
            if (!SetProperty(ref _selectedStep, value))
                return;

            OnPropertyChanged(nameof(SelectedInstruction));
            SyncConditionSelectionToStep();
            CommandManagerRefresh();
        }
    }

    /// <summary>
    /// The selected step's action, or null when a block reference is selected. The property
    /// editor binds here, so selecting a block reference disables the per-action fields rather
    /// than editing the block's first step by accident.
    /// </summary>
    public InstructionViewModel? SelectedInstruction => _selectedStep?.Action;

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set { if (SetProperty(ref _isRunning, value)) CommandManagerRefresh(); }
    }

    private bool _isPickingCoordinate;
    public bool IsPickingCoordinate
    {
        get => _isPickingCoordinate;
        set { if (SetProperty(ref _isPickingCoordinate, value)) { OnPropertyChanged(nameof(StatusHint)); } }
    }

    private bool _isSelectingRegion;
    public bool IsSelectingRegion
    {
        get => _isSelectingRegion;
        set { if (SetProperty(ref _isSelectingRegion, value)) { OnPropertyChanged(nameof(StatusHint)); CommandManagerRefresh(); } }
    }

    private bool _isConfirmingRegion;
    public bool IsConfirmingRegion
    {
        get => _isConfirmingRegion;
        set { if (SetProperty(ref _isConfirmingRegion, value)) { OnPropertyChanged(nameof(StatusHint)); CommandManagerRefresh(); } }
    }

    private BitmapSource? _pendingTemplatePreview;
    public BitmapSource? PendingTemplatePreview
    {
        get => _pendingTemplatePreview;
        private set => SetProperty(ref _pendingTemplatePreview, value);
    }

    // TemplateName and TemplateDirectory are gone. They were the "where do I put this crop"
    // pair from when either editor could capture its own image; a condition's image now always
    // goes to <TemplateRoot>/<Game>/images/ so the stored path stays library-relative and
    // resolves on another machine. The per-capture file name lives on as PendingImageName in
    // MainViewModel.Libraries.cs.

    public string StatusHint => IsConfirmingRegion
        ? "请确认截图: 确认保存 / 重选 (ESC 取消)"
        : IsSelectingRegion
            ? "框选模式: 点击左上角 → 点击右下角 (ESC 取消)"
            : IsPickingCoordinate
                ? "点击预览画面以拾取坐标..."
                : IsCapturing ? "捕获中" : "未捕获";

    private string _scriptName = "未命名脚本";
    public string ScriptName
    {
        get => _scriptName;
        set => SetProperty(ref _scriptName, value);
    }

    private int _selectedTabIndex;
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (SetProperty(ref _selectedTabIndex, value))
            {
                OnPropertyChanged(nameof(IsLinearMode));
                OnPropertyChanged(nameof(IsRuleMode));
                OnPropertyChanged(nameof(IsConditionMode));
                OnPropertyChanged(nameof(IsActionBlockMode));

                // The condition picker follows the active tab's selection, so switching tabs
                // has to re-point it — otherwise it would still show (and edit) the other
                // editor's reference.
                if (IsLinearMode) SyncConditionSelectionToStep();
                else if (IsRuleMode) SyncConditionSelectionToRule();

                CommandManagerRefresh();
            }
        }
    }

    // Tab indices, named because several behaviours branch on the active tab and bare 0/1/2
    // scattered through the file silently break the moment a tab is inserted.
    public const int LinearTabIndex = 0;
    public const int RuleTabIndex = 1;
    public const int ConditionTabIndex = 2;
    public const int ActionBlockTabIndex = 3;

    public bool IsLinearMode => SelectedTabIndex == LinearTabIndex;
    public bool IsRuleMode => SelectedTabIndex == RuleTabIndex;

    /// <summary>
    /// The condition library tab is where a condition's image, region and thresholds are edited.
    /// The two script editors can only reference conditions now — a shared object with two edit
    /// paths is a shared object nobody can reason about.
    /// </summary>
    public bool IsConditionMode => SelectedTabIndex == ConditionTabIndex;

    public bool IsActionBlockMode => SelectedTabIndex == ActionBlockTabIndex;

    // === Window sizing / reference resolution ===
    // ResizeWidth/Height are the OUTER window rect (same semantics as AutoHotkey WinMove).
    // ScriptReferenceWidth/Height are the coordinate space the script's coordinates and
    // templates live in; they are saved into the script file, not the window profile.
    private bool _resizeWindowBeforeRun;
    public bool ResizeWindowBeforeRun
    {
        get => _resizeWindowBeforeRun;
        set { if (SetProperty(ref _resizeWindowBeforeRun, value)) SaveWindowSizeSettings(); }
    }

    private int _resizeWidth = 750;
    public int ResizeWidth
    {
        get => _resizeWidth;
        set { if (SetProperty(ref _resizeWidth, value)) SaveWindowSizeSettings(); }
    }

    private int _resizeHeight = 1040;
    public int ResizeHeight
    {
        get => _resizeHeight;
        set { if (SetProperty(ref _resizeHeight, value)) SaveWindowSizeSettings(); }
    }

    private int _scriptReferenceWidth = CoordinateTransformService.DefaultReferenceWidth;
    public int ScriptReferenceWidth
    {
        get => _scriptReferenceWidth;
        set { if (SetProperty(ref _scriptReferenceWidth, value)) SaveWindowSizeSettings(); }
    }

    private int _scriptReferenceHeight = CoordinateTransformService.DefaultReferenceHeight;
    public int ScriptReferenceHeight
    {
        get => _scriptReferenceHeight;
        set { if (SetProperty(ref _scriptReferenceHeight, value)) SaveWindowSizeSettings(); }
    }

    private string _measuredClientSize = "未测量";
    public string MeasuredClientSize
    {
        get => _measuredClientSize;
        private set => SetProperty(ref _measuredClientSize, value);
    }

    private int _previewFrameWidth;
    private int _previewFrameHeight;

    public bool CanMoveUp => SelectedStep != null && Steps.IndexOf(SelectedStep) > 0;
    public bool CanMoveDown => SelectedStep != null && Steps.IndexOf(SelectedStep) < Steps.Count - 1;

    // === Rule editor state ===
    private RuleViewModel? _selectedRule;
    public RuleViewModel? SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (SetProperty(ref _selectedRule, value))
            {
                SelectedActionStep = null;
                OnPropertyChanged(nameof(SelectedRuleActions));
                SyncConditionSelectionToRule();
                CommandManagerRefresh();
            }
        }
    }

    public ObservableCollection<StepViewModel>? SelectedRuleActions => SelectedRule?.Actions;

    // === Shared condition editor state ===

    /// <summary>
    /// Conditions available for reference, for the rule editor's picker. Rebuilt whenever the
    /// library reloads rather than bound to the library directly, because the library is a
    /// plain dictionary with no change notification.
    /// </summary>
    public ObservableCollection<ConditionViewModel> AvailableConditions { get; }

    private ConditionViewModel? _selectedCondition;

    /// <summary>
    /// The condition currently shown in the condition panel. Setting it repoints whatever the
    /// active editor has selected — picking a condition IS the act of referencing it, so a
    /// separate "apply" step would only add a way to forget.
    ///
    /// <para>The target depends on the tab: the selected rule in the rule editor, the selected
    /// step in the linear editor. One picker serving both is what makes the same condition
    /// object reusable from either (FR-06).</para>
    /// </summary>
    public ConditionViewModel? SelectedCondition
    {
        get => _selectedCondition;
        set
        {
            if (!SetProperty(ref _selectedCondition, value))
                return;

            if (!_syncingConditionSelection && ActiveConditionConsumer is { } consumer)
            {
                consumer.ConditionRef = value?.Id;
                consumer.RefreshConditionView();
            }

            OnPropertyChanged(nameof(ReferenceSpaceWarning));
            OnPropertyChanged(nameof(HasReferenceSpaceWarning));
            CommandManagerRefresh();
        }
    }

    /// <summary>
    /// What a condition pick applies to right now. Null in the linear editor when the selected
    /// step cannot use a condition (a WaitTime, or a block reference), so picking one there
    /// changes nothing rather than writing a reference the loader would reject.
    ///
    /// <para>Null on every tab that is not one of the two script editors. This used to be
    /// <c>IsLinearMode ? step : SelectedRule</c>, which made "not the linear tab" mean "the rule
    /// tab" — so once the library tabs existed, browsing conditions there would have silently
    /// repointed whatever rule happened to be selected.</para>
    /// </summary>
    private IConditionConsumer? ActiveConditionConsumer =>
        IsLinearMode ? (SelectedStep?.SupportsCondition == true ? SelectedStep : null)
        : IsRuleMode ? SelectedRule
        : null;

    // Guards against the selection → picker sync being mistaken for the user picking a
    // condition, which would otherwise rewrite a ConditionRef while merely changing selection.
    private bool _syncingConditionSelection;

    private void SyncConditionSelectionToRule() => SyncConditionSelectionTo(SelectedRule);

    private void SyncConditionSelectionToStep() =>
        SyncConditionSelectionTo(SelectedStep?.SupportsCondition == true ? SelectedStep : null);

    private void SyncConditionSelectionTo(IConditionConsumer? consumer)
    {
        _syncingConditionSelection = true;
        try
        {
            var conditionRef = consumer?.ConditionRef;
            SelectedCondition = string.IsNullOrWhiteSpace(conditionRef)
                ? null
                : AvailableConditions.FirstOrDefault(
                    c => string.Equals(c.Id, conditionRef, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _syncingConditionSelection = false;
        }
    }

    /// <summary>
    /// Warns when the selected condition's region coordinates were authored against a
    /// different window size than the current script declares (AC-07). Surfaced in the editor
    /// because the coordinates look plausible but match the wrong place at runtime.
    /// </summary>
    public string? ReferenceSpaceWarning
    {
        get
        {
            if (SelectedCondition is null)
                return null;

            // ResizeWidth/Height is the script's declared outer size: ApplyScriptOuterSize
            // writes the opened script's OuterWidth/Height into them, and saving writes them
            // back out, so they are the same number the interpreter will check against.
            var check = ReferenceSpaceValidator.Check(
                SelectedCondition.Model, ResizeWidth, ResizeHeight);
            return check.IsOk ? null : check.Message;
        }
    }

    public bool HasReferenceSpaceWarning => ReferenceSpaceWarning is not null;

    // === Action block reuse (linear editor) ===

    private ActionBlock? _selectedActionBlock;

    /// <summary>The block the "insert block" button will insert.</summary>
    public ActionBlock? SelectedActionBlock
    {
        get => _selectedActionBlock;
        set { if (SetProperty(ref _selectedActionBlock, value)) CommandManagerRefresh(); }
    }

    // === Template library browser ===

    /// <summary>
    /// The filter values, in the order the combo box shows them. "全部" first because browsing
    /// everything is the common case; the rest map onto <see cref="TemplateKind"/>.
    /// </summary>
    private static readonly string[] TemplateKindFilterOptions =
        ["全部", "条件", "动作块", "规则", "线性脚本"];

    private static TemplateKind? KindForFilter(string? filter) => filter switch
    {
        "条件" => TemplateKind.Condition,
        "动作块" => TemplateKind.ActionBlock,
        "规则" => TemplateKind.Rule,
        "线性脚本" => TemplateKind.LinearScript,
        _ => null
    };

    private string _selectedTemplateKindFilter = TemplateKindFilterOptions[0];
    public string SelectedTemplateKindFilter
    {
        get => _selectedTemplateKindFilter;
        set { if (SetProperty(ref _selectedTemplateKindFilter, value)) RefreshTemplateLibrary(); }
    }

    private TemplateIndexEntry? _selectedTemplateEntry;
    public TemplateIndexEntry? SelectedTemplateEntry
    {
        get => _selectedTemplateEntry;
        set
        {
            if (SetProperty(ref _selectedTemplateEntry, value))
            {
                OnPropertyChanged(nameof(SelectedTemplateHint));
                CommandManagerRefresh();
            }
        }
    }

    /// <summary>
    /// Where the selected entry is actually used from. This tab is now browse-only, so the hint
    /// points at the editor control that consumes each kind — otherwise a user who found a
    /// condition here has no way to know it is applied from the rule editor's reference picker.
    /// </summary>
    public string SelectedTemplateHint => SelectedTemplateEntry?.Kind switch
    {
        TemplateKind.Condition => "共享条件：在规则编辑器 / 线性编辑器的「引用条件」下拉框中选用",
        TemplateKind.ActionBlock => "共享动作块：在线性编辑器的「从动作模板库插入」中选用",
        TemplateKind.Rule => "规则模板：在规则编辑器点「📥 从库导入规则」导入",
        TemplateKind.LinearScript => "线性脚本模板：在线性编辑器点「📂 打开线性模板」打开",
        _ => "选择一个模板查看它的来源文件"
    };

    private StepViewModel? _selectedActionStep;

    /// <summary>The selected row of the rule's response-action list.</summary>
    public StepViewModel? SelectedActionStep
    {
        get => _selectedActionStep;
        set
        {
            if (!SetProperty(ref _selectedActionStep, value))
                return;

            OnPropertyChanged(nameof(SelectedAction));
            CommandManagerRefresh();
        }
    }

    /// <summary>
    /// The selected response action's instruction, or null when a block reference is selected —
    /// same reason as <see cref="SelectedInstruction"/>: a block's steps live in the block, and
    /// the per-action fields must not silently edit them.
    /// </summary>
    public InstructionViewModel? SelectedAction => _selectedActionStep?.Action;

    public bool CanMoveRuleUp => SelectedRule != null && Rules.IndexOf(SelectedRule) > 0;
    public bool CanMoveRuleDown => SelectedRule != null && Rules.IndexOf(SelectedRule) < Rules.Count - 1;
    public bool CanMoveActionUp => SelectedRule != null && SelectedActionStep != null
        && SelectedRule.Actions.IndexOf(SelectedActionStep) > 0;

    public bool CanMoveActionDown => SelectedRule != null && SelectedActionStep != null
        && SelectedRule.Actions.IndexOf(SelectedActionStep) < SelectedRule.Actions.Count - 1;

    // === Debug settings ===
    private bool _enableFrameDebug;
    public bool EnableFrameDebug
    {
        get => _enableFrameDebug;
        set => SetProperty(ref _enableFrameDebug, value);
    }

    private int _frameDebugMode = 0; // 0=first only, 1=matched only, 2=all
    public int FrameDebugMode
    {
        get => _frameDebugMode;
        set => SetProperty(ref _frameDebugMode, value);
    }

    private string _frameDebugPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "FrameDebug");
    public string FrameDebugPath
    {
        get => _frameDebugPath;
        set => SetProperty(ref _frameDebugPath, value);
    }

    // === Game selection ===
    private string _selectedGame = "LastCloudia";
    public string SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (SetProperty(ref _selectedGame, value))
            {
                LoadSharedLibraries();
            }
        }
    }

    /// <summary>
    /// Loads the selected game's shared conditions and action blocks. Warnings are logged
    /// rather than raised: a single malformed template file must not stop the editor from
    /// opening, and the log is where the user will look for why a reference failed to resolve.
    /// </summary>
    private void LoadSharedLibraries()
    {
        try
        {
            // Cleared first because LoadGame accumulates. Without this, calling it twice for the
            // same game reported every condition as a duplicate of itself (a flood of
            // "条件 Id 重复" lines naming one file twice), a game switch left the previous game's
            // conditions in the pickers, and a condition deleted from the library stayed
            // resolvable until restart.
            //
            // The cost is that unsaved condition edits are dropped on reload. Acceptable because
            // the condition editor saves explicitly and every caller here either just wrote to
            // disk or is deliberately switching context.
            _conditionLibrary.Clear();
            _actionBlockLibrary.Clear();

            foreach (var warning in _conditionLibrary.LoadGame(SelectedGame))
            {
                Log($"[条件库] {warning}");
            }

            foreach (var warning in _actionBlockLibrary.LoadGame(SelectedGame))
            {
                Log($"[动作块库] {warning}");
            }
        }
        catch (Exception ex)
        {
            Log($"加载共享库失败 ({SelectedGame}): {ex.Message}");
        }

        RebuildAvailableConditions();
        RebuildAvailableActionBlocks();
        RebuildLibraryTabs();
        RefreshTemplateLibrary();
    }

    /// <summary>
    /// Rebuilds the condition picker from the library and re-resolves what every rule and step
    /// points at. Called after a library load, because switching games can make a previously
    /// valid reference dangling — the lists must show that, not keep displaying the old game's
    /// condition.
    /// </summary>
    private void RebuildAvailableConditions()
    {
        var previousId = SelectedCondition?.Id;

        AvailableConditions.Clear();
        foreach (var condition in _conditionLibrary.All.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            AvailableConditions.Add(new ConditionViewModel(condition));
        }

        foreach (var rule in Rules)
        {
            rule.RefreshConditionView();
        }

        foreach (var step in Steps)
        {
            step.RefreshConditionView();
        }

        _syncingConditionSelection = true;
        try
        {
            SelectedCondition = previousId is null
                ? null
                : AvailableConditions.FirstOrDefault(
                    c => string.Equals(c.Id, previousId, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _syncingConditionSelection = false;
        }

        OnPropertyChanged(nameof(ReferenceSpaceWarning));
        OnPropertyChanged(nameof(HasReferenceSpaceWarning));
    }

    /// <summary>
    /// Rebuilds the action-block picker and re-resolves every block reference in the step list.
    /// A step keeps its Id across a game switch, so a step that referenced a block from the old
    /// game must now render as a broken reference rather than silently resolving to nothing.
    /// </summary>
    private void RebuildAvailableActionBlocks()
    {
        var previousId = SelectedActionBlock?.Id;

        AvailableActionBlocks.Clear();
        foreach (var block in _actionBlockLibrary.All.OrderBy(b => b.Id, StringComparer.Ordinal))
        {
            AvailableActionBlocks.Add(block);
        }

        SelectedActionBlock = previousId is null
            ? AvailableActionBlocks.FirstOrDefault()
            : AvailableActionBlocks.FirstOrDefault(
                  b => string.Equals(b.Id, previousId, StringComparison.OrdinalIgnoreCase))
              ?? AvailableActionBlocks.FirstOrDefault();

        foreach (var step in Steps)
        {
            step.RefreshConditionView();
        }
    }

    private static void CommandManagerRefresh() =>
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();

    // === Window capture ===
    private void RefreshWindows()
    {
        var current = SelectedWindow?.Handle;
        Windows.Clear();
        foreach (var (handle, title) in NativeWindowHelper.EnumerateVisibleWindows())
        {
            Windows.Add(new WindowOption(handle, title));
        }
        if (current.HasValue)
        {
            SelectedWindow = Windows.FirstOrDefault(w => w.Handle == current.Value);
        }
    }

    private void StartCapture()
    {
        if (SelectedWindow is null) return;

        try
        {
            _captureService.Start(SelectedWindow.Handle, SelectedCaptureMode);
            _windowInfoService.SetTargetWindowTitle(SelectedWindow.Title);
            IsCapturing = true;
            OnPropertyChanged(nameof(StatusHint));
            _previewTimer.Start();
            Log($"开始捕获: {SelectedWindow.Title}");
        }
        catch (Exception ex)
        {
            Log($"捕获失败: {ex.Message}");
            MessageBox.Show($"捕获失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopCapture()
    {
        _previewTimer.Stop();
        _captureService.Stop();
        IsCapturing = false;
        IsPickingCoordinate = false;
        OnPropertyChanged(nameof(StatusHint));
        Log("停止捕获");
    }

    // === Window sizing persistence ===
    // Settings are stored per target-window title, plus a "last used" fallback for when no
    // window is selected yet, so the size survives across sessions without reconfiguring.
    private const string LastUsedProfileName = "__last_used__";

    private void LoadWindowSizeSettings(string? windowTitle = null)
    {
        try
        {
            var profiles = _profileService.Load();
            var profile = windowTitle is null
                ? null
                : profiles.FirstOrDefault(p =>
                    string.Equals(p.WindowTitle, windowTitle, StringComparison.OrdinalIgnoreCase));
            profile ??= profiles.FirstOrDefault(p =>
                string.Equals(p.Name, LastUsedProfileName, StringComparison.OrdinalIgnoreCase));
            if (profile is null) return;

            _suppressProfileSave = true;
            ResizeWindowBeforeRun = profile.ResizeBeforeRun;
            ResizeWidth = profile.ResizeWidth;
            ResizeHeight = profile.ResizeHeight;
            ScriptReferenceWidth = profile.ReferenceWidth;
            ScriptReferenceHeight = profile.ReferenceHeight;
        }
        catch (Exception ex)
        {
            Log($"读取窗口尺寸设置失败: {ex.Message}");
        }
        finally
        {
            _suppressProfileSave = false;
        }
    }

    private void SaveWindowSizeSettings()
    {
        if (_suppressProfileSave) return;

        try
        {
            var profile = BuildWindowProfile(LastUsedProfileName);
            _profileService.Upsert(profile);

            var title = SelectedWindow?.Title;
            if (!string.IsNullOrWhiteSpace(title))
            {
                _profileService.Upsert(BuildWindowProfile(title));
            }
        }
        catch (Exception ex)
        {
            Log($"保存窗口尺寸设置失败: {ex.Message}");
        }
    }

    private WindowTargetProfile BuildWindowProfile(string name) => new()
    {
        Name = name,
        WindowTitle = SelectedWindow?.Title ?? string.Empty,
        ResizeBeforeRun = ResizeWindowBeforeRun,
        ResizeWidth = ResizeWidth,
        ResizeHeight = ResizeHeight,
        ReferenceWidth = ScriptReferenceWidth,
        ReferenceHeight = ScriptReferenceHeight
    };

    /// <summary>
    /// Resize the selected window right now and report the resulting client size. Useful for
    /// checking what client area a given outer size actually yields before running a script.
    /// </summary>
    private void ApplyWindowSizeNow()
    {
        if (SelectedWindow is null) return;

        var profile = BuildWindowProfile(LastUsedProfileName);
        profile.ResizeBeforeRun = true; // explicit user action overrides the checkbox
        if (!_windowInfoService.TryApplyResize(SelectedWindow.Handle, profile, out var client, out var reason))
        {
            // A button that reports nothing when pressed reads as a broken button, so the
            // reason is surfaced in a dialog as well as the log.
            Log($"调整窗口尺寸失败: {SelectedWindow.Title} — {reason}");
            MessageBox.Show(
                $"调整窗口尺寸失败 ({ResizeWidth}×{ResizeHeight})。\n\n原因: {reason}",
                "调整窗口尺寸", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MeasuredClientSize = $"{client.Width}×{client.Height}";

        // Automatically set the measured client area as the script's reference resolution.
        // This ensures coordinates and templates work immediately after resize without
        // requiring a separate "use measured size" button click.
        ScriptReferenceWidth = client.Width;
        ScriptReferenceHeight = client.Height;

        Log($"已调整窗口外框到 {ResizeWidth}×{ResizeHeight}，实测客户区 {client.Width}×{client.Height}，参考分辨率已自动设置");
    }

    /// <summary>Copy the last measured client size into the script's reference resolution.</summary>
    private void UseReferenceAsWindowSize()
    {
        if (SelectedWindow is null ||
            !NativeWindowHelper.TryGetClientBounds(SelectedWindow.Handle, out var client) ||
            client.Width <= 0 || client.Height <= 0)
        {
            MessageBox.Show("请先选择目标窗口并调整尺寸。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ScriptReferenceWidth = client.Width;
        ScriptReferenceHeight = client.Height;
        MeasuredClientSize = $"{client.Width}×{client.Height}";
        Log($"参考分辨率已设为实测客户区 {client.Width}×{client.Height}");
    }

    /// <summary>
    /// Automatically measure the current client size of the window and apply it to the
    /// script's reference resolution. Called when a window is selected to reduce manual steps.
    /// </summary>
    private void AutoMeasureClientSize(IntPtr windowHandle)
    {
        if (!NativeWindowHelper.TryGetClientBounds(windowHandle, out var client) ||
            client.Width <= 0 || client.Height <= 0)
        {
            return; // Silent fail - don't spam the user when switching windows
        }

        ScriptReferenceWidth = client.Width;
        ScriptReferenceHeight = client.Height;
        MeasuredClientSize = $"{client.Width}×{client.Height}";
        Log($"已测量窗口客户区 {client.Width}×{client.Height}，参考分辨率已自动设置");
    }

    private bool _showPreview = false;
    public bool ShowPreview
    {
        get => _showPreview;
        set => SetProperty(ref _showPreview, value);
    }

    private void UpdatePreview()
    {
        if (!ShowPreview)
        {
            return;
        }

        var frame = _captureService.CaptureFrame();
        if (frame is null || frame.Empty())
        {
            return;
        }

        using (frame)
        {
            _previewFrameWidth = frame.Width;
            _previewFrameHeight = frame.Height;
            PreviewImage = MatImageConverter.ToBitmapSource(frame);
        }
    }

    /// <summary>
    /// Called by the view when the user clicks the preview image.
    /// normalizedX/Y are 0..1 relative to the displayed image.
    /// </summary>
    public void OnPreviewClicked(double normalizedX, double normalizedY)
    {
        if (!IsPickingCoordinate) return;

        // Frame pixel -> script coordinate (in the script's declared reference space)
        var scriptX = (int)Math.Round(normalizedX * ScriptReferenceWidth);
        var scriptY = (int)Math.Round(normalizedY * ScriptReferenceHeight);

        // Which editor the pick belongs to follows the active tab: both can hold a selection,
        // so testing nullness alone would write the coordinate into the wrong one.
        if (IsLinearMode && SelectedInstruction is not null)
        {
            SelectedInstruction.PositionX = scriptX;
            SelectedInstruction.PositionY = scriptY;

            // If picking for a WaitColor, also sample the pixel color
            if (SelectedInstruction.CommandType == CommandType.WaitColor && _previewFrameWidth > 0)
            {
                var px = (int)Math.Round(normalizedX * _previewFrameWidth);
                var py = (int)Math.Round(normalizedY * _previewFrameHeight);
                var frame = _captureService.CaptureFrame();
                if (frame is not null && !frame.Empty())
                {
                    using (frame)
                    {
                        px = Math.Clamp(px, 0, frame.Width - 1);
                        py = Math.Clamp(py, 0, frame.Height - 1);
                        var pixel = frame.At<OpenCvSharp.Vec3b>(py, px);
                        SelectedInstruction.WaitColorHex = $"#{pixel.Item2:X2}{pixel.Item1:X2}{pixel.Item0:X2}";
                    }
                }
            }

            IsPickingCoordinate = false;
            OnPropertyChanged(nameof(StatusHint));
            Log($"拾取坐标: ({scriptX}, {scriptY})");
        }
        // Picking for action coordinate (rule script)
        else if (IsRuleMode && SelectedAction is not null)
        {
            SelectedAction.PositionX = scriptX;
            SelectedAction.PositionY = scriptY;
            SelectedRule?.RefreshSummary();
            IsPickingCoordinate = false;
            OnPropertyChanged(nameof(StatusHint));
            Log($"拾取动作坐标: ({scriptX}, {scriptY})");
        }
    }

    // === Template region selection ===
    private (double X, double Y)? _regionFirstCorner;

    /// <summary>
    /// Whether framing a region on the preview would land somewhere.
    ///
    /// <para>Only the condition library tab qualifies. Both script editors used to be able to
    /// capture into an inline image of their own; they cannot any more, because an image
    /// condition is the most-reused object in a script library and two edit paths for one shared
    /// object means neither can be trusted. The editors reference conditions, the library owns
    /// them.</para>
    /// </summary>
    private bool CanStartRegionSelect =>
        IsCapturing && IsConditionMode && SelectedLibraryCondition is not null;

    private void StartRegionSelect()
    {
        // Gated on the active tab, not on which selection happens to be non-null: several tabs
        // can hold a selection at once, so the crop has to land in the one the user is looking at.
        if (!CanStartRegionSelect)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(PendingImageName))
        {
            MessageBox.Show("请先填写截图文件名。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _regionFirstCorner = null;
        IsSelectingRegion = true;
    }

    public void CancelRegionSelect()
    {
        if (!IsSelectingRegion)
        {
            return;
        }
        DiscardPendingRegion();
        _regionFirstCorner = null;
        IsSelectingRegion = false;
        IsConfirmingRegion = false;
        Log("已取消框选");
    }

    /// <summary>
    /// Called by the view during region selection when the user clicks the preview.
    /// normalizedX/Y are 0..1 relative to the displayed frame content.
    /// </summary>
    public void OnRegionClicked(double normalizedX, double normalizedY)
    {
        if (!IsSelectingRegion)
        {
            return;
        }

        if (_regionFirstCorner is null)
        {
            _regionFirstCorner = (normalizedX, normalizedY);
            Log("已记录左上角，请点击右下角");
            return;
        }

        var (x1, y1) = _regionFirstCorner.Value;
        _regionFirstCorner = null;
        IsSelectingRegion = false;
        PrepareRegionPreview(x1, y1, normalizedX, normalizedY);
    }

    // Cached crop pending confirmation
    private OpenCvSharp.Mat? _pendingCrop;
    private (int X1, int Y1, int X2, int Y2)? _pendingRegionScript;

    /// <summary>
    /// Crop the current frame into a pending template, show a thumbnail, and await confirmation.
    /// Does NOT write any file yet.
    /// </summary>
    private void PrepareRegionPreview(double nx1, double ny1, double nx2, double ny2)
    {
        var frame = _captureService.CaptureFrame();
        if (frame is null || frame.Empty())
        {
            MessageBox.Show("无法获取画面帧。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        using (frame)
        {
            var left = (int)Math.Round(Math.Min(nx1, nx2) * frame.Width);
            var top = (int)Math.Round(Math.Min(ny1, ny2) * frame.Height);
            var right = (int)Math.Round(Math.Max(nx1, nx2) * frame.Width);
            var bottom = (int)Math.Round(Math.Max(ny1, ny2) * frame.Height);

            left = Math.Clamp(left, 0, frame.Width - 1);
            top = Math.Clamp(top, 0, frame.Height - 1);
            right = Math.Clamp(right, left + 1, frame.Width);
            bottom = Math.Clamp(bottom, top + 1, frame.Height);

            var width = right - left;
            var height = bottom - top;
            if (width < 2 || height < 2)
            {
                MessageBox.Show("框选区域太小，请重新框选。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DiscardPendingRegion();
            using var roi = new OpenCvSharp.Mat(frame, new OpenCvSharp.Rect(left, top, width, height));
            _pendingCrop = roi.Clone();
            PendingTemplatePreview = MatImageConverter.ToBitmapSource(_pendingCrop);

            // Cache region in the script's reference coordinate space (for rule conditions)
            _pendingRegionScript = (
                (int)Math.Round(Math.Min(nx1, nx2) * ScriptReferenceWidth),
                (int)Math.Round(Math.Min(ny1, ny2) * ScriptReferenceHeight),
                (int)Math.Round(Math.Max(nx1, nx2) * ScriptReferenceWidth),
                (int)Math.Round(Math.Max(ny1, ny2) * ScriptReferenceHeight));

            IsConfirmingRegion = true;
            Log($"已截取 {width}×{height}，请确认保存或重选");
        }
    }

    private void ConfirmRegion()
    {
        if (_pendingCrop is null || _pendingCrop.Empty())
        {
            return;
        }

        var name = PendingImageName.Trim();
        if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            name += ".png";
        }

        if (SelectedLibraryCondition is not { } editedCondition)
            return;

        // The image belongs to the library, so it goes into <Game>/images/ where a
        // library-relative path can reach it. Writing it anywhere else would force the condition
        // to store an absolute path, which only resolves on this machine.
        var targetDirectory = TemplateLayout.ImagesDirectoryFor(
            TemplatePathResolver.TemplateRoot, SelectedGame);

        string targetPath;
        try
        {
            System.IO.Directory.CreateDirectory(targetDirectory);
            targetPath = System.IO.Path.Combine(targetDirectory, name);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"目标目录无效: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (System.IO.File.Exists(targetPath))
        {
            var answer = MessageBox.Show(
                $"文件已存在:\n{targetPath}\n\n是否覆盖？",
                "确认覆盖", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }
        }

        try
        {
            _pendingCrop.SaveImage(targetPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存图片失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        ApplyCaptureToLibraryCondition(editedCondition, targetPath, name);

        DiscardPendingRegion();
        IsConfirmingRegion = false;
    }

    /// <summary>
    /// Applies a capture to the condition being edited in the condition library tab: the image,
    /// the region, and the reference space the region was measured in.
    ///
    /// <para>Written to disk immediately rather than left pending. A condition held only in
    /// memory can be referenced by a rule that is then saved, producing a dangling reference the
    /// interpreter refuses to load.</para>
    /// </summary>
    private void ApplyCaptureToLibraryCondition(
        ConditionViewModel target, string imagePath, string imageFileName)
    {
        var condition = target.Model;

        // Store the library-relative form ("<Game>/images/x.png") so the condition stays valid
        // when the library is copied elsewhere; ResolvedTemplatePath carries the absolute path
        // for this session so matching works before the next library reload.
        condition.TemplatePath = $"{SelectedGame}/{TemplateLayout.ImagesDirectoryName}/{imageFileName}";
        condition.ResolvedTemplatePath = imagePath;

        if (_pendingRegionScript is { } r)
        {
            condition.RegionX1 = r.X1;
            condition.RegionY1 = r.Y1;
            condition.RegionX2 = r.X2;
            condition.RegionY2 = r.Y2;
        }

        // The region was measured against the current window's outer size, so record that as the
        // reference space — without it the coordinates cannot be validated later (FR-10).
        condition.ReferenceOuterWidth = ResizeWidth;
        condition.ReferenceOuterHeight = ResizeHeight;

        if (!TrySaveCondition(condition, out var savedPath))
            return;

        target.RefreshAll();
        RefreshConditionDependentViews();

        Log($"已更新条件 '{condition.Id}' 的图片和区域: {savedPath}");
    }

    /// <summary>
    /// Writes a condition into the selected game's library, reporting warnings to the log.
    /// Returns false when the write failed, so callers do not point a rule at a condition
    /// that never made it to disk.
    /// </summary>
    private bool TrySaveCondition(VisualConditionSpec condition, out string savedPath)
    {
        savedPath = string.Empty;
        try
        {
            savedPath = _conditionLibrary.Save(condition, SelectedGame, out var warnings);
            foreach (var warning in warnings)
            {
                Log($"[条件库] {warning}");
            }
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存条件失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>
    /// Re-reads everything that displays a condition's values: both editors' step and rule lists,
    /// the reference-space warning, and the picker labels. Called after a condition changes,
    /// because a shared condition's edit is visible in places the editing tab cannot see.
    /// </summary>
    private void RefreshConditionDependentViews()
    {
        foreach (var rule in Rules)
        {
            rule.RefreshConditionView();
        }

        foreach (var step in Steps)
        {
            step.RefreshConditionView();
        }

        OnPropertyChanged(nameof(ReferenceSpaceWarning));
        OnPropertyChanged(nameof(HasReferenceSpaceWarning));
    }

    /// <summary>
    /// Drops the rule's condition reference, making it an unconditional catch-all. A real
    /// configuration (the trailing rule in MultiPlayerGame.rules.json is exactly this), so it
    /// gets an explicit control instead of requiring the user to clear a combo box.
    /// </summary>
    private void ClearConditionRef()
    {
        if (SelectedRule is null) return;

        SelectedRule.ConditionRef = null;
        SelectedCondition = null;
        SelectedRule.RefreshConditionView();
        Log($"规则 '{SelectedRule.Name}' 已改为无条件兜底规则");
    }

    // DeriveConditionId is gone. It guessed a condition's Id from the captured image's file name,
    // which only made sense while capturing was also how a condition got created. Creating one is
    // now an explicit step that asks for the Id first, so the guess has no caller — and an Id the
    // user typed beats one derived from a screenshot they named in a hurry.

    private void ReselectRegion()
    {
        DiscardPendingRegion();
        IsConfirmingRegion = false;
        _regionFirstCorner = null;
        IsSelectingRegion = true;
        Log("重新框选: 点击左上角 → 点击右下角");
    }

    private void DiscardPendingRegion()
    {
        _pendingCrop?.Dispose();
        _pendingCrop = null;
        _pendingRegionScript = null;
        PendingTemplatePreview = null;
    }

    // === Step management (linear editor) ===

    /// <summary>
    /// Wraps an instruction as a step, wiring in the library lookups. Every step must go
    /// through here or one of its siblings: a step built without the lookups renders every
    /// reference as broken.
    /// </summary>
    private StepViewModel CreateStep(Instruction instruction, string? conditionRef = null)
        => StepViewModel.ForAction(instruction, LookupCondition, LookupActionBlock, conditionRef);

    private StepViewModel CreateBlockStep(string blockId, string? alias = null)
        => StepViewModel.ForBlock(blockId, alias, LookupCondition, LookupActionBlock);

    private VisualConditionSpec? LookupCondition(string id)
        => _conditionLibrary.TryGet(id, out var condition) ? condition : null;

    private ActionBlock? LookupActionBlock(string id)
        => _actionBlockLibrary.TryGet(id, out var block) ? block : null;

    private void AddInstruction(CommandType type)
    {
        var instruction = new Instruction { CommandType = type };
        switch (type)
        {
            case CommandType.WaitTime:
                instruction.WaitTimeMilliseconds = 1000;
                break;
            case CommandType.Loop:
                instruction.LoopBackToIndex = 0;
                instruction.LoopCount = -1;
                break;
            case CommandType.WaitColor:
                instruction.WaitColorHex = "#FFFFFF";
                break;
        }

        InsertStep(CreateStep(instruction));
    }

    /// <summary>
    /// Inserts the currently picked action block as a reference, not as a copy of its steps.
    /// Copying would be duplication (NFR-01) and later edits to the block would not reach this
    /// script.
    /// </summary>
    private void InsertActionBlock()
    {
        if (SelectedActionBlock is null) return;

        InsertStep(CreateBlockStep(SelectedActionBlock.Id));

        // A block can depend on conditions; report missing ones now rather than at run time.
        var missing = _actionBlockLibrary.ValidateConditionRefs(SelectedActionBlock, _conditionLibrary);
        if (missing.Count > 0)
        {
            Log($"[警告] 动作块 '{SelectedActionBlock.Id}' 依赖的条件缺失: {string.Join(", ", missing)}");
        }

        Log($"已插入动作块引用: {SelectedActionBlock.Id} ({SelectedActionBlock.Steps.Count} 步)");
    }

    /// <summary>Inserts after the selection, or appends when nothing is selected.</summary>
    private void InsertStep(StepViewModel step)
    {
        var insertAt = SelectedStep != null ? Steps.IndexOf(SelectedStep) + 1 : Steps.Count;
        Steps.Insert(insertAt, step);
        SelectedStep = step;
        CommandManagerRefresh();
    }

    /// <summary>
    /// Drops the step's condition reference. Kept separate from the rule version because the two
    /// act on different objects, and sharing one command would clear whichever happened to be
    /// selected.
    ///
    /// <para>The step is left with no condition at all rather than falling back to inline values:
    /// it has none any more. A WaitImage in this state is incomplete and has to be pointed at
    /// another condition before the script will run.</para>
    /// </summary>
    private void ClearStepConditionRef()
    {
        if (SelectedStep is null) return;

        var previous = SelectedStep.ConditionRef;
        SelectedStep.ConditionRef = null;
        SelectedCondition = null;
        Log($"步骤已取消对条件 '{previous}' 的引用，需重新选择一个条件才能运行");
    }

    private void RemoveSelectedInstruction()
    {
        if (SelectedStep is null) return;
        var index = Steps.IndexOf(SelectedStep);
        Steps.Remove(SelectedStep);
        SelectedStep = Steps.Count > 0 ? Steps[Math.Min(index, Steps.Count - 1)] : null;
        CommandManagerRefresh();
    }

    private void MoveInstructionUp()
    {
        if (SelectedStep is null) return;
        var index = Steps.IndexOf(SelectedStep);
        if (index <= 0) return;
        Steps.Move(index, index - 1);
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        CommandManagerRefresh();
    }

    private void MoveInstructionDown()
    {
        if (SelectedStep is null) return;
        var index = Steps.IndexOf(SelectedStep);
        if (index < 0 || index >= Steps.Count - 1) return;
        Steps.Move(index, index + 1);
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        CommandManagerRefresh();
    }

    // === Rule management ===
    private void AddRule()
    {
        var rule = new Rule { Name = $"规则 {Rules.Count + 1}" };
        var vm = CreateRuleViewModel(rule);
        Rules.Add(vm);
        SelectedRule = vm;
    }

    /// <summary>
    /// Wraps a rule, giving it a live lookup into the condition library. Every rule view model
    /// must go through here: one built without the lookup cannot resolve its ConditionRef and
    /// would render every rule as a broken reference.
    /// </summary>
    private RuleViewModel CreateRuleViewModel(Rule rule) => new(rule, LookupCondition);

    private void RemoveSelectedRule()
    {
        if (SelectedRule is null) return;
        var index = Rules.IndexOf(SelectedRule);
        Rules.Remove(SelectedRule);
        SelectedRule = Rules.Count > 0 ? Rules[Math.Min(index, Rules.Count - 1)] : null;
    }

    private void MoveRuleUp()
    {
        if (SelectedRule is null) return;
        var i = Rules.IndexOf(SelectedRule);
        if (i > 0) Rules.Move(i, i - 1);
        CommandManagerRefresh();
    }

    private void MoveRuleDown()
    {
        if (SelectedRule is null) return;
        var i = Rules.IndexOf(SelectedRule);
        if (i < Rules.Count - 1) Rules.Move(i, i + 1);
        CommandManagerRefresh();
    }

    private void AddAction(CommandType type)
    {
        if (SelectedRule is null) return;
        var action = new Instruction { CommandType = type };
        if (type == CommandType.WaitTime) action.WaitTimeMilliseconds = 1000;
        var vm = StepViewModel.ForAction(action, LookupCondition, LookupActionBlock);
        SelectedRule.Actions.Add(vm);
        SelectedRule.SyncActions();
        SelectedActionStep = vm;
    }

    /// <summary>
    /// Adds a reference to the picked action block to the selected rule's response actions.
    /// The rule editor's half of FR-06's "动作片段引用" — the same block the linear editor
    /// inserts, referenced rather than copied.
    /// </summary>
    private void AddActionBlockToRule()
    {
        if (SelectedRule is null || SelectedActionBlock is null) return;

        var vm = StepViewModel.ForBlock(
            SelectedActionBlock.Id, alias: null, LookupCondition, LookupActionBlock);

        SelectedRule.Actions.Add(vm);
        SelectedRule.SyncActions();
        SelectedActionStep = vm;

        var missing = _actionBlockLibrary.ValidateConditionRefs(SelectedActionBlock, _conditionLibrary);
        if (missing.Count > 0)
        {
            Log($"[警告] 动作块 '{SelectedActionBlock.Id}' 依赖的条件缺失: {string.Join(", ", missing)}");
        }

        Log($"规则 '{SelectedRule.Name}' 已添加动作块引用: {SelectedActionBlock.Id}");
    }

    private void RemoveSelectedAction()
    {
        if (SelectedRule is null || SelectedActionStep is null) return;
        SelectedRule.Actions.Remove(SelectedActionStep);
        SelectedRule.SyncActions();
        SelectedActionStep = null;
    }

    private void MoveActionUp()
    {
        if (SelectedRule is null || SelectedActionStep is null) return;
        var i = SelectedRule.Actions.IndexOf(SelectedActionStep);
        if (i > 0) SelectedRule.Actions.Move(i, i - 1);
        SelectedRule.SyncActions();
        CommandManagerRefresh();
    }

    private void MoveActionDown()
    {
        if (SelectedRule is null || SelectedActionStep is null) return;
        var i = SelectedRule.Actions.IndexOf(SelectedActionStep);
        if (i < SelectedRule.Actions.Count - 1) SelectedRule.Actions.Move(i, i + 1);
        SelectedRule.SyncActions();
        CommandManagerRefresh();
    }

    // === Rule Template Management ===
    private void LoadGames()
    {
        Games.Clear();
        var games = _templateService.GetGames();
        foreach (var game in games)
        {
            Games.Add(game);
        }

        // 如果没有游戏，添加默认的 LastCloudia
        if (Games.Count == 0)
        {
            Games.Add("LastCloudia");
        }

        // 确保 SelectedGame 在列表中
        if (!Games.Contains(SelectedGame))
        {
            SelectedGame = Games[0]; // setter 会顺带加载共享库
        }
        else
        {
            // 默认值恰好在列表里时 setter 不会触发，共享库仍需显式加载一次。
            LoadSharedLibraries();
        }
    }

    private void AddGame()
    {
        var gameName = TextPromptDialog.Show("添加新游戏", "游戏名称:");
        if (gameName is null) return;

        if (!Games.Contains(gameName))
        {
            Games.Add(gameName);
            SelectedGame = gameName;
            Log($"已添加游戏: {gameName}");
        }
    }

    private void SaveRuleAsTemplate()
    {
        if (SelectedRule is null) return;

        // 文件名取自条件的图片名；无条件的兜底规则退回用规则名，这种规则本来就没有图片，
        // 不该因此拒绝导出。
        var condition = SelectedRule.Condition;
        var fileName = !string.IsNullOrWhiteSpace(condition?.TemplatePath)
            ? Path.GetFileNameWithoutExtension(condition!.TemplatePath)
            : TemplateFileNames.Sanitize(SelectedRule.Name);

        if (SelectedRule.Model.UsesSharedCondition && condition is null)
        {
            MessageBox.Show(
                $"规则引用的条件 '{SelectedRule.ConditionRef}' 在条件库中找不到，无法保存模板。",
                "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            // fileName: JSON 文件名（用截图名）
            // SelectedRule.Name: 显示名称（规则名称）
            // SelectedRule.Description: 用户填写的描述
            // 用服务返回的真实路径，而不是在这里再拼一遍——
            // 拼接版本曾漏掉 Rules/ 一层，导致提示的路径和实际写入位置不一致。
            var fullPath = _templateService.SaveAsTemplate(
                SelectedRule.ToModel(),
                SelectedGame,
                fileName,
                SelectedRule.Name,
                SelectedRule.Description,
                condition);

            Log($"已保存规则模板: {SelectedRule.Name} (文件: {Path.GetFileName(fullPath)}, 游戏: {SelectedGame})");
            MessageBox.Show($"规则模板已保存到:\n{fullPath}",
                "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"保存模板失败: {ex.Message}");
            MessageBox.Show($"保存模板失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ImportRuleFromTemplate()
    {
        var templates = _templateService.ListTemplates();

        if (templates.Count == 0)
        {
            MessageBox.Show("规则模板库为空。\n\n您可以右键选中规则，选择「另存为模板」来创建模板。",
                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var selectionDialog = new TemplateSelectionDialog(templates);

        if (selectionDialog.ShowDialog() == true && selectionDialog.SelectedTemplate != null)
        {
            try
            {
                ImportRulesFromFile(
                    selectionDialog.SelectedTemplate.FilePath,
                    selectionDialog.SelectedTemplate.Name);
            }
            catch (Exception ex)
            {
                Log($"导入模板失败: {ex.Message}");
                MessageBox.Show($"导入模板失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// Appends a rule template's rules to the rule list. Shared by the rule editor's import
    /// button and the template library browser, so both end up with the same library reload —
    /// which is what makes an imported rule's ConditionRef resolvable.
    /// </summary>
    private void ImportRulesFromFile(string filePath, string templateName)
    {
        var rules = _templateService.LoadRulesFromTemplate(filePath);

        RuleViewModel? first = null;
        foreach (var rule in rules)
        {
            var vm = CreateRuleViewModel(rule);
            Rules.Add(vm);
            first ??= vm;
        }

        // An imported rule references a condition that may not be in the loaded library, so
        // re-read the library to pick up whatever the template brought.
        LoadSharedLibraries();

        SelectedTabIndex = RuleTabIndex;
        if (first is not null) SelectedRule = first;

        Log($"已导入 {rules.Count} 个规则: {templateName}");
        MessageBox.Show($"成功导入 {rules.Count} 个规则", "导入成功", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // === Template library browser ===

    /// <summary>
    /// Rebuilds the browsable template list for the selected game. Uses
    /// <see cref="TemplateIndex"/> rather than querying each library separately, so the four
    /// kinds are listed with the same shape and a rule template can never be confused with a
    /// linear one (FR-06).
    /// </summary>
    private void RefreshTemplateLibrary()
    {
        var previous = SelectedTemplateEntry?.FilePath;
        var kind = KindForFilter(SelectedTemplateKindFilter);

        TemplateEntries.Clear();

        try
        {
            var entries = kind is null
                ? _templateIndex.Build(SelectedGame, out var warnings)
                : _templateIndex.Build(SelectedGame, kind.Value, out warnings);

            foreach (var warning in warnings)
            {
                Log($"[模板库] {warning}");
            }

            foreach (var entry in entries)
            {
                TemplateEntries.Add(entry);
            }
        }
        catch (Exception ex)
        {
            Log($"读取模板库失败 ({SelectedGame}): {ex.Message}");
        }

        SelectedTemplateEntry = previous is null
            ? null
            : TemplateEntries.FirstOrDefault(
                e => string.Equals(e.FilePath, previous, StringComparison.OrdinalIgnoreCase));
    }

    // UseSelectedTemplate / UseConditionTemplate / UseActionBlockTemplate are gone along with the
    // template library's "使用选中模板" button. That tab is browse-only now: each kind is applied
    // from the control that owns it — the editors' reference pickers, 从库导入规则, 打开线性模板 —
    // which SelectedTemplateHint names for the selected row.

    /// <summary>Shows the backing file in Explorer, for editing something the UI does not cover.</summary>
    private void RevealSelectedTemplate()
    {
        if (SelectedTemplateEntry is not { } entry) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{entry.FilePath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log($"打开文件位置失败: {ex.Message}");
        }
    }

    // === Linear script templates ===

    private void OpenLinearTemplate()
    {
        var entries = _scriptTemplateLibrary.List(SelectedGame, out var warnings);
        foreach (var warning in warnings)
        {
            Log($"[线性模板] {warning}");
        }

        if (entries.Count == 0)
        {
            MessageBox.Show(
                $"游戏 '{SelectedGame}' 还没有线性脚本模板。\n\n" +
                "可以先编辑一个脚本，然后用「另存为线性模板」创建。",
                "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new TemplateEntryDialog(entries.ToList(), "选择线性脚本模板");
        if (dialog.ShowDialog() != true || dialog.SelectedEntry is null) return;

        try
        {
            OpenLinearTemplateFile(dialog.SelectedEntry.FilePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开模板失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Loads a linear template into the editor. Deliberately leaves
    /// <see cref="_currentFilePath"/> null: saving should write a new script, not overwrite the
    /// template the user just started from.
    /// </summary>
    private void OpenLinearTemplateFile(string filePath)
    {
        var document = _scriptTemplateLibrary.Load(filePath);

        SelectedTabIndex = LinearTabIndex;
        LoadLinearDocument(document, Path.GetFileNameWithoutExtension(filePath));

        _currentFilePath = null;
        ScriptName = document.Name ?? Path.GetFileNameWithoutExtension(filePath);

        Log($"已从线性模板载入 {Steps.Count} 步: {filePath}（保存时会另存为新文件）");
    }

    private void SaveLinearAsTemplate()
    {
        if (Steps.Count == 0) return;

        var fileName = TemplateFileNames.Sanitize(ScriptName);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            MessageBox.Show("请先给脚本起个名字。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var document = BuildLinearDocument();
            document.Name = ScriptName;
            document.OuterWidth = ResizeWidth;
            document.OuterHeight = ResizeHeight;
            document.Metadata.TargetWindowTitle =
                SelectedWindow?.Title ?? _windowInfoService.GetTargetWindowTitle();

            var path = _scriptTemplateLibrary.Save(document, SelectedGame, fileName);
            RefreshTemplateLibrary();

            Log($"已保存线性脚本模板: {path}");
            MessageBox.Show($"线性脚本模板已保存到:\n{path}",
                "保存成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log($"保存线性模板失败: {ex.Message}");
            MessageBox.Show($"保存线性模板失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // === Script persistence ===
    private void NewScript()
    {
        if (IsLinearMode)
        {
            Steps.Clear();
            SelectedStep = null;
        }
        else
        {
            Rules.Clear();
            SelectedRule = null;
        }
        _currentFilePath = null;
        ScriptName = "未命名脚本";
        Log("新建脚本");
    }

    private void OpenScript()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "AutoFlow 脚本 (*.json;*.rules.json)|*.json;*.rules.json|所有文件 (*.*)|*.*",
            Title = "打开脚本"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            // Detect the script type using only explicit, reliable signals:
            // the .rules.json filename convention and the document's own schema
            // marker. We intentionally avoid guessing from content (e.g. the
            // mere presence of a "Rules" array), which can misclassify files.
            // A file matching neither type is reported rather than silently
            // loaded as the wrong kind (which used to show an empty editor).
            var json = File.ReadAllText(dialog.FileName);
            var isRuleScript =
                dialog.FileName.EndsWith(".rules.json", StringComparison.OrdinalIgnoreCase)
                || json.Contains("autoflow/rule-script");
            var isInstructionScript = json.Contains("autoflow/script");

            if (!isRuleScript && !isInstructionScript)
            {
                MessageBox.Show(
                    "无法识别的脚本类型。\n\n" +
                    "规则脚本应使用 .rules.json 文件名，或包含 \"Schema\": \"autoflow/rule-script\"；\n" +
                    "指令脚本应包含 \"Schema\": \"autoflow/script\"。\n\n" +
                    "请检查文件是否缺少 Schema 字段。",
                    "无法打开", MessageBoxButton.OK, MessageBoxImage.Warning);
                Log($"打开失败: 无法识别脚本类型: {dialog.FileName}");
                return;
            }

            // Switch to the appropriate tab
            SelectedTabIndex = isRuleScript ? RuleTabIndex : LinearTabIndex;

            if (isRuleScript)
            {
                var script = _ruleService.Load(dialog.FileName);
                Rules.Clear();
                foreach (var rule in script.Rules)
                {
                    Rules.Add(CreateRuleViewModel(rule));
                }
                // Refresh the picker and each rule's condition view now that Rules is
                // populated: the library was loaded before these rules existed.
                RebuildAvailableConditions();
                _currentFilePath = dialog.FileName;
                ScriptName = Path.GetFileNameWithoutExtension(dialog.FileName);

                // Auto-resize window and measure client area based on script's outer dimensions
                ApplyScriptOuterSize(script.OuterWidth, script.OuterHeight);

                SelectedRule = Rules.FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(script.Metadata?.TargetWindowTitle))
                {
                    _windowInfoService.SetTargetWindowTitle(script.Metadata.TargetWindowTitle);
                }

                Log($"打开规则脚本: {dialog.FileName} ({Rules.Count} 条规则)");
            }
            else
            {
                var document = _documentService.Load(dialog.FileName);
                LoadLinearDocument(document, Path.GetFileNameWithoutExtension(dialog.FileName));
                _currentFilePath = dialog.FileName;
                ScriptName = Path.GetFileNameWithoutExtension(dialog.FileName);

                // LoadLinearDocument already applied the script's outer size.
                if (!string.IsNullOrWhiteSpace(document.Metadata?.TargetWindowTitle))
                {
                    _windowInfoService.SetTargetWindowTitle(document.Metadata.TargetWindowTitle);
                }

                Log($"打开脚本: {dialog.FileName} ({Steps.Count} 步)");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Fills the linear editor from a document, preserving references rather than expanding
    /// them.
    ///
    /// <para>A block reference stays a reference and a condition reference stays an Id. The
    /// previous version ran the whole document through <c>ScriptStepExpander</c>, which is right
    /// for execution but wrong for editing: the expanded copy has no idea it came from a shared
    /// object, so the next save rewrote every reference as inline steps.</para>
    ///
    /// <para>Documents that carry only <c>Instructions</c> are read directly, so files written
    /// before the step form still open unchanged.</para>
    /// </summary>
    private void LoadLinearDocument(ScriptDocument document, string scriptName)
    {
        Steps.Clear();
        SelectedStep = null;

        if (document.Steps.Count > 0)
        {
            for (var i = 0; i < document.Steps.Count; i++)
            {
                var step = document.Steps[i];
                step.Validate($"脚本 '{scriptName}' 步骤 {i}");

                if (step.BlockRef is { } reference)
                {
                    Steps.Add(CreateBlockStep(reference.BlockId, reference.Alias));
                    continue;
                }

                var spec = step.Action!;
                var conditionRef = spec.Parameters.TryGetValue(ActionSpecMapper.ConditionRefKey, out var raw)
                    ? raw?.ToString()
                    : null;

                Steps.Add(CreateStep(ActionSpecMapper.ToInstruction(spec), conditionRef));
            }
        }
        else
        {
            foreach (var instruction in document.Instructions)
            {
                Steps.Add(CreateStep(instruction));
            }
        }

        ApplyScriptOuterSize(document.OuterWidth, document.OuterHeight);

        SelectedStep = Steps.FirstOrDefault();

        // Report broken references at load rather than at run: a missing block or condition
        // makes the script unrunnable, and finding out mid-run is worse.
        var broken = Steps
            .Where(s => (s.IsBlockRef && s.Block is null)
                     || (s.ConditionRef is not null && s.Condition is null))
            .ToList();

        if (broken.Count > 0)
        {
            Log($"[警告] {broken.Count} 个步骤的引用无法解析（当前游戏: {SelectedGame}），请检查是否选错游戏。");
        }
    }

    /// <summary>
    /// Apply the outer window size declared by a script that was just opened.
    /// If a window is selected, automatically resize it to the script's target outer
    /// dimensions and measure the resulting client area as the coordinate reference.
    /// If no window is selected, just update the resize fields and prompt the user.
    /// </summary>
    private void ApplyScriptOuterSize(int outerWidth, int outerHeight)
    {
        if (outerWidth <= 0 || outerHeight <= 0)
        {
            Log("脚本未声明窗口尺寸，使用默认值");
            return;
        }

        // Update the resize fields so they match the script's target
        ResizeWidth = outerWidth;
        ResizeHeight = outerHeight;

        Log($"脚本目标窗口外框: {outerWidth}×{outerHeight}");

        // If a window is selected, auto-resize and measure
        if (SelectedWindow != null)
        {
            var profile = BuildWindowProfile(LastUsedProfileName);
            profile.ResizeBeforeRun = true;
            if (_windowInfoService.TryApplyResize(SelectedWindow.Handle, profile, out var client, out var reason))
            {
                ScriptReferenceWidth = client.Width;
                ScriptReferenceHeight = client.Height;
                MeasuredClientSize = $"{client.Width}×{client.Height}";
                Log($"已自动调整窗口，实测客户区 {client.Width}×{client.Height}，参考分辨率已设置");
            }
            else
            {
                Log($"自动调整窗口失败: {reason} 可手动点击\"调整窗口尺寸\"按钮重试");
            }
        }
        else
        {
            Log("提示: 请选择目标窗口，脚本将自动调整窗口尺寸");
        }
    }

    private void SaveScript()
    {
        if (string.IsNullOrEmpty(_currentFilePath))
        {
            SaveScriptAs();
            return;
        }
        SaveToPath(_currentFilePath);
    }

    private void SaveScriptAs()
    {
        var filter = IsLinearMode
            ? "AutoFlow 脚本 (*.json)|*.json"
            : "AutoFlow 规则脚本 (*.rules.json)|*.rules.json";
        var defaultName = IsLinearMode ? ScriptName + ".json" : ScriptName + ".rules.json";

        var dialog = new SaveFileDialog
        {
            Filter = filter,
            Title = "保存脚本",
            FileName = defaultName
        };
        if (dialog.ShowDialog() != true) return;
        SaveToPath(dialog.FileName);
        _currentFilePath = dialog.FileName;
        ScriptName = Path.GetFileNameWithoutExtension(dialog.FileName);
    }

    /// <summary>
    /// Builds the document for the current step list.
    ///
    /// <para>Writes the <c>Steps</c> form only when something in the list actually needs it — a
    /// block reference or a condition reference. A list of plain actions is still written as
    /// <c>Instructions</c>, so opening and re-saving an existing script does not rewrite it into
    /// a different shape for no reason.</para>
    /// </summary>
    private ScriptDocument BuildLinearDocument()
    {
        var document = new ScriptDocument();

        if (Steps.Any(s => s.RequiresStepForm))
        {
            document.Steps = Steps.Select(s => s.ToScriptStep()).ToList();
        }
        else
        {
            document.Instructions = Steps.Select(s => s.Model!).ToList();
        }

        return document;
    }

    private void SaveToPath(string path)
    {
        try
        {
            if (IsLinearMode)
            {
                var document = BuildLinearDocument();
                document.OuterWidth = ResizeWidth;
                document.OuterHeight = ResizeHeight;
                document.Metadata.TargetWindowTitle = SelectedWindow?.Title ?? _windowInfoService.GetTargetWindowTitle();
                _documentService.Save(document, path);
                Log($"保存脚本: {path}");
            }
            else
            {
                foreach (var rule in Rules)
                {
                    rule.SyncActions();
                }
                var script = new RuleScript
                {
                    Rules = Rules.Select(r => r.Model).ToList(),
                    OuterWidth = ResizeWidth,
                    OuterHeight = ResizeHeight
                };
                script.Metadata.TargetWindowTitle = SelectedWindow?.Title ?? _windowInfoService.GetTargetWindowTitle();
                _ruleService.Save(script, path);
                Log($"保存规则脚本: {path}");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Resize the target window (if enabled) and push the script's reference resolution into
    /// the coordinate transform. Both matter: templates only match at the size they were
    /// cropped at (template matching is not scale-invariant), and coordinates only resolve
    /// correctly when the transform knows which space they were authored in.
    /// Returns false if the run should be aborted.
    /// </summary>
    private bool PrepareTargetWindowForRun()
    {
        if (ResizeWindowBeforeRun && SelectedWindow is not null)
        {
            var profile = BuildWindowProfile(LastUsedProfileName);
            if (_windowInfoService.TryApplyResize(SelectedWindow.Handle, profile, out var client, out var reason))
            {
                MeasuredClientSize = $"{client.Width}×{client.Height}";
                Log($"已调整窗口外框到 {ResizeWidth}×{ResizeHeight}，实测客户区 {client.Width}×{client.Height}");
            }
            else
            {
                // The reason goes to the log too: the dialog is transient, the log is what the
                // user can come back to (and paste) after they have clicked through.
                Log($"调整窗口尺寸失败 ({ResizeWidth}×{ResizeHeight}): {reason}");
                var answer = MessageBox.Show(
                    $"调整窗口尺寸失败 ({ResizeWidth}×{ResizeHeight})。\n\n" +
                    $"原因: {reason}\n\n" +
                    "模板可能因尺寸不符而匹配不上。是否仍要继续运行？",
                    "警告", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes)
                {
                    return false;
                }
            }
        }

        _coordinateTransform.SetReferenceResolution(ScriptReferenceWidth, ScriptReferenceHeight);
        Log($"参考分辨率: {ScriptReferenceWidth}×{ScriptReferenceHeight}");
        return true;
    }

    // === Script execution ===
    private async Task RunScriptAsync()
    {
        // Auto-start capture if not already running
        if (!IsCapturing || _captureService.Current is null)
        {
            if (SelectedWindow is null)
            {
                MessageBox.Show("请先选择目标窗口。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            StartCapture();
            // Give capture service a moment to initialize
            await Task.Delay(100);
        }

        if (!PrepareTargetWindowForRun())
        {
            return;
        }

        // Activate (bring to front) the target window before running
        if (SelectedWindow != null)
        {
            if (NativeWindowHelper.TryActivateWindow(SelectedWindow.Handle))
            {
                Log("目标窗口已激活到前台");
                await Task.Delay(300); // Brief delay to ensure window is ready
            }
            else
            {
                Log("警告: 无法激活目标窗口，脚本仍将运行");
            }
        }

        var context = new ScriptExecutionContext
        {
            Capture = _captureService.Current,
            Input = new InputService(new InputSimulator()),
            CoordinateTransform = _coordinateTransform,
            WindowInfo = _windowInfoService,
            TemplateMatch = TemplateMatchService.Instance,
            Logger = msg => Application.Current.Dispatcher.Invoke(() => Log(msg)),
            EnableFrameDebug = EnableFrameDebug,
            FrameDebugPath = FrameDebugPath,
            FrameDebugMode = FrameDebugMode
        };

        var registry = InstructionHandlerRegistry.CreateDefault();

        _runCts = new CancellationTokenSource();
        IsRunning = true;

        try
        {
            if (IsLinearMode)
            {
                // Same builder the save path uses, so a run exercises exactly the document that
                // would be written — a separate construction here is how "runs fine but saves
                // wrong" bugs get in.
                var document = BuildLinearDocument();
                document.OuterWidth = ResizeWidth;
                document.OuterHeight = ResizeHeight;
                document.Metadata.TargetWindowTitle = SelectedWindow?.Title ?? _windowInfoService.GetTargetWindowTitle();

                var interpreter = new ScriptInterpreter(registry, _conditionLibrary, _actionBlockLibrary);
                Log("=== 开始执行脚本 ===");
                await Task.Run(() => interpreter.ExecuteAsync(document, context, _runCts.Token));
                Log("=== 脚本执行完成 ===");
            }
            else
            {
                foreach (var rule in Rules)
                {
                    rule.SyncActions();
                }
                var script = new RuleScript
                {
                    Rules = Rules.Select(r => r.Model).ToList()
                };
                script.Metadata.TargetWindowTitle = SelectedWindow?.Title ?? _windowInfoService.GetTargetWindowTitle();

                var interpreter = new RuleLoopInterpreter(registry, _conditionLibrary, _actionBlockLibrary);
                Log("=== 开始运行规则循环 ===");
                await Task.Run(() => interpreter.ExecuteAsync(script, context, _runCts.Token));
            }
        }
        catch (OperationCanceledException)
        {
            Log(IsLinearMode ? "=== 脚本已停止 ===" : "=== 规则循环已停止 ===");
        }
        catch (Exception ex)
        {
            Log($"=== 执行错误: {ex.Message} ===");
        }
        finally
        {
            IsRunning = false;
            _runCts?.Dispose();
            _runCts = null;
        }
    }

    private void StopScript()
    {
        _runCts?.Cancel();
    }

    // === Logging ===
    /// <summary>
    /// How many lines the in-window log keeps. Small on purpose: the window rebuilds the whole
    /// text on every line, so a long buffer makes the log pane sluggish. The file keeps
    /// everything, so trimming here loses nothing.
    /// </summary>
    private const int VisibleLogLines = 50;

    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";

        // Disk first: the window's buffer is lossy by design, so the file has to receive the
        // line whether or not it survives trimming below.
        _logWriter.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}", OnLogWriteFailed);

        LogLines.Add(line);
        while (LogLines.Count > VisibleLogLines)
        {
            LogLines.RemoveAt(0);
        }
        LogText = string.Join(Environment.NewLine, LogLines);
    }

    /// <summary>
    /// Surfaces a log-file failure in the window only. Cannot call <see cref="Log"/>: that would
    /// re-enter the writer that just failed.
    /// </summary>
    private void OnLogWriteFailed(string message)
    {
        LogLines.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        while (LogLines.Count > VisibleLogLines)
        {
            LogLines.RemoveAt(0);
        }
        LogText = string.Join(Environment.NewLine, LogLines);
    }

    /// <summary>Opens the log directory with this session's file selected.</summary>
    private void OpenLogFolder()
    {
        try
        {
            // The file does not exist until the first line is written, and /select on a missing
            // path opens nothing at all — so fall back to just opening the directory.
            var arguments = File.Exists(_logWriter.FilePath)
                ? $"/select,\"{_logWriter.FilePath}\""
                : $"\"{_logWriter.Directory}\"";

            Directory.CreateDirectory(_logWriter.Directory);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = arguments,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log($"打开日志文件夹失败: {ex.Message}");
        }
    }

    // === Debug settings ===
    private void BrowseFrameDebugPath()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择帧调试保存路径",
            SelectedPath = FrameDebugPath,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            FrameDebugPath = dialog.SelectedPath;
        }
    }

    public void Dispose()
    {
        _previewTimer.Stop();
        _runCts?.Cancel();
        _runCts?.Dispose();
        DiscardPendingRegion();
        _captureService.Dispose();

        // Last, so anything the shutdown above logs still reaches the file.
        _logWriter.Dispose();
    }
}
