using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AutoFlow.App.ViewModels;
using Fischless.HotkeyCapture;
using Microsoft.Win32;
using Vanara.PInvoke;
using WinFormsKeys = System.Windows.Forms.Keys;

namespace AutoFlow.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private HotkeyHook? _hotkeyHook;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;
        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    // Global hotkeys: F9 = run, F10 = stop. Registered globally (via RegisterHotKey) so they
    // work even while the target game window has focus, which is the normal case during a run.
    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _hotkeyHook = new HotkeyHook();
            _hotkeyHook.KeyPressed += OnGlobalHotkeyPressed;
            _hotkeyHook.RegisterHotKey(User32.HotKeyModifiers.MOD_NONE, WinFormsKeys.F9);
            _hotkeyHook.RegisterHotKey(User32.HotKeyModifiers.MOD_NONE, WinFormsKeys.F10);
        }
        catch (Exception ex)
        {
            // A hotkey may already be claimed by another app; degrade gracefully to buttons only.
            _hotkeyHook?.Dispose();
            _hotkeyHook = null;
            MessageBox.Show($"注册全局快捷键失败（F9/F10 可能被占用），可继续使用按钮：\n{ex.Message}",
                "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnGlobalHotkeyPressed(object? sender, KeyPressedEventArgs e)
    {
        // Hotkey callback arrives on the hook's message thread; marshal to the UI thread.
        Dispatcher.Invoke(() =>
        {
            switch (e.Key)
            {
                case WinFormsKeys.F9 when _viewModel.RunScriptCommand.CanExecute(null):
                    _viewModel.RunScriptCommand.Execute(null);
                    break;
                case WinFormsKeys.F10 when _viewModel.StopScriptCommand.CanExecute(null):
                    _viewModel.StopScriptCommand.Execute(null);
                    break;
            }
        });
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        if (_hotkeyHook is not null)
        {
            _hotkeyHook.KeyPressed -= OnGlobalHotkeyPressed;
            _hotkeyHook.Dispose();
            _hotkeyHook = null;
        }
        _viewModel.Dispose();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Hide the frozen selection rectangle once the region workflow is fully done
        if (e.PropertyName is nameof(MainViewModel.IsConfirmingRegion) or nameof(MainViewModel.IsSelectingRegion))
        {
            if (!_viewModel.IsConfirmingRegion && !_viewModel.IsSelectingRegion)
            {
                HideRegionRect();
            }
        }
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && (_viewModel.IsSelectingRegion || _viewModel.IsConfirmingRegion))
        {
            _viewModel.CancelRegionSelect();
            HideRegionRect();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Compute normalized (0..1) content coordinates from a mouse position over the
    /// letterboxed (Stretch=Uniform) image. Returns false if outside the content rect.
    /// </summary>
    private static bool TryGetNormalized(Image image, Point position, out double normalizedX, out double normalizedY)
    {
        normalizedX = 0;
        normalizedY = 0;

        if (image.Source is null)
        {
            return false;
        }

        var sourceWidth = image.Source.Width;
        var sourceHeight = image.Source.Height;
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return false;
        }

        var scale = Math.Min(image.ActualWidth / sourceWidth, image.ActualHeight / sourceHeight);
        var displayedWidth = sourceWidth * scale;
        var displayedHeight = sourceHeight * scale;
        var offsetX = (image.ActualWidth - displayedWidth) / 2;
        var offsetY = (image.ActualHeight - displayedHeight) / 2;

        var contentX = position.X - offsetX;
        var contentY = position.Y - offsetY;

        if (contentX < 0 || contentY < 0 || contentX > displayedWidth || contentY > displayedHeight)
        {
            return false; // in the letterbox area
        }

        normalizedX = contentX / displayedWidth;
        normalizedY = contentY / displayedHeight;
        return true;
    }

    private Point? _regionStartDisplay;

    private void PreviewImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Image image)
        {
            return;
        }

        if (!TryGetNormalized(image, e.GetPosition(image), out var normalizedX, out var normalizedY))
        {
            return;
        }

        if (_viewModel.IsSelectingRegion)
        {
            var displayPoint = e.GetPosition(RegionOverlay);
            if (_regionStartDisplay is null)
            {
                // first corner
                _regionStartDisplay = displayPoint;
                Canvas.SetLeft(RegionRect, displayPoint.X);
                Canvas.SetTop(RegionRect, displayPoint.Y);
                RegionRect.Width = 0;
                RegionRect.Height = 0;
                RegionRect.Visibility = Visibility.Visible;
            }
            else
            {
                // second corner -> stop rubber-band but keep the rect frozen for confirmation
                _regionStartDisplay = null;
            }
            _viewModel.OnRegionClicked(normalizedX, normalizedY);
            return;
        }

        _viewModel.OnPreviewClicked(normalizedX, normalizedY);
    }

    private void PreviewImage_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_viewModel.IsSelectingRegion || _regionStartDisplay is null)
        {
            return;
        }

        var current = e.GetPosition(RegionOverlay);
        var start = _regionStartDisplay.Value;
        var x = Math.Min(start.X, current.X);
        var y = Math.Min(start.Y, current.Y);
        Canvas.SetLeft(RegionRect, x);
        Canvas.SetTop(RegionRect, y);
        RegionRect.Width = Math.Abs(current.X - start.X);
        RegionRect.Height = Math.Abs(current.Y - start.Y);
    }

    private void HideRegionRect()
    {
        _regionStartDisplay = null;
        RegionRect.Visibility = Visibility.Collapsed;
        RegionRect.Width = 0;
        RegionRect.Height = 0;
    }

    private void BrowseTemplateDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择模板保存目录"
        };
        if (!string.IsNullOrWhiteSpace(_viewModel.TemplateDirectory))
        {
            dialog.InitialDirectory = _viewModel.TemplateDirectory;
        }
        if (dialog.ShowDialog() == true)
        {
            _viewModel.TemplateDirectory = dialog.FolderName;
        }
    }

    private void BrowseTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedInstruction is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "图片文件 (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.bmp|所有文件 (*.*)|*.*",
            Title = "选择模板图片"
        };

        if (dialog.ShowDialog() == true)
        {
            _viewModel.SelectedInstruction.TemplatePath = dialog.FileName;
        }
    }
}
