using System.CommandLine;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PostgresBackup.Cli.Commands;
using PostgresBackup.Cli.Services;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Resources;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // CLI 目前的介面文字為繁體中文，故將 Core 層訊息對齊系統語系，
        // 避免在中文環境下輸出中英夾雜的訊息。
        CoreStrings.SetCulture(CultureInfo.CurrentUICulture);

        var services = ConfigureServices();

        var rootCommand = new RootCommand("PostgresBackup CLI — PostgreSQL 官方工具備份與還原管理命令列介面");
        rootCommand.Add(CheckToolsCommand.Create(services));
        rootCommand.Add(BackupCommand.Create(services));
        rootCommand.Add(RestoreCommand.Create(services));
        rootCommand.Add(ProfileCommand.Create(services));

        return await rootCommand.Parse(args).InvokeAsync(new InvocationConfiguration(), CancellationToken.None);
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IEnvironmentProbe, WindowsEnvironmentProbe>();
        services.AddSingleton<IToolDetectionService, ToolDetectionService>();
        // 命令列使用機器範圍存放區：以 SYSTEM 身分執行的排程任務才讀得到。
        // 圖形介面維持使用者範圍實作，兩者在執行期完全看不到對方的資料。
        // 位置與權限政策由單一物件提供，讓「檢查權限的目錄」與「實際寫入的目錄」
        // 不可能脫鉤——存放區、憑證存放區與 profile 指令都取用同一個實例。
        services.AddSingleton(MachineScopedStoreLocation.Default);
        services.AddSingleton<ICredentialStorage>(sp =>
            new MachineScopedCredentialStorage(sp.GetRequiredService<MachineScopedStoreLocation>()));
        services.AddSingleton<IConnectionProfileRepository>(sp =>
            new MachineScopedConnectionProfileRepository(
                sp.GetRequiredService<ICredentialStorage>(),
                sp.GetRequiredService<MachineScopedStoreLocation>()));
        services.AddSingleton<IPasswordReader, ConsolePasswordReader>();
        services.AddSingleton<IBackupHistoryRepository, SqliteBackupHistoryRepository>();
        services.AddSingleton<ClientToolRun>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IRestoreService, RestoreService>();

        return services.BuildServiceProvider();
    }
}
