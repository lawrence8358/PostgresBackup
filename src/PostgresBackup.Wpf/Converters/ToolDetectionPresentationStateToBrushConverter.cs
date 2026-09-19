using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using PostgresBackup.Wpf.ViewModels;

namespace PostgresBackup.Wpf.Converters;

public class ToolDetectionPresentationStateToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush NeutralBrush = new(Color.FromRgb(0x75, 0x75, 0x75));
    private static readonly SolidColorBrush DetectingBrush = new(Color.FromRgb(0x15, 0x65, 0xC0));
    private static readonly SolidColorBrush ReadyBrush = new(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly SolidColorBrush IncompatibleBrush = new(Color.FromRgb(0xF5, 0x7F, 0x17));
    private static readonly SolidColorBrush NotFoundBrush = new(Color.FromRgb(0xD3, 0x2F, 0x2F));

    static ToolDetectionPresentationStateToBrushConverter()
    {
        NeutralBrush.Freeze();
        DetectingBrush.Freeze();
        ReadyBrush.Freeze();
        IncompatibleBrush.Freeze();
        NotFoundBrush.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        ToolDetectionPresentationState.Detecting => DetectingBrush,
        ToolDetectionPresentationState.Ready => ReadyBrush,
        ToolDetectionPresentationState.Incompatible => IncompatibleBrush,
        ToolDetectionPresentationState.NotFound => NotFoundBrush,
        _ => NeutralBrush
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
