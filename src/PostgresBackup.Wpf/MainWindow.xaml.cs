using System.Windows;
using System.Windows.Controls;
using PostgresBackup.Wpf.Services;
using PostgresBackup.Wpf.ViewModels;

namespace PostgresBackup.Wpf;

public partial class MainWindow
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        InitializeComponent();

        Loaded += MainWindow_Loaded;
        _vm.NavigationRequested += OnNavigationRequested;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // 設定語系切換器初值
        var currentLang = LocalizationService.Instance.CurrentLanguageCode;
        foreach (var item in LanguageComboBox.Items)
        {
            if (item is LocalizationService.Language lang && lang.Code == currentLang)
            {
                LanguageComboBox.SelectedItem = lang;
                break;
            }
        }

        // 預設切換至 Settings 頁面
        SwitchToPage("Settings");

        // 系統啟動時初始化連線設定檔並掃描客戶端工具
        _ = _vm.Settings.InitializeAsync();
    }

    private void OnNavigationRequested(string pageName)
    {
        Dispatcher.Invoke(() =>
        {
            switch (pageName)
            {
                case "Settings":
                    NavSettings.IsChecked = true;
                    break;
                case "Backup":
                    NavBackup.IsChecked = true;
                    break;
                case "Restore":
                    NavRestore.IsChecked = true;
                    break;
                case "History":
                    NavHistory.IsChecked = true;
                    break;
                case "Log":
                    NavLog.IsChecked = true;
                    break;
            }
            SwitchToPage(pageName);
        });
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string pageName)
        {
            SwitchToPage(pageName);
        }
    }

    private void SwitchToPage(string pageName)
    {
        if (MainContent == null) return;

        switch (pageName)
        {
            case "Settings":
                MainContent.Content = MainContent.Resources["SettingsPage"];
                break;
            case "Backup":
                _ = _vm.Backup.InitializeAsync();
                MainContent.Content = MainContent.Resources["BackupPage"];
                break;
            case "Restore":
                _ = _vm.Restore.InitializeAsync();
                MainContent.Content = MainContent.Resources["RestorePage"];
                break;
            case "History":
                _ = _vm.History.LoadRecordsAsync();
                MainContent.Content = MainContent.Resources["HistoryPage"];
                break;
            case "Log":
                MainContent.Content = MainContent.Resources["LogPage"];
                break;
            default:
                var placeholder = new Border
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = $"「{pageName}」模組",
                        FontSize = 16,
                        Foreground = System.Windows.Media.Brushes.Gray
                    }
                };
                MainContent.Content = placeholder;
                break;
        }
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is LocalizationService.Language lang)
        {
            LocalizationService.Instance.SetLanguage(lang.Code);
        }
    }
}
