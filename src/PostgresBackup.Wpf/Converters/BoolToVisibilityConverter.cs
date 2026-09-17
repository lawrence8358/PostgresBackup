using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PostgresBackup.Wpf.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is true;
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool isVis = value is Visibility v && v == Visibility.Visible;
        return Invert ? !isVis : isVis;
    }
}
