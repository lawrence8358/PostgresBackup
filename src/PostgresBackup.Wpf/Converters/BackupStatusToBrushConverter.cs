using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PostgresBackup.Core.Models;

namespace PostgresBackup.Wpf.Converters;

public class BackupStatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x2E, 0x7D, 0x32)); // Green
    private static readonly SolidColorBrush FailedBrush = new(Color.FromRgb(0xD3, 0x2F, 0x2F));  // Red
    private static readonly SolidColorBrush DefaultBrush = new(Color.FromRgb(0x75, 0x75, 0x75)); // Grey

    static BackupStatusToBrushConverter()
    {
        SuccessBrush.Freeze();
        FailedBrush.Freeze();
        DefaultBrush.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is BackupStatus status)
        {
            return status switch
            {
                BackupStatus.Success => SuccessBrush,
                BackupStatus.Failed => FailedBrush,
                _ => DefaultBrush
            };
        }

        return DefaultBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
