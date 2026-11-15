using System.Windows;
using Microsoft.Win32;

namespace AutoFlow.App.Services;

public interface IEditorInteraction
{
    MessageBoxResult ConfirmReplacement();
    string? ChooseSavePath(string name);
    void ReportSaveFailure(string message);
}

public sealed class EditorInteraction : IEditorInteraction
{
    public MessageBoxResult ConfirmReplacement() => MessageBox.Show(
        "打开副本将替换规则文档；切换游戏还可能重载条件和动作库。\n是否先保存当前编辑内容？\n是：分别保存现有脚本及库编辑；否：放弃被替换的内容；取消：返回。",
        "保护编辑内容", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

    public string? ChooseSavePath(string name)
    {
        var dialog = new SaveFileDialog { Title = "保存当前编辑内容", FileName = name, Filter = "JSON 脚本|*.json" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void ReportSaveFailure(string message) => MessageBox.Show("保存失败，保留原编辑内容：" + message);
}
