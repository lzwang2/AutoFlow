using System.Collections.ObjectModel;
using AutoFlow.App.Common;
using AutoFlow.Core.Scripting.Rules;

namespace AutoFlow.App.ViewModels;

/// <summary>
/// Editable wrapper around a <see cref="Rule"/>. Actions are wrapped as
/// <see cref="InstructionViewModel"/> so they reuse the existing instruction editor.
/// </summary>
public sealed class RuleViewModel : ObservableObject
{
    private readonly Rule _model;

    public RuleViewModel(Rule model)
    {
        _model = model;
        Actions = new ObservableCollection<InstructionViewModel>();
        foreach (var action in _model.Actions)
        {
            Actions.Add(new InstructionViewModel(action));
        }
        Actions.CollectionChanged += (_, _) => SyncActions();
    }

    public Rule Model => _model;

    public ObservableCollection<InstructionViewModel> Actions { get; }

    /// <summary>Rebuild the underlying model's Actions list from the view models.</summary>
    public void SyncActions()
    {
        _model.Actions.Clear();
        foreach (var vm in Actions)
        {
            _model.Actions.Add(vm.Model);
        }
        OnPropertyChanged(nameof(Summary));
    }

    public string Name
    {
        get => _model.Name;
        set { _model.Name = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public string? Description
    {
        get => _model.Description;
        set { _model.Description = value; OnPropertyChanged(); }
    }

    public bool Enabled
    {
        get => _model.Enabled;
        set { _model.Enabled = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public string? TemplatePath
    {
        get => _model.TemplatePath;
        set { _model.TemplatePath = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public double MatchThreshold
    {
        get => _model.MatchThreshold;
        set { _model.MatchThreshold = value; OnPropertyChanged(); }
    }

    public double MeanDiffThreshold
    {
        get => _model.MeanDiffThreshold;
        set { _model.MeanDiffThreshold = value; OnPropertyChanged(); }
    }

    public int RegionX1
    {
        get => _model.RegionX1;
        set { _model.RegionX1 = value; OnPropertyChanged(); OnPropertyChanged(nameof(RegionSummary)); }
    }

    public int RegionY1
    {
        get => _model.RegionY1;
        set { _model.RegionY1 = value; OnPropertyChanged(); OnPropertyChanged(nameof(RegionSummary)); }
    }

    public int RegionX2
    {
        get => _model.RegionX2;
        set { _model.RegionX2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(RegionSummary)); }
    }

    public int RegionY2
    {
        get => _model.RegionY2;
        set { _model.RegionY2 = value; OnPropertyChanged(); OnPropertyChanged(nameof(RegionSummary)); }
    }

    public bool TriggerOnce
    {
        get => _model.TriggerOnce;
        set { _model.TriggerOnce = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public string RegionSummary => _model.HasRegion
        ? $"区域 ({RegionX1},{RegionY1})-({RegionX2},{RegionY2})"
        : "全屏匹配";

    public string Summary
    {
        get
        {
            var flag = Enabled ? "" : "[停用] ";
            var once = TriggerOnce ? " (仅一次)" : "";
            var tpl = string.IsNullOrEmpty(TemplatePath) ? "?" : System.IO.Path.GetFileNameWithoutExtension(TemplatePath);
            return $"{flag}若[{tpl}] → {Actions.Count}个动作{once}";
        }
    }

    public void RefreshSummary()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(RegionSummary));
    }

    /// <summary>
    /// Get the underlying model (syncing actions first)
    /// </summary>
    public Rule ToModel()
    {
        SyncActions();
        return _model;
    }
}
