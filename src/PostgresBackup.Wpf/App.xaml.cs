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

        // 註冊 ViewModels
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();

        // 註冊視窗
        services.AddSingleton<MainWindow>();

        Services = services.BuildServiceProvider();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }
}
