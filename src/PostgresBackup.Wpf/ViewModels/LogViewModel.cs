using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PostgresBackup.Wpf.Services;

namespace PostgresBackup.Wpf.ViewModels;

public partial class LogViewModel : ObservableObject
{
    // 標頭與日誌內文分開保存，語系切換時才能單獨重新在地化標頭而不影響既有紀錄。
    private readonly StringBuilder _entries = new();
    private bool _isCleared;

    public LogViewModel()
    {
        LocalizationService.Instance.PropertyChanged += (_, _) => OnPropertyChanged(nameof(LogContent));
    }

    public string LogContent
    {
        get
        {
            var header = LocalizationService.S(_isCleared ? "Log_Header_Cleared" : "Log_Header");
            return _entries.Length == 0 ? header : $"{header}{Environment.NewLine}{_entries}";
        }
    }

    public void AppendLog(string message)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            if (_entries.Length > 0)
            {
                _entries.AppendLine();
            }
            _entries.Append($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");
            _isCleared = false;
            OnPropertyChanged(nameof(LogContent));
        });
    }

    [RelayCommand]
    public void ClearLog()
    {
        _entries.Clear();
        _isCleared = true;
        OnPropertyChanged(nameof(LogContent));
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
