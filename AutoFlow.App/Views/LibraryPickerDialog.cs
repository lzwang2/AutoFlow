using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using AutoFlow.App.Common;

namespace AutoFlow.App.Views;

/// <summary>One row offered by <see cref="LibraryPickerDialog"/>.</summary>
public sealed class LibraryPickerItem
{
    public required string Id { get; init; }
    public string? Name { get; init; }

    /// <summary>Secondary line: whatever helps tell two similar entries apart.</summary>
    public string? Detail { get; init; }
}

/// <summary>
/// Picks one or more objects out of a shared library by Id.
///
/// <para>Built in code rather than XAML for the same reason as <see cref="TextPromptDialog"/>:
/// it is a search box, a list and two buttons.</para>
///
/// <para>Selection is held on the rows as a checkbox rather than by the ListBox's own
/// SelectionMode. WPF drops the selection of any item a filter hides, so with a search box
/// the obvious approach would silently lose everything the user ticked before typing.</para>
/// </summary>
public static class LibraryPickerDialog
{
    private sealed class Row : ObservableObject
    {
        public required LibraryPickerItem Item { get; init; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set => SetProperty(ref _isChecked, value);
        }

        public string Display => string.IsNullOrWhiteSpace(Item.Name) ? Item.Id : $"{Item.Id} — {Item.Name}";
        public string? Detail => Item.Detail;

        /// <summary>Matched against the search text: Id and Name, so either one finds the row.</summary>
        public string SearchText => $"{Item.Id} {Item.Name}";
    }

    /// <summary>
    /// Shows the picker and returns the chosen Ids in list order, or null when cancelled or
    /// nothing was ticked.
    /// </summary>
    public static IReadOnlyList<string>? Show(
        string title, string label, IReadOnlyList<LibraryPickerItem> items, string? hint = null)
    {
        ArgumentNullException.ThrowIfNull(items);

        var rows = new ObservableCollection<Row>(items.Select(i => new Row { Item = i }));
        var view = CollectionViewSource.GetDefaultView(rows);

        var dialog = new Window
        {
            Title = title,
            Width = 520,
            Height = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current?.MainWindow
        };

        var grid = new Grid { Margin = new Thickness(12) };
        for (var i = 0; i < 5; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = i == 3 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto
            });
        }

        var labelBlock = new TextBlock { Text = label, FontWeight = FontWeights.Bold };
        Grid.SetRow(labelBlock, 0);
        grid.Children.Add(labelBlock);

        if (!string.IsNullOrWhiteSpace(hint))
        {
            var hintBlock = new TextBlock
            {
                Text = hint,
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            Grid.SetRow(hintBlock, 1);
            grid.Children.Add(hintBlock);
        }

        var search = new TextBox { Margin = new Thickness(0, 8, 0, 6) };
        Grid.SetRow(search, 2);
        grid.Children.Add(search);
        search.TextChanged += (_, _) =>
        {
            var needle = search.Text?.Trim();
            view.Filter = string.IsNullOrWhiteSpace(needle)
                ? null
                : o => o is Row row
                       && row.SearchText.Contains(needle, StringComparison.OrdinalIgnoreCase);
        };

        var list = new ListBox { ItemsSource = rows };
        Grid.SetRow(list, 3);
        grid.Children.Add(list);

        // Checkbox bound two-way to the row, so ticking survives a filter change.
        var itemTemplate = new DataTemplate();
        var checkBox = new FrameworkElementFactory(typeof(CheckBox));
        checkBox.SetBinding(ToggleButton_IsCheckedProperty,
            new Binding(nameof(Row.IsChecked)) { Mode = BindingMode.TwoWay });
        checkBox.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 2));

        var content = new FrameworkElementFactory(typeof(StackPanel));
        var line1 = new FrameworkElementFactory(typeof(TextBlock));
        line1.SetBinding(TextBlock.TextProperty, new Binding(nameof(Row.Display)));
        line1.SetValue(TextBlock.FontFamilyProperty, new System.Windows.Media.FontFamily("Consolas"));
        var line2 = new FrameworkElementFactory(typeof(TextBlock));
        line2.SetBinding(TextBlock.TextProperty, new Binding(nameof(Row.Detail)));
        line2.SetValue(TextBlock.ForegroundProperty, System.Windows.Media.Brushes.Gray);
        line2.SetValue(TextBlock.FontSizeProperty, 11.0);
        line2.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        content.AppendChild(line1);
        content.AppendChild(line2);
        checkBox.AppendChild(content);
        itemTemplate.VisualTree = checkBox;
        list.ItemTemplate = itemTemplate;

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        var ok = new Button { Content = "确定", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "取消", Width = 80, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 4);
        grid.Children.Add(buttons);

        dialog.Content = grid;
        ok.Click += (_, _) => { dialog.DialogResult = true; };
        dialog.Loaded += (_, _) => search.Focus();

        if (dialog.ShowDialog() != true)
            return null;

        var chosen = rows.Where(r => r.IsChecked).Select(r => r.Item.Id).ToList();
        return chosen.Count == 0 ? null : chosen;
    }

    /// <summary>
    /// CheckBox.IsChecked lives on ToggleButton and is nullable; naming it once here keeps the
    /// template-building code above readable.
    /// </summary>
    private static readonly DependencyProperty ToggleButton_IsCheckedProperty =
        System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty;
}
