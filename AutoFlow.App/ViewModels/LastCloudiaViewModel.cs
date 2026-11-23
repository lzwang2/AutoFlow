using System.Collections.ObjectModel;
using System.IO;
using AutoFlow.App.Common;
using AutoFlow.Core.Scripting;
using AutoFlow.Core.Scripting.Actions;
using AutoFlow.Core.Scripting.Conditions;
using AutoFlow.Core.Scripting.Rules;

namespace AutoFlow.App.ViewModels;

public enum LastCloudiaTaskKind
{
    RuleScript,
    LinearScript
}

public sealed record LastCloudiaTaskSnapshot(
    LastCloudiaModuleViewModel Module,
    LastCloudiaTaskKind Kind,
    RuleScript? RuleScript,
    ScriptDocument? LinearScript);

public sealed class LastCloudiaModuleViewModel : ObservableObject
{
    private string _loadStatus = "尚未加载";
    private string _runStatus = "尚未运行";

    public LastCloudiaModuleViewModel(
        string id, string displayName, string description,
        LastCloudiaTaskKind kind, string scriptPath)
    {
        Id = id;
        DisplayName = displayName;
        Description = description;
        Kind = kind;
        ScriptPath = scriptPath;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public LastCloudiaTaskKind Kind { get; }
    public string ScriptPath { get; }
    public RuleScript? RuleScript { get; private set; }
    public ScriptDocument? LinearScript { get; private set; }
    public bool IsRunnable => RuleScript is not null || LinearScript is not null;
    public string LoadStatus { get => _loadStatus; private set { if (SetProperty(ref _loadStatus, value)) OnPropertyChanged(nameof(StatusText)); } }
    public string RunStatus { get => _runStatus; private set { if (SetProperty(ref _runStatus, value)) OnPropertyChanged(nameof(StatusText)); } }
    public string StatusText => $"{LoadStatus} · {RunStatus}";

    internal void Reload(ConditionLibrary conditions, ActionBlockLibrary blocks, Action<string> log)
    {
        try
        {
            RuleScript = null;
            LinearScript = null;
            if (Kind == LastCloudiaTaskKind.RuleScript)
            {
                var script = GameTaskScript.Load(ScriptPath);
                GameTaskScript.Validate(script, conditions, blocks, AppContext.BaseDirectory);
                RuleScript = script;
            }
            else
            {
                var script = GameTaskScript.LoadLinear(ScriptPath);
                GameTaskScript.Validate(script, conditions, blocks, AppContext.BaseDirectory);
                LinearScript = script;
            }
            LoadStatus = "已加载";
        }
        catch (Exception ex)
        {
            RuleScript = null;
            LinearScript = null;
            LoadStatus = "加载失败：" + ex.Message;
            log($"{DisplayName} {LoadStatus}");
        }
        OnPropertyChanged(nameof(IsRunnable));
    }

    internal LastCloudiaTaskSnapshot CreateSnapshot()
    {
        if (!IsRunnable) throw new InvalidOperationException($"{DisplayName} 尚未成功加载。");
        return new LastCloudiaTaskSnapshot(
            this,
            Kind,
            RuleScript is null ? null : GameTaskScript.Copy(RuleScript),
            LinearScript is null ? null : GameTaskScript.Copy(LinearScript));
    }

    internal void SetRunStatus(string value) => RunStatus = value;
}

/// <summary>Owns Last Cloudia feature modules; navigation never mutates editor documents.</summary>
public sealed class LastCloudiaViewModel : ObservableObject
{
    private readonly Action<string> _log;
    private LastCloudiaModuleViewModel? _selectedModule;
    private bool _selectionLocked;

    public LastCloudiaViewModel(Action<string> log)
    {
        _log = log;
        var root = TemplatePathResolver.TemplateRoot;
        Modules = new ObservableCollection<LastCloudiaModuleViewModel>
        {
            new(
                "LastCloudia.MultiPlayerGame", "多人游戏",
                "自动选房、刷新列表、准备、战斗及结算，按预置规则完整执行。",
                LastCloudiaTaskKind.RuleScript,
                Path.Combine(TemplateLayout.DirectoryFor(root, "LastCloudia", TemplateKind.Rule), "MultiPlayerGame.rules.json")),
            new(
                "LastCloudia.WeekRound", "周回活动",
                "固定执行四次单人核心流程，每次在确认继续再战后进入下一次。",
                LastCloudiaTaskKind.LinearScript,
                Path.Combine(TemplateLayout.DirectoryFor(root, "LastCloudia", TemplateKind.LinearScript), "WeekRound.json"))
        };
        _selectedModule = Modules[0];
    }

    public ObservableCollection<LastCloudiaModuleViewModel> Modules { get; }
    public bool HasLoaded { get; private set; }
    public bool SelectionLocked
    {
        get => _selectionLocked;
        set => SetProperty(ref _selectionLocked, value);
    }

    public LastCloudiaModuleViewModel? SelectedModule
    {
        get => _selectedModule;
        set
        {
            if (SelectionLocked || value is null || !Modules.Contains(value)) return;
            SetProperty(ref _selectedModule, value);
        }
    }

    public (ConditionLibrary Conditions, ActionBlockLibrary Blocks) LoadLibraries()
    {
        var conditions = new ConditionLibrary();
        var blocks = new ActionBlockLibrary();
        foreach (var warning in conditions.LoadGame("LastCloudia")) _log(warning);
        foreach (var warning in blocks.LoadGame("LastCloudia")) _log(warning);
        return (conditions, blocks);
    }

    public void ReloadAll()
    {
        var libraries = LoadLibraries();
        foreach (var module in Modules)
            module.Reload(libraries.Conditions, libraries.Blocks, _log);
        HasLoaded = true;
        OnPropertyChanged(nameof(HasLoaded));
    }

    public void ReloadSelected()
    {
        var module = SelectedModule ?? throw new InvalidOperationException("请先选择功能。");
        var libraries = LoadLibraries();
        module.Reload(libraries.Conditions, libraries.Blocks, _log);
        HasLoaded = true;
        OnPropertyChanged(nameof(HasLoaded));
    }
}
