using System.Windows;

namespace PostgresBackup.Wpf.Services;

public sealed class WpfClipboardService : IClipboardService
{
    public void SetText(string text) => Clipboard.SetText(text);
}
