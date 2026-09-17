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

        // 系統啟動時自動掃描客戶端工具
        _ = _vm.Settings.DetectToolsAsync();
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

        if (pageName == "Settings")
        {
            MainContent.Content = MainContent.Resources["SettingsPage"];
        }
        else
        {
            // 其他尚未實作之分頁提示
            var placeholder = new Border
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = new TextBlock
                {
                    Text = $"「{pageName}」模組將於後續 Ticket 實作",
                    FontSize = 16,
                    Foreground = System.Windows.Media.Brushes.Gray
                }
            };
            MainContent.Content = placeholder;
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
