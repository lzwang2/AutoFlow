namespace AutoFlow.App.ViewModels;

/// <summary>
/// A selectable target window in the picker dropdown
/// </summary>
public sealed record WindowOption(nint Handle, string Title)
{
    public override string ToString() => Title;
}
