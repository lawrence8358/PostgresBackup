using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PostgresBackup.Wpf.ViewModels;

public partial class LogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _logContent = "=== PostgresBackup 系統日誌記錄 ===\n";

    private readonly StringBuilder _builder = new("=== PostgresBackup 系統日誌記錄 ===\n");

    public void AppendLog(string message)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            _builder.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
            LogContent = _builder.ToString();
        });
    }

    [RelayCommand]
    public void ClearLog()
    {
        _builder.Clear();
        _builder.AppendLine("=== PostgresBackup 系統日誌記錄 (已清空) ===\n");
        LogContent = _builder.ToString();
    }

    [RelayCommand]
    public void CopyLog()
    {
        if (!string.IsNullOrEmpty(LogContent))
        {
            Clipboard.SetText(LogContent);
        }
    }
}
