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
    public MainNavigation Navigation { get; } = new();

    public string VersionDisplay => LocalizationService.S("App_Version_Format", ApplicationVersion.Current);

    public MainViewModel(
        SettingsViewModel settings,
        BackupViewModel backup,
        RestoreViewModel restore,
        HistoryViewModel history)
    {
        Settings = settings;
        Backup = backup;
        Restore = restore;
        History = history;
        History.RequestRestore += OnRequestRestore;

        LocalizationService.Instance.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(VersionDisplay));
        };
    }

    private void OnRequestRestore(BackupRecord record)
    {
        Restore.SetRestoreTarget(record.FilePath, record.DatabaseName);
        Navigation.NavigateTo(MainPage.Restore);
    }
}
