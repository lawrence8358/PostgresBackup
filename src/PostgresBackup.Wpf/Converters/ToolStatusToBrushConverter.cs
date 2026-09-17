using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PostgresBackup.Core.Models;

namespace PostgresBackup.Wpf.Converters;

public class ToolStatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ReadyBrush = new(Color.FromRgb(0x2E, 0x7D, 0x32));       // Green
    private static readonly SolidColorBrush IncompatibleBrush = new(Color.FromRgb(0xF5, 0x7F, 0x17));  // Amber / Warning
    private static readonly SolidColorBrush NotFoundBrush = new(Color.FromRgb(0xD3, 0x2F, 0x2F));      // Red
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(0x9E, 0x9E, 0x9E));       // Grey

    static ToolStatusToBrushConverter()
    {
        ReadyBrush.Freeze();
        IncompatibleBrush.Freeze();
        NotFoundBrush.Freeze();
        DefaultBrush.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ToolStatus status)
        {
            return status switch
            {
                ToolStatus.Ready => ReadyBrush,
                ToolStatus.Incompatible => IncompatibleBrush,
                ToolStatus.NotFound => NotFoundBrush,
                _ => DefaultBrush
            };
        }

        return DefaultBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw freshNotSupported();

    private static NotSupportedException freshNotSupported() => new();
}
