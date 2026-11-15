using AutoFlow.App.Common;
using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Rules;

namespace AutoFlow.App.ViewModels;

/// <summary>Owns the Last Cloudia preset; navigation never mutates the editor's document.</summary>
public sealed class LastCloudiaViewModel(Action<string> log) : ObservableObject
{
    public string ScriptPath => System.IO.Path.Combine(
        TemplateLayout.DirectoryFor(TemplatePathResolver.TemplateRoot, "LastCloudia", TemplateKind.Rule),
        "MultiPlayerGame.rules.json");

    public RuleScript? Script { get; private set; }
    private string _loadStatus = "尚未加载";
    public string LoadStatus { get => _loadStatus; private set => SetProperty(ref _loadStatus, value); }

    public (ConditionLibrary Conditions, ActionBlockLibrary Blocks) LoadLibraries()
    {
        var conditions = new ConditionLibrary();
        var blocks = new ActionBlockLibrary();
        foreach (var warning in conditions.LoadGame("LastCloudia")) log(warning);
        foreach (var warning in blocks.LoadGame("LastCloudia")) log(warning);
        return (conditions, blocks);
    }

    public void Reload()
    {
        try
        {
            var script = GameTaskScript.Load(ScriptPath);
            var libraries = LoadLibraries();
            GameTaskScript.Validate(script, libraries.Conditions, libraries.Blocks, AppContext.BaseDirectory);
            Script = script;
            LoadStatus = "多人游戏脚本已加载 · 无需配置";
        }
        catch (Exception ex)
        {
            Script = null;
            LoadStatus = "加载失败：" + ex.Message;
            log(LoadStatus);
        }
    }
}
