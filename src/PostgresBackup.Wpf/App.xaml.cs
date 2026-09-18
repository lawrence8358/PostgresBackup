using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Services;
using PostgresBackup.Wpf.Services;
using PostgresBackup.Wpf.ViewModels;

namespace PostgresBackup.Wpf;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        LocalizationService.Instance.InitFromSystem();

        var services = new ServiceCollection();

        // 註冊核心服務
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IEnvironmentProbe, WindowsEnvironmentProbe>();
        services.AddSingleton<IToolDetectionService, ToolDetectionService>();
        services.AddSingleton<ICredentialStorage, WindowsCredentialStorage>();
        services.AddSingleton<IConnectionProfileRepository, JsonConnectionProfileRepository>();
        services.AddSingleton<IClientToolRunner, ClientToolRunner>();
        services.AddSingleton<IBackupHistoryRepository, SqliteBackupHistoryRepository>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IRestoreService, RestoreService>();

        // 註冊 ViewModels
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<BackupViewModel>();
        services.AddSingleton<RestoreViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<LogViewModel>();
        services.AddSingleton<MainViewModel>();

        // 註冊視窗
        services.AddSingleton<MainWindow>();

        Services = services.BuildServiceProvider();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();


        // 支援自動化截圖旗標 --capture <outputDir> [--custom-tools <toolsDir>]
        if (UiCaptureService.IsCaptureMode(e.Args))
        {
            _ = UiCaptureService.RunCaptureAsync(mainWindow, Services, e.Args);
        }
    }
}
