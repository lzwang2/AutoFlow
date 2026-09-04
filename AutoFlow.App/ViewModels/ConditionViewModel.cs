using AutoFlow.App.Common;
using AutoFlow.Core.Scripting.Conditions;

namespace AutoFlow.App.ViewModels;

/// <summary>
/// Editable wrapper around a shared <see cref="VisualConditionSpec"/>.
///
/// Edits go straight to the underlying spec, which is the same instance the condition
/// library holds. That is deliberate: a rule referencing the condition should show the
/// edited values immediately rather than after a save/reload round trip. Persisting is a
/// separate explicit step (see MainViewModel.SaveCondition), so nothing reaches disk until
/// the user asks for it.
/// </summary>
public sealed class ConditionViewModel : ObservableObject
{
    private readonly VisualConditionSpec _model;

    public ConditionViewModel(VisualConditionSpec model)
    {
        _model = model;
    }

    public VisualConditionSpec Model => _model;

    /// <summary>
    /// Id is intentionally read-only here. Renaming it would silently break every rule
    /// pointing at the old value; changing the reference is done on the rule side instead.
    /// </summary>
    public string Id => _model.Id;

    public string Name
    {
        get => _model.Name;
        set { _model.Name = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayText)); }
    }

    public string? Description
    {
        get => _model.Description;
        set { _model.Description = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// The authored, library-relative image path — e.g. "LastCloudia/images/welcome.png".
    /// Editing this clears the cached resolved path, otherwise matching would keep opening
    /// the old image while the editor displays the new one.
    /// </summary>
    public string? TemplatePath
    {
        get => _model.TemplatePath;
        set
        {
            _model.TemplatePath = value;
            _model.ResolvedTemplatePath = null;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayText));
        }
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
        set { _model.RegionX1 = value; OnPropertyChanged(); RegionChanged(); }
    }

    public int RegionY1
    {
        get => _model.RegionY1;
        set { _model.RegionY1 = value; OnPropertyChanged(); RegionChanged(); }
    }

    public int RegionX2
    {
        get => _model.RegionX2;
        set { _model.RegionX2 = value; OnPropertyChanged(); RegionChanged(); }
    }

    public int RegionY2
    {
        get => _model.RegionY2;
        set { _model.RegionY2 = value; OnPropertyChanged(); RegionChanged(); }
    }

    /// <summary>
    /// The window outer size the region coordinates were measured against. Without it the
    /// coordinates cannot be checked against the script using them (FR-10).
    /// </summary>
    public int ReferenceOuterWidth
    {
        get => _model.ReferenceOuterWidth;
        set { _model.ReferenceOuterWidth = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReferenceSummary)); }
    }

    public int ReferenceOuterHeight
    {
        get => _model.ReferenceOuterHeight;
        set { _model.ReferenceOuterHeight = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReferenceSummary)); }
    }

    public string RegionSummary => _model.HasRegion
        ? $"区域 ({RegionX1},{RegionY1})-({RegionX2},{RegionY2})"
        : "整帧匹配";

    public string ReferenceSummary => ReferenceOuterWidth > 0 && ReferenceOuterHeight > 0
        ? $"参照空间 {ReferenceOuterWidth}×{ReferenceOuterHeight}"
        : "未声明参照空间";

    /// <summary>Label for the condition picker: Id is the reference, Name is for humans.</summary>
    public string DisplayText => string.IsNullOrWhiteSpace(Name) ? Id : $"{Id} — {Name}";

    private void RegionChanged()
    {
        OnPropertyChanged(nameof(RegionSummary));
    }

    /// <summary>Re-raises every property, for when the model was changed from outside.</summary>
    public void RefreshAll()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(TemplatePath));
        OnPropertyChanged(nameof(MatchThreshold));
        OnPropertyChanged(nameof(MeanDiffThreshold));
        OnPropertyChanged(nameof(RegionX1));
        OnPropertyChanged(nameof(RegionY1));
        OnPropertyChanged(nameof(RegionX2));
        OnPropertyChanged(nameof(RegionY2));
        OnPropertyChanged(nameof(ReferenceOuterWidth));
        OnPropertyChanged(nameof(ReferenceOuterHeight));
        OnPropertyChanged(nameof(RegionSummary));
        OnPropertyChanged(nameof(ReferenceSummary));
        OnPropertyChanged(nameof(DisplayText));
    }
}
