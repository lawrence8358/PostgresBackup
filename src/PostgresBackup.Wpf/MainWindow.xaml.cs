using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
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
        ShowPage(_vm.Navigation.CurrentPage);

        Loaded += MainWindow_Loaded;
        _vm.Navigation.PropertyChanged += OnNavigationChanged;
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

    private void OnNavigationChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainNavigation.CurrentPage)) return;

        Dispatcher.Invoke(() =>
        {
            SelectNavigationItem(_vm.Navigation.CurrentPage);
            ShowPage(_vm.Navigation.CurrentPage);
        });
    }

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb &&
            rb.Tag is string pageName &&
            Enum.TryParse<MainPage>(pageName, out var destination))
        {
            _vm.Navigation.NavigateTo(destination);
        }
    }

    public void SwitchToPage(string pageName)
    {
        if (Enum.TryParse<MainPage>(pageName, out var destination))
        {
            if (_vm.Navigation.CurrentPage == destination)
            {
                SelectNavigationItem(destination);
                ShowPage(destination);
            }
            else
            {
                _vm.Navigation.NavigateTo(destination);
            }
            return;
        }

        ShowUnknownPage(pageName);
    }

    private void SelectNavigationItem(MainPage destination)
    {
        switch (destination)
        {
            case MainPage.Settings:
                NavSettings.IsChecked = true;
                break;
            case MainPage.Backup:
                NavBackup.IsChecked = true;
                break;
            case MainPage.Restore:
                NavRestore.IsChecked = true;
                break;
            case MainPage.History:
                NavHistory.IsChecked = true;
                break;
        }
    }

    private void ShowPage(MainPage destination)
    {
        if (MainContent == null) return;

        switch (destination)
        {
            case MainPage.Settings:
                MainContent.Content = MainContent.Resources["SettingsPage"];
                break;
            case MainPage.Backup:
                _ = _vm.Backup.InitializeAsync();
                MainContent.Content = MainContent.Resources["BackupPage"];
                break;
            case MainPage.Restore:
                _ = _vm.Restore.InitializeAsync();
                MainContent.Content = MainContent.Resources["RestorePage"];
                break;
            case MainPage.History:
                _ = _vm.History.LoadRecordsAsync();
                MainContent.Content = MainContent.Resources["HistoryPage"];
                break;
        }
    }

    private void ShowUnknownPage(string pageName)
    {
        if (MainContent == null) return;

        MainContent.Content = new Border
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
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is LocalizationService.Language lang)
        {
            LocalizationService.Instance.SetLanguage(lang.Code);
        }
    }
}
