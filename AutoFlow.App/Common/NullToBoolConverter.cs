using System.Globalization;
using System.Windows.Data;

namespace AutoFlow.App.Common;

/// <summary>
/// Returns true when the bound value is not null (used to enable panels only when an item is selected)
/// </summary>
public sealed class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
