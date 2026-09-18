using CommunityToolkit.Mvvm.ComponentModel;
using PostgresBackup.Core.Models;
using PostgresBackup.Wpf.Services;

namespace PostgresBackup.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public string WindowTitle => LocalizationService.S("App_WindowTitle");

    public SettingsViewModel Settings { get; }
    public BackupViewModel Backup { get; }
    public RestoreViewModel Restore { get; }
    public HistoryViewModel History { get; }
    public LogViewModel Log { get; }

    public event Action<string>? NavigationRequested;

    public MainViewModel(
        SettingsViewModel settings,
        BackupViewModel backup,
        RestoreViewModel restore,
        HistoryViewModel history,
        LogViewModel log)
    {
        Settings = settings;
        Backup = backup;
        Restore = restore;
        History = history;
        Log = log;

        History.RequestRestore += OnRequestRestore;

        LocalizationService.Instance.PropertyChanged += (_, _) => OnPropertyChanged(nameof(WindowTitle));
    }

    private void OnRequestRestore(BackupRecord record)
    {
        Restore.SetRestoreTarget(record.FilePath, record.DatabaseName);
        NavigationRequested?.Invoke("Restore");
    }
}
