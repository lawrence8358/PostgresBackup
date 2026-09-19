using CommunityToolkit.Mvvm.ComponentModel;

namespace PostgresBackup.Wpf.ViewModels;

public enum MainPage
{
    Settings,
    Backup,
    Restore,
    History
}

public partial class MainNavigation : ObservableObject
{
    [ObservableProperty]
    private MainPage _currentPage = MainPage.Settings;

    public void NavigateTo(MainPage destination)
    {
        CurrentPage = destination;
    }
}
