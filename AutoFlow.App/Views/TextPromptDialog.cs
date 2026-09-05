using System.Windows;
using System.Windows.Controls;

namespace AutoFlow.App.Views;

/// <summary>
/// A one-line text prompt. Built in code rather than XAML because it is a single TextBox and
/// two buttons; a .xaml/.xaml.cs pair for that is more plumbing than content.
///
/// <para>Exists so "ask the user for an Id" is written once. New games, new conditions and new
/// action blocks all need it, and the alternative — building the same StackPanel inline at each
/// call site, as AddGame used to — makes each copy free to drift.</para>
/// </summary>
public static class TextPromptDialog
{
    /// <summary>
    /// Shows the prompt and returns the trimmed input, or null when cancelled or left blank.
    /// </summary>
    /// <param name="title">Window title.</param>
    /// <param name="label">Text shown above the input box.</param>
    /// <param name="initialValue">Pre-filled value; selected so typing replaces it.</param>
    /// <param name="hint">Optional grey explanatory line below the box, for naming rules.</param>
    public static string? Show(string title, string label, string initialValue = "", string? hint = null)
    {
        var dialog = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow
        };

        var stack = new StackPanel { Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 6) });

        var textBox = new TextBox { Text = initialValue };
        stack.Children.Add(textBox);

        if (!string.IsNullOrWhiteSpace(hint))
        {
            stack.Children.Add(new TextBlock
            {
                Text = hint,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var ok = new Button { Content = "确定", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "取消", Width = 80, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);

        dialog.Content = stack;

        ok.Click += (_, _) => { dialog.DialogResult = true; };

        // Focus and select on load, so Enter-to-accept and type-to-replace both work without
        // the user having to click into the box first.
        dialog.Loaded += (_, _) => { textBox.Focus(); textBox.SelectAll(); };

        if (dialog.ShowDialog() != true)
            return null;

        var value = textBox.Text?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
