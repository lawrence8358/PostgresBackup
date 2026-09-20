using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

public class RestoreTargetDatabaseTests
{
    private sealed class CapturingProcessRunner : IProcessRunner
    {
        public string? CapturedArguments { get; private set; }

        public Task<ProcessResult> RunAsync(
            string executable,
            string arguments,
            IDictionary<string, string?>? environmentVariables = null,
            Action<string>? onOutputLine = null,
            Action<string>? onErrorLine = null,
            CancellationToken ct = default)
        {
            CapturedArguments = arguments;
            return Task.FromResult(new ProcessResult(0, string.Empty, string.Empty));
        }
    }

    [Fact]
    public async Task CleanAndRecreate_restores_into_the_selected_existing_database()
    {
        var options = new RestoreOptions
        {
            Connection = new ConnectionSettings
            {
                Host = "localhost",
                Port = 5432,
                Username = "postgres",
                Database = "stock_analysis2"
            },
            TargetDatabase = "stock_analysis2",
            SourceFilePath = @"C:\backups\stock_analysis.dump",
            Format = BackupFormat.Custom,
            Mode = RestoreMode.CleanAndRecreate
        };

        var args = RestoreArgumentsBuilder.Build(options);

        Assert.Equal(["--clean", "--if-exists"], args.Take(2));
        Assert.DoesNotContain("--create", args);

        // 選定的目標資料庫要真的抵達 pg_restore：-d 由客戶端工具作業補上，
        // 構建器的清單裡看不到它，因此這裡跑完整條拼接再比對命令列。
        var runner = new CapturingProcessRunner();
        await new ClientToolRun(runner).RunAsync(new ClientToolRunRequest
        {
            ExecutablePath = @"C:\pg\bin\pg_restore.exe",
            Arguments = args,
            Connection = options.Connection,
            TargetDatabase = options.TargetDatabase,
            LogPrefix = "pg_restore",
            NonZeroExitErrorKey = "Restore_Error_NonZeroExit",
            RecordedFilePath = options.SourceFilePath
        });

        Assert.Contains("-d stock_analysis2", runner.CapturedArguments);
        Assert.Contains("--clean --if-exists", runner.CapturedArguments);
    }
}
