using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PostgresBackup.Wpf.ViewModels;

namespace PostgresBackup.Wpf.Converters;

public sealed class OperationStatusToBrushConverter : IValueConverter
{
    private static readonly Brush SuccessBrush = CreateBrush(0x2E, 0x7D, 0x32);
    private static readonly Brush RunningBrush = CreateBrush(0x19, 0x76, 0xD2);
    private static readonly Brush FailureBrush = CreateBrush(0xD3, 0x2F, 0x2F);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is OperationStatusKind.Running
            ? RunningBrush
            : value is OperationStatusKind.Failed or OperationStatusKind.Error
                ? FailureBrush
                : SuccessBrush;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    private static Brush CreateBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
