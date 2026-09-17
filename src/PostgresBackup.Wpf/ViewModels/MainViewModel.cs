using CommunityToolkit.Mvvm.ComponentModel;

namespace PostgresBackup.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private string _windowTitle = "PostgresBackup — PostgreSQL 官方工具備份與還原";

    public SettingsViewModel Settings { get; }

    public MainViewModel(SettingsViewModel settings)
    {
        Settings = settings;
    }
}
