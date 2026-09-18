using System.CommandLine;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PostgresBackup.Cli.Commands;
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
        services.AddSingleton<ICredentialStorage, WindowsCredentialStorage>();
        services.AddSingleton<IConnectionProfileRepository, JsonConnectionProfileRepository>();
        services.AddSingleton<IBackupHistoryRepository, SqliteBackupHistoryRepository>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<IRestoreService, RestoreService>();

        return services.BuildServiceProvider();
    }
}
