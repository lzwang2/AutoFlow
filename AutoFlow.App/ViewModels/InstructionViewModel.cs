using AutoFlow.App.Common;
using AutoFlow.Core.Scripting.Instructions;

namespace AutoFlow.App.ViewModels;

/// <summary>
/// Editable wrapper around an Instruction model
/// </summary>
public sealed class InstructionViewModel : ObservableObject
{
    private readonly Instruction _model;

    public InstructionViewModel(Instruction model)
    {
        _model = model;
    }

    public Instruction Model => _model;

    public CommandType CommandType
    {
        get => _model.CommandType;
        set
        {
            if (_model.CommandType != value)
            {
                _model.CommandType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public double PositionX
    {
        get => _model.PositionX;
        set { _model.PositionX = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public double PositionY
    {
        get => _model.PositionY;
        set { _model.PositionY = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
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

    public int WaitTimeoutMs
    {
        get => _model.WaitTimeoutMs;
        set { _model.WaitTimeoutMs = value; OnPropertyChanged(); }
    }

    public bool ClickAfterMatch
    {
        get => _model.ClickAfterMatch;
        set { _model.ClickAfterMatch = value; OnPropertyChanged(); }
    }

    public string? WaitColorHex
    {
        get => _model.WaitColorHex;
        set { _model.WaitColorHex = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public int WaitColorTolerance
    {
        get => _model.WaitColorTolerance;
        set { _model.WaitColorTolerance = value; OnPropertyChanged(); }
    }

    public int WaitTimeMilliseconds
    {
        get => _model.WaitTimeMilliseconds;
        set { _model.WaitTimeMilliseconds = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public int LoopBackToIndex
    {
        get => _model.LoopBackToIndex;
        set { _model.LoopBackToIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public int LoopCount
    {
        get => _model.LoopCount;
        set { _model.LoopCount = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public string? CommentContent
    {
        get => _model.CommentContent;
        set { _model.CommentContent = value; OnPropertyChanged(); OnPropertyChanged(nameof(Summary)); }
    }

    public int IntervalToNextInstructionMs
    {
        get => _model.IntervalToNextInstructionMs;
        set { _model.IntervalToNextInstructionMs = value; OnPropertyChanged(); }
    }

    /// <summary>
    /// Human-readable one-line summary for the instruction list
    /// </summary>
    public string Summary => CommandType switch
    {
        CommandType.Click => $"点击 ({PositionX:0}, {PositionY:0})",
        CommandType.WaitImage => $"等待图像 {System.IO.Path.GetFileName(TemplatePath ?? "?")}" + (ClickAfterMatch ? " 并点击" : ""),
        CommandType.WaitColor => $"等待颜色 {WaitColorHex} @ ({PositionX:0}, {PositionY:0})",
        CommandType.WaitTime => $"等待 {WaitTimeMilliseconds}ms",
        CommandType.Loop => LoopCount > 0 ? $"循环到 #{LoopBackToIndex}, {LoopCount}次" : $"循环到 #{LoopBackToIndex} (无限)",
        CommandType.Comment => $"// {CommentContent}",
        _ => CommandType.ToString()
    };

    public void RefreshAll()
    {
        OnPropertyChanged(string.Empty);
    }
}
