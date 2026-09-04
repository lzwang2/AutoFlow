using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using AutoFlow.Core.Scripting;

namespace AutoFlow.App.Views;

/// <summary>
/// Picks one entry out of a <see cref="TemplateIndexEntry"/> list.
///
/// Separate from <see cref="TemplateSelectionDialog"/> because that one is typed to
/// <c>RuleTemplateInfo</c> and only knows about rule templates. This one works for all four
/// kinds, which is what browsing the library needs (FR-06).
/// </summary>
public partial class TemplateEntryDialog : Window
{
    public List<TemplateIndexEntry> Entries { get; }
    public TemplateIndexEntry? SelectedEntry { get; set; }
    public string Prompt { get; }

    public TemplateEntryDialog(List<TemplateIndexEntry> entries, string prompt = "请选择一个模板:")
    {
        InitializeComponent();
        Entries = entries;
        Prompt = prompt;
        DataContext = this;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEntry != null)
        {
            DialogResult = true;
            Close();
        }
    }

    private void EntryListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedEntry != null)
        {
            DialogResult = true;
            Close();
        }
    }
}
