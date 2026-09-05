using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace AutoFlow.App.Common;

/// <summary>
/// Visible when the bound value is not null, Collapsed otherwise.
///
/// Distinct from chaining <see cref="NullToBoolConverter"/> into a BooleanToVisibilityConverter,
/// which WPF cannot do in a single binding, and from binding IsEnabled instead: a greyed-out
/// panel of empty boxes reads as "broken", whereas a collapsed one reads as "nothing selected".
/// </summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
