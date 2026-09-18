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

        // NavSettings 的 Checked 事件在 InitializeComponent 解析左側邊欄時即觸發，
        // 此時右側 MainContent 尚未建立，SwitchToPage 會被 null 防護提早返回，
        // 導致啟動後內容區域空白。故於樹狀結構建立完成後補呼叫一次初始導覽。
        SwitchToPage("Settings");

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

        // 確保視窗高度與寬度不超出可用工作區 (工作列上方)
        if (Height > SystemParameters.WorkArea.Height)
        {
            Height = Math.Max(480, SystemParameters.WorkArea.Height - 30);
        }
        if (Width > SystemParameters.WorkArea.Width)
        {
            Width = Math.Max(720, SystemParameters.WorkArea.Width - 30);
        }

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

    public void SwitchToPage(string pageName)
    {
        if (MainContent == null) return;

        switch (pageName)
        {
            case "Settings":
                if (NavSettings != null && NavSettings.IsChecked != true) NavSettings.IsChecked = true;
                MainContent.Content = MainContent.Resources["SettingsPage"];
                break;
            case "Backup":
                if (NavBackup != null && NavBackup.IsChecked != true) NavBackup.IsChecked = true;
                _ = _vm.Backup.InitializeAsync();
                MainContent.Content = MainContent.Resources["BackupPage"];
                break;
            case "Restore":
                if (NavRestore != null && NavRestore.IsChecked != true) NavRestore.IsChecked = true;
                _ = _vm.Restore.InitializeAsync();
                MainContent.Content = MainContent.Resources["RestorePage"];
                break;
            case "History":
                if (NavHistory != null && NavHistory.IsChecked != true) NavHistory.IsChecked = true;
                _ = _vm.History.LoadRecordsAsync();
                MainContent.Content = MainContent.Resources["HistoryPage"];
                break;
            case "Log":
                if (NavLog != null && NavLog.IsChecked != true) NavLog.IsChecked = true;
                MainContent.Content = MainContent.Resources["LogPage"];
                break;
            default:
                var placeholder = new Border
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = LocalizationService.S("Nav_Placeholder", pageName),
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
