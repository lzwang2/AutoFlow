using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using AutoFlow.Core.Scripting.Rules;

namespace AutoFlow.App.Views;

public partial class TemplateSelectionDialog : Window
{
    public List<RuleTemplateInfo> Templates { get; }
    public RuleTemplateInfo? SelectedTemplate { get; set; }
    public string TemplateDirectory { get; }

    public TemplateSelectionDialog(List<RuleTemplateInfo> templates)
    {
        InitializeComponent();
        Templates = templates;
        TemplateDirectory = new RuleTemplateService().GetTemplateDirectory();
        DataContext = this;
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTemplate != null)
        {
            DialogResult = true;
            Close();
        }
    }

    private void TemplateListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedTemplate != null)
        {
            DialogResult = true;
            Close();
        }
    }
}
