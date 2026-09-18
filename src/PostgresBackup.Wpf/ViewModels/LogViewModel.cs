using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PostgresBackup.Wpf.ViewModels;

public partial class LogViewModel : ObservableObject
{
    [ObservableProperty]
    private string _logContent = "=== PostgresBackup 系統日誌記錄 ===";

    private readonly StringBuilder _builder = new("=== PostgresBackup 系統日誌記錄 ===");

    public void AppendLog(string message)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_builder.Length > 0)
            {
                _builder.AppendLine();
            }
            _builder.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
            LogContent = _builder.ToString();
        });
    }

    [RelayCommand]
    public void ClearLog()
    {
        _builder.Clear();
        _builder.Append("=== PostgresBackup 系統日誌記錄 (已清空) ===");
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
