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

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly CaptureService _captureService = new();
    private readonly ScriptDocumentService _documentService = new();
    private readonly RuleScriptService _ruleService = new();
    private readonly RuleTemplateService _templateService = new();
    private readonly ConditionLibrary _conditionLibrary = new();
    private readonly ActionBlockLibrary _actionBlockLibrary = new();
    private readonly CoordinateTransformService _coordinateTransform = new();
    private readonly WindowInfoService _windowInfoService = new();
    private readonly WindowTargetProfileService _profileService = new();
    private readonly DispatcherTimer _previewTimer;

    private CancellationTokenSource? _runCts;
    private string? _currentFilePath;
    private bool _suppressProfileSave;

    public MainViewModel()
    {
        Windows = new ObservableCollection<WindowOption>();
        Instructions = new ObservableCollection<InstructionViewModel>();
        Rules = new ObservableCollection<RuleViewModel>();
        Rules.CollectionChanged += (_, _) => CommandManagerRefresh();
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

        RemoveInstructionCommand = new RelayCommand(RemoveSelectedInstruction, () => SelectedInstruction != null);
        MoveUpCommand = new RelayCommand(MoveInstructionUp, () => CanMoveUp);
        MoveDownCommand = new RelayCommand(MoveInstructionDown, () => CanMoveDown);

        NewScriptCommand = new RelayCommand(NewScript);
        OpenScriptCommand = new RelayCommand(OpenScript);
        SaveScriptCommand = new RelayCommand(SaveScript);
        SaveAsScriptCommand = new RelayCommand(SaveScriptAs);

        RunScriptCommand = new RelayCommand(async () => await RunScriptAsync(),
            () => !IsRunning && (IsLinearMode ? Instructions.Count > 0 : Rules.Count > 0));
        StopScriptCommand = new RelayCommand(StopScript, () => IsRunning);

        PickCoordinateCommand = new RelayCommand(() => IsPickingCoordinate = true, () => SelectedInstruction != null && IsCapturing);
        StartRegionSelectCommand = new RelayCommand(StartRegionSelect,
            () => (SelectedInstruction?.CommandType == CommandType.WaitImage || SelectedRule != null) && IsCapturing);
        ConfirmRegionCommand = new RelayCommand(ConfirmRegion, () => IsConfirmingRegion);
        ReselectRegionCommand = new RelayCommand(ReselectRegion, () => IsConfirmingRegion);

        // Rule commands
        AddRuleCommand = new RelayCommand(AddRule);
        RemoveRuleCommand = new RelayCommand(RemoveSelectedRule, () => SelectedRule != null);
        MoveRuleUpCommand = new RelayCommand(MoveRuleUp, () => CanMoveRuleUp);
        MoveRuleDownCommand = new RelayCommand(MoveRuleDown, () => CanMoveRuleDown);
        AddClickActionCommand = new RelayCommand(() => AddAction(CommandType.Click), () => SelectedRule != null);
        AddWaitTimeActionCommand = new RelayCommand(() => AddAction(CommandType.WaitTime), () => SelectedRule != null);
        RemoveActionCommand = new RelayCommand(RemoveSelectedAction, () => SelectedAction != null);
        MoveActionUpCommand = new RelayCommand(MoveActionUp, () => CanMoveActionUp);
        MoveActionDownCommand = new RelayCommand(MoveActionDown, () => CanMoveActionDown);
        PickActionCoordinateCommand = new RelayCommand(() => IsPickingCoordinate = true,
            () => SelectedAction?.CommandType == CommandType.Click && IsCapturing);

        // Rule template commands
        SaveRuleAsTemplateCommand = new RelayCommand(SaveRuleAsTemplate, () => SelectedRule != null);
        ImportRuleFromTemplateCommand = new RelayCommand(ImportRuleFromTemplate);
        AddGameCommand = new RelayCommand(AddGame);

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
    public ObservableCollection<InstructionViewModel> Instructions { get; }
    public ObservableCollection<RuleViewModel> Rules { get; }
    public ObservableCollection<string> LogLines { get; }
    public ObservableCollection<string> Games { get; } = new();

    private string _logText = string.Empty;
    public string LogText
    {
        get => _logText;
        private set => SetProperty(ref _logText, value);
    }

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
    public RelayCommand RemoveActionCommand { get; }
    public RelayCommand MoveActionUpCommand { get; }
    public RelayCommand MoveActionDownCommand { get; }
    public RelayCommand PickActionCoordinateCommand { get; }
    public RelayCommand SaveRuleAsTemplateCommand { get; }
    public RelayCommand ImportRuleFromTemplateCommand { get; }
    public RelayCommand AddGameCommand { get; }
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

    private InstructionViewModel? _selectedInstruction;
    public InstructionViewModel? SelectedInstruction
    {
        get => _selectedInstruction;
        set { if (SetProperty(ref _selectedInstruction, value)) CommandManagerRefresh(); }
    }

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

    private string _templateName = string.Empty;
    public string TemplateName
    {
        get => _templateName;
        set => SetProperty(ref _templateName, value);
    }

    private string _templateDirectory =
        System.IO.Path.Combine(AppContext.BaseDirectory, "Templates");
    public string TemplateDirectory
    {
        get => _templateDirectory;
        set => SetProperty(ref _templateDirectory, value);
    }

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
                CommandManagerRefresh();
            }
        }
    }

    public bool IsLinearMode => SelectedTabIndex == 0;
    public bool IsRuleMode => SelectedTabIndex == 1;

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

    public bool CanMoveUp => SelectedInstruction != null && Instructions.IndexOf(SelectedInstruction) > 0;
    public bool CanMoveDown => SelectedInstruction != null && Instructions.IndexOf(SelectedInstruction) < Instructions.Count - 1;

    // === Rule editor state ===
    private RuleViewModel? _selectedRule;
    public RuleViewModel? SelectedRule
    {
        get => _selectedRule;
        set
        {
            if (SetProperty(ref _selectedRule, value))
            {
                SelectedAction = null;
                OnPropertyChanged(nameof(SelectedRuleActions));
                CommandManagerRefresh();
            }
        }
    }

    public ObservableCollection<InstructionViewModel>? SelectedRuleActions => SelectedRule?.Actions;

    private InstructionViewModel? _selectedAction;
    public InstructionViewModel? SelectedAction
    {
        get => _selectedAction;
        set { if (SetProperty(ref _selectedAction, value)) CommandManagerRefresh(); }
    }

    public bool CanMoveRuleUp => SelectedRule != null && Rules.IndexOf(SelectedRule) > 0;
    public bool CanMoveRuleDown => SelectedRule != null && Rules.IndexOf(SelectedRule) < Rules.Count - 1;
    public bool CanMoveActionUp => SelectedRule != null && SelectedAction != null && SelectedRule.Actions.IndexOf(SelectedAction) > 0;
    public bool CanMoveActionDown => SelectedRule != null && SelectedAction != null && SelectedRule.Actions.IndexOf(SelectedAction) < SelectedRule.Actions.Count - 1;

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
        if (!_windowInfoService.TryApplyResize(SelectedWindow.Handle, profile, out var client))
        {
            Log($"调整窗口尺寸失败: {SelectedWindow.Title}");
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

        // Picking for instruction coordinate (linear script)
        if (SelectedInstruction is not null)
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
        else if (SelectedAction is not null)
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

    private void StartRegionSelect()
    {
        // Support both WaitImage instructions (linear scripts) and Rule conditions
        if (SelectedInstruction?.CommandType != CommandType.WaitImage && SelectedRule is null)
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(TemplateName))
        {
            MessageBox.Show("请先填写模板图片名称。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
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

        var name = TemplateName.Trim();
        if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            name += ".png";
        }

        string targetPath;
        try
        {
            System.IO.Directory.CreateDirectory(TemplateDirectory);
            targetPath = System.IO.Path.Combine(TemplateDirectory, name);
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

        // Apply to WaitImage instruction or Rule condition
        if (SelectedInstruction is not null)
        {
            SelectedInstruction.TemplatePath = targetPath;
            Log($"已保存模板: {targetPath}");
        }
        else if (SelectedRule is not null)
        {
            SelectedRule.TemplatePath = targetPath;
            if (_pendingRegionScript is { } r)
            {
                SelectedRule.RegionX1 = r.X1;
                SelectedRule.RegionY1 = r.Y1;
                SelectedRule.RegionX2 = r.X2;
                SelectedRule.RegionY2 = r.Y2;
            }
            SelectedRule.RefreshSummary();
            Log($"已保存模板并设为匹配区域: {targetPath}");
        }

        DiscardPendingRegion();
        IsConfirmingRegion = false;
    }

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

    // === Instruction management ===
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

        var vm = new InstructionViewModel(instruction);
        var insertAt = SelectedInstruction != null
            ? Instructions.IndexOf(SelectedInstruction) + 1
            : Instructions.Count;
        Instructions.Insert(insertAt, vm);
        SelectedInstruction = vm;
        CommandManagerRefresh();
    }

    private void RemoveSelectedInstruction()
    {
        if (SelectedInstruction is null) return;
        var index = Instructions.IndexOf(SelectedInstruction);
        Instructions.Remove(SelectedInstruction);
        SelectedInstruction = Instructions.Count > 0
            ? Instructions[Math.Min(index, Instructions.Count - 1)]
            : null;
        CommandManagerRefresh();
    }

    private void MoveInstructionUp()
    {
        if (SelectedInstruction is null) return;
        var index = Instructions.IndexOf(SelectedInstruction);
        if (index <= 0) return;
        Instructions.Move(index, index - 1);
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        CommandManagerRefresh();
    }

    private void MoveInstructionDown()
    {
        if (SelectedInstruction is null) return;
        var index = Instructions.IndexOf(SelectedInstruction);
        if (index < 0 || index >= Instructions.Count - 1) return;
        Instructions.Move(index, index + 1);
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
        CommandManagerRefresh();
    }

    // === Rule management ===
    private void AddRule()
    {
        var rule = new Rule { Name = $"规则 {Rules.Count + 1}" };
        var vm = new RuleViewModel(rule);
        Rules.Add(vm);
        SelectedRule = vm;
    }

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
        var vm = new InstructionViewModel(action);
        SelectedRule.Actions.Add(vm);
        SelectedRule.SyncActions();
        SelectedAction = vm;
    }

    private void RemoveSelectedAction()
    {
        if (SelectedRule is null || SelectedAction is null) return;
        SelectedRule.Actions.Remove(SelectedAction);
        SelectedRule.SyncActions();
        SelectedAction = null;
    }

    private void MoveActionUp()
    {
        if (SelectedRule is null || SelectedAction is null) return;
        var i = SelectedRule.Actions.IndexOf(SelectedAction);
        if (i > 0) SelectedRule.Actions.Move(i, i - 1);
        SelectedRule.SyncActions();
        CommandManagerRefresh();
    }

    private void MoveActionDown()
    {
        if (SelectedRule is null || SelectedAction is null) return;
        var i = SelectedRule.Actions.IndexOf(SelectedAction);
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
        var dialog = new System.Windows.Window
        {
            Title = "添加新游戏",
            Width = 400,
            Height = 150,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow
        };

        var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
        var label = new System.Windows.Controls.TextBlock { Text = "游戏名称:", Margin = new Thickness(0, 0, 0, 10) };
        var textBox = new System.Windows.Controls.TextBox { Margin = new Thickness(0, 0, 0, 20) };
        var buttonPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        var okButton = new System.Windows.Controls.Button
        {
            Content = "确定",
            Width = 80,
            Margin = new Thickness(0, 0, 10, 0),
            IsDefault = true
        };
        var cancelButton = new System.Windows.Controls.Button
        {
            Content = "取消",
            Width = 80,
            IsCancel = true
        };

        okButton.Click += (s, e) => { dialog.DialogResult = true; dialog.Close(); };
        cancelButton.Click += (s, e) => { dialog.DialogResult = false; dialog.Close(); };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);
        stack.Children.Add(label);
        stack.Children.Add(textBox);
        stack.Children.Add(buttonPanel);
        dialog.Content = stack;

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(textBox.Text))
        {
            var gameName = textBox.Text.Trim();
            if (!Games.Contains(gameName))
            {
                Games.Add(gameName);
                SelectedGame = gameName;
                Log($"已添加游戏: {gameName}");
            }
        }
    }

    private void SaveRuleAsTemplate()
    {
        if (SelectedRule is null) return;

        // 从 TemplatePath 提取截图名称（不含扩展名）作为 JSON 文件名
        string fileName;
        if (!string.IsNullOrWhiteSpace(SelectedRule.TemplatePath))
        {
            fileName = Path.GetFileNameWithoutExtension(SelectedRule.TemplatePath);
        }
        else
        {
            MessageBox.Show("规则未设置截图路径，无法保存模板。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                SelectedRule.Description);

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

        // 简单的选择对话框（后续可以改进为自定义UI）
        var templateNames = templates.Select(t => t.DisplayText).ToList();
        var selectionDialog = new TemplateSelectionDialog(templates);

        if (selectionDialog.ShowDialog() == true && selectionDialog.SelectedTemplate != null)
        {
            try
            {
                var rules = _templateService.LoadRulesFromTemplate(selectionDialog.SelectedTemplate.FilePath);

                foreach (var rule in rules)
                {
                    var vm = new RuleViewModel(rule);
                    Rules.Add(vm);
                }

                Log($"已导入 {rules.Count} 个规则: {selectionDialog.SelectedTemplate.Name}");
                MessageBox.Show($"成功导入 {rules.Count} 个规则", "导入成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"导入模板失败: {ex.Message}");
                MessageBox.Show($"导入模板失败:\n{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    // === Script persistence ===
    private void NewScript()
    {
        if (IsLinearMode)
        {
            Instructions.Clear();
            SelectedInstruction = null;
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
            SelectedTabIndex = isRuleScript ? 1 : 0;

            if (isRuleScript)
            {
                var script = _ruleService.Load(dialog.FileName);
                Rules.Clear();
                foreach (var rule in script.Rules)
                {
                    Rules.Add(new RuleViewModel(rule));
                }
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

                // Steps 形态的脚本要先展开成指令，否则编辑器会显示为空——
                // 编辑器只认 Instructions，而新格式的内容可能全在 Steps 里。
                var expanded = new ScriptStepExpander(_conditionLibrary, _actionBlockLibrary)
                    .Expand(document, Path.GetFileNameWithoutExtension(dialog.FileName));

                Instructions.Clear();
                foreach (var step in expanded)
                {
                    Instructions.Add(new InstructionViewModel(step.Instruction));
                }
                _currentFilePath = dialog.FileName;
                ScriptName = Path.GetFileNameWithoutExtension(dialog.FileName);

                // Auto-resize window and measure client area based on script's outer dimensions
                ApplyScriptOuterSize(document.OuterWidth, document.OuterHeight);

                if (!string.IsNullOrWhiteSpace(document.Metadata?.TargetWindowTitle))
                {
                    _windowInfoService.SetTargetWindowTitle(document.Metadata.TargetWindowTitle);
                }

                Log($"打开脚本: {dialog.FileName} ({Instructions.Count} 条指令)");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"打开失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
            if (_windowInfoService.TryApplyResize(SelectedWindow.Handle, profile, out var client))
            {
                ScriptReferenceWidth = client.Width;
                ScriptReferenceHeight = client.Height;
                MeasuredClientSize = $"{client.Width}×{client.Height}";
                Log($"已自动调整窗口，实测客户区 {client.Width}×{client.Height}，参考分辨率已设置");
            }
            else
            {
                Log($"自动调整窗口失败，请手动点击\"调整窗口尺寸\"按钮");
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

    private void SaveToPath(string path)
    {
        try
        {
            if (IsLinearMode)
            {
                var document = new ScriptDocument
                {
                    Instructions = Instructions.Select(vm => vm.Model).ToList(),
                    OuterWidth = ResizeWidth,
                    OuterHeight = ResizeHeight
                };
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
            if (_windowInfoService.TryApplyResize(SelectedWindow.Handle, profile, out var client))
            {
                MeasuredClientSize = $"{client.Width}×{client.Height}";
                Log($"已调整窗口外框到 {ResizeWidth}×{ResizeHeight}，实测客户区 {client.Width}×{client.Height}");
            }
            else
            {
                var answer = MessageBox.Show(
                    $"调整窗口尺寸失败 ({ResizeWidth}×{ResizeHeight})。\n" +
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
                var document = new ScriptDocument
                {
                    Instructions = Instructions.Select(vm => vm.Model).ToList()
                };
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
    private void Log(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogLines.Add(line);
        while (LogLines.Count > 500)
        {
            LogLines.RemoveAt(0);
        }
        LogText = string.Join(Environment.NewLine, LogLines);
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
    }
}
