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

        // --lang <code> 可覆寫系統語系偵測結果（供自動化截圖與整合測試使用）
        LocalizationService.Instance.InitFromSystem(GetLanguageOverride(e.Args));

        var services = new ServiceCollection();

        // 註冊核心服務
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IEnvironmentProbe, WindowsEnvironmentProbe>();
        services.AddSingleton<IToolDetectionService, ToolDetectionService>();
        services.AddSingleton<IClientToolPreferencesStore, JsonClientToolPreferencesStore>();
        services.AddSingleton<IClipboardService, WpfClipboardService>();
        services.AddSingleton<ICredentialStorage, WindowsCredentialStorage>();
        services.AddSingleton<IConnectionProfileRepository, JsonConnectionProfileRepository>();
        services.AddSingleton<IBackupHistoryRepository, SqliteBackupHistoryRepository>();
        services.AddSingleton<ClientToolRun>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IRestoreService, RestoreService>();

        // 註冊 ViewModels
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<BackupViewModel>();
        services.AddSingleton<RestoreViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<MainViewModel>();

        // 註冊視窗
        services.AddSingleton<MainWindow>();

        Services = services.BuildServiceProvider();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();


        // 支援自動化截圖旗標 --capture <outputDir> [--custom-tools <toolsDir>] [--lang <code>]
        if (UiCaptureService.IsCaptureMode(e.Args))
        {
            _ = UiCaptureService.RunCaptureAsync(mainWindow, Services, e.Args);
        }
    }

    /// <summary>自命令列引數取出 --lang &lt;code&gt; 指定之語系，未指定時回傳 null 以沿用系統偵測。</summary>
    private static string? GetLanguageOverride(string[] args)
    {
        var idx = Array.IndexOf(args, "--lang");
        if (idx < 0 || idx + 1 >= args.Length) return null;

        var code = args[idx + 1];
        return LocalizationService.SupportedLanguages.Any(l => l.Code == code) ? code : null;
    }
}
