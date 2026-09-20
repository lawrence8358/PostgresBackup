using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Core.Resources;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

/// <summary>
/// 客戶端工具作業（Client Tool Run）的直接測試。
///
/// 此模組是備份與還原兩條路徑抵達 pg_dump／pg_restore／psql 的唯一通道，
/// 環境變數政策、連線參數後備值、argv 引號規則與備份紀錄寫入都只有這一份實作，
/// 因此這裡的斷言同時守住了兩個服務的行為。
/// </summary>
public class ClientToolRunTests
{
    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public ProcessResult ResultToReturn { get; set; } = new(0, string.Empty, string.Empty);
        public Exception? ExceptionToThrow { get; set; }
        public string? CapturedExecutable { get; private set; }
        public string? CapturedArguments { get; private set; }
        public IReadOnlyDictionary<string, string?> CapturedEnvironment { get; private set; } =
            new Dictionary<string, string?>();
        public bool WasCalled { get; private set; }

        public Task<ProcessResult> RunAsync(
            string executable,
            string arguments,
            IDictionary<string, string?>? environmentVariables = null,
            Action<string>? onOutputLine = null,
            Action<string>? onErrorLine = null,
            CancellationToken ct = default)
        {
            WasCalled = true;
            CapturedExecutable = executable;
            CapturedArguments = arguments;
            CapturedEnvironment = environmentVariables?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                ?? new Dictionary<string, string?>();

            if (ExceptionToThrow is not null) throw ExceptionToThrow;

            onOutputLine?.Invoke("tool says hello");
            return Task.FromResult(ResultToReturn);
        }
    }

    private sealed class RecordingHistoryRepository : IBackupHistoryRepository
    {
        public List<BackupRecord> Records { get; } = [];
        public Exception? ExceptionToThrow { get; set; }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<BackupRecord> AddRecordAsync(BackupRecord record, CancellationToken ct = default)
        {
            if (ExceptionToThrow is not null) throw ExceptionToThrow;
            Records.Add(record);
            return Task.FromResult(record);
        }

        public Task<IReadOnlyList<BackupRecord>> GetRecordsAsync(
            BackupHistoryFilter? filter = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<BackupRecord>>(Records);

        public Task<BackupRecord?> GetRecordByIdAsync(long id, CancellationToken ct = default) =>
            Task.FromResult<BackupRecord?>(null);

        public Task<bool> DeleteRecordAsync(long id, CancellationToken ct = default) =>
            Task.FromResult(false);
    }

    private static ClientToolRunRequest Request(
        ConnectionSettings? connection = null,
        IReadOnlyList<string>? arguments = null,
        string? targetDatabase = null) => new()
        {
            ExecutablePath = @"C:\pg\bin\pg_dump.exe",
            Arguments = arguments ?? ["-Fc"],
            Connection = connection ?? new ConnectionSettings
            {
                Host = "localhost",
                Port = 5432,
                Username = "postgres",
                Database = "testdb"
            },
            TargetDatabase = targetDatabase,
            LogPrefix = "pg_dump",
            NonZeroExitErrorKey = "Backup_Error_NonZeroExit",
            OperationType = BackupOperationType.Backup,
            RecordedFilePath = @"C:\out\test.dump",
            RecordedFormat = BackupFormat.Custom
        };

    [Fact]
    public async Task RunAsync_WhenPasswordPresent_SetsPgPassword()
    {
        var runner = new RecordingProcessRunner();
        var run = new ClientToolRun(runner);

        await run.RunAsync(Request(new ConnectionSettings { Password = "s3cret" }));

        Assert.Equal("s3cret", runner.CapturedEnvironment["PGPASSWORD"]);
    }

    [Fact]
    public async Task RunAsync_WhenPasswordEmpty_DoesNotSetPgPassword()
    {
        var runner = new RecordingProcessRunner();
        var run = new ClientToolRun(runner);

        await run.RunAsync(Request(new ConnectionSettings { Password = string.Empty }));

        Assert.False(runner.CapturedEnvironment.ContainsKey("PGPASSWORD"));
    }

    [Fact]
    public async Task RunAsync_AppliesTheEncodingAndLocalePolicy()
    {
        var runner = new RecordingProcessRunner();
        var run = new ClientToolRun(runner);

        await run.RunAsync(Request());

        Assert.Equal("UTF8", runner.CapturedEnvironment["PGCLIENTENCODING"]);
        Assert.Equal("C", runner.CapturedEnvironment["LC_ALL"]);
        Assert.Equal("C", runner.CapturedEnvironment["LC_MESSAGES"]);
        Assert.Equal("C", runner.CapturedEnvironment["LANG"]);

        // null 值代表「自子處理序環境中移除該變數」，而非「設為空字串」。
        Assert.True(runner.CapturedEnvironment.ContainsKey("LANGUAGE"));
        Assert.Null(runner.CapturedEnvironment["LANGUAGE"]);
    }

    [Fact]
    public async Task RunAsync_PrependsConnectionArgumentsBeforeTheOperationArguments()
    {
        var runner = new RecordingProcessRunner();
        var run = new ClientToolRun(runner);

        await run.RunAsync(Request(
            new ConnectionSettings
            {
                Host = "db.internal",
                Port = 6543,
                Username = "dbadmin",
                Database = "mydb"
            },
            arguments: ["-Fc", "-v"]));

        Assert.Equal("-h db.internal -p 6543 -U dbadmin -d mydb -Fc -v", runner.CapturedArguments);
    }

    [Fact]
    public async Task RunAsync_WhenConnectionFieldsAreBlank_UsesTheDocumentedFallbacks()
    {
        var runner = new RecordingProcessRunner();
        var run = new ClientToolRun(runner);

        await run.RunAsync(Request(new ConnectionSettings
        {
            Host = "   ",
            Port = 0,
            Username = string.Empty,
            Database = string.Empty
        }));

        Assert.StartsWith("-h localhost -p 5432 -U postgres -d postgres ", runner.CapturedArguments);
    }

    [Fact]
    public async Task RunAsync_WhenTargetDatabaseGiven_ItWinsOverTheConnectionDatabase()
    {
        var runner = new RecordingProcessRunner();
        var run = new ClientToolRun(runner);

        await run.RunAsync(Request(
            new ConnectionSettings { Database = "source_db" },
            targetDatabase: "target_db"));

        Assert.Contains("-d target_db", runner.CapturedArguments);
        Assert.DoesNotContain("source_db", runner.CapturedArguments);
    }

    [Fact]
    public async Task RunAsync_QuotesOnlyTheArgvElementsThatNeedIt()
    {
        var runner = new RecordingProcessRunner();
        var run = new ClientToolRun(runner);

        await run.RunAsync(Request(arguments:
        [
            "-f",
            @"C:\out\no-space.dump",
            "-t",
            "a table",
            "-n",
            "say \"hi\"",
            "--label",
            string.Empty
        ]));

        var args = runner.CapturedArguments!;
        Assert.Contains(@"-f C:\out\no-space.dump", args);
        Assert.Contains("-t \"a table\"", args);
        Assert.Contains("-n \"say \\\"hi\\\"\"", args);
        Assert.Contains("--label \"\"", args);
    }

    [Fact]
    public async Task RunAsync_WhenSuccessful_WritesASuccessRecordCarryingTheJoinedCommandLine()
    {
        var runner = new RecordingProcessRunner();
        var history = new RecordingHistoryRepository();
        var run = new ClientToolRun(runner, history);

        var request = Request() with { MeasureRecordedFileSize = success => success ? 4096 : 0 };
        var result = await run.RunAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Equal(4096, result.RecordedFileSizeBytes);
        Assert.Null(result.ErrorMessage);

        var record = Assert.Single(history.Records);
        Assert.Equal(BackupStatus.Success, record.Status);
        Assert.Equal(BackupOperationType.Backup, record.OperationType);
        Assert.Equal("testdb", record.DatabaseName);
        Assert.Equal(@"C:\out\test.dump", record.FilePath);
        Assert.Equal(4096, record.FileSizeBytes);
        Assert.Equal(result.CommandLine, record.Arguments);
        Assert.Null(record.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_WhenProcessFails_WritesAFailedRecordCarryingTheErrorMessage()
    {
        var runner = new RecordingProcessRunner
        {
            ResultToReturn = new ProcessResult(2, string.Empty, "FATAL: no such database")
        };
        var history = new RecordingHistoryRepository();
        var run = new ClientToolRun(runner, history);

        var result = await run.RunAsync(Request());

        Assert.False(result.IsSuccess);
        Assert.Equal(2, result.ExitCode);
        Assert.Equal("FATAL: no such database", result.ErrorMessage);

        var record = Assert.Single(history.Records);
        Assert.Equal(BackupStatus.Failed, record.Status);
        Assert.Equal("FATAL: no such database", record.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_WhenStandardErrorIsEmpty_FallsBackToTheSuppliedResourceKey()
    {
        var runner = new RecordingProcessRunner
        {
            ResultToReturn = new ProcessResult(3, "some stdout", "   ")
        };
        var run = new ClientToolRun(runner);

        var result = await run.RunAsync(Request() with
        {
            NonZeroExitErrorKey = "Restore_Error_NonZeroExit"
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(CoreStrings.Get("Restore_Error_NonZeroExit"), result.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_WhenConfirmSuccessRejectsAZeroExit_TheRunIsAFailure()
    {
        var runner = new RecordingProcessRunner();
        var history = new RecordingHistoryRepository();
        var run = new ClientToolRun(runner, history);

        var result = await run.RunAsync(Request() with { ConfirmSuccess = () => false });

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(CoreStrings.Get("Backup_Error_NonZeroExit"), result.ErrorMessage);
        Assert.Equal(BackupStatus.Failed, Assert.Single(history.Records).Status);
    }

    [Fact]
    public async Task RunAsync_WhenTheHistoryRepositoryThrows_WarnsWithoutChangingTheOutcome()
    {
        var runner = new RecordingProcessRunner();
        var history = new RecordingHistoryRepository
        {
            ExceptionToThrow = new InvalidOperationException("history database is locked")
        };
        var run = new ClientToolRun(runner, history);

        var logs = new List<string>();
        var result = await run.RunAsync(Request(), onLogLine: logs.Add);

        Assert.True(result.IsSuccess);
        Assert.Contains(logs, l => l.Contains("[WARNING]") && l.Contains("history database is locked"));
    }

    [Fact]
    public async Task RunAsync_WithoutAHistoryRepository_StillCompletes()
    {
        var runner = new RecordingProcessRunner();
        var run = new ClientToolRun(runner, historyRepo: null);

        var result = await run.RunAsync(Request());

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task RunAsync_WhenTheRunnerThrowsNonCancellation_ProducesAFailureAndStillRecordsIt()
    {
        var runner = new RecordingProcessRunner
        {
            ExceptionToThrow = new IOException("the executable vanished")
        };
        var history = new RecordingHistoryRepository();
        var run = new ClientToolRun(runner, history);

        var result = await run.RunAsync(Request());

        Assert.False(result.IsSuccess);
        Assert.Equal(-1, result.ExitCode);
        Assert.Equal("the executable vanished", result.ErrorMessage);
        Assert.Equal(BackupStatus.Failed, Assert.Single(history.Records).Status);
    }

    [Fact]
    public async Task RunAsync_WhenCancelled_PropagatesInsteadOfRecordingAFailure()
    {
        var runner = new RecordingProcessRunner
        {
            ExceptionToThrow = new OperationCanceledException()
        };
        var history = new RecordingHistoryRepository();
        var run = new ClientToolRun(runner, history);

        await Assert.ThrowsAsync<OperationCanceledException>(() => run.RunAsync(Request()));
        Assert.Empty(history.Records);
    }

    [Fact]
    public async Task ProbeAsync_JoinsArgumentsAndAppliesTheEnvironmentPolicyWithoutConnectionArgsOrRecord()
    {
        var runner = new RecordingProcessRunner
        {
            ResultToReturn = new ProcessResult(0, "archive listing", string.Empty)
        };
        var history = new RecordingHistoryRepository();
        var run = new ClientToolRun(runner, history);

        var result = await run.ProbeAsync(
            @"C:\pg\bin\pg_restore.exe",
            ["--list", @"C:\my backups\source.dump"],
            new ConnectionSettings { Password = "s3cret", Database = "testdb" },
            "Restore_Error_NonZeroExit");

        Assert.True(result.IsSuccess);
        Assert.Equal("archive listing", result.StandardOutput);
        Assert.Equal(@"--list ""C:\my backups\source.dump""", result.CommandLine);
        Assert.Equal("s3cret", runner.CapturedEnvironment["PGPASSWORD"]);
        Assert.Equal("UTF8", runner.CapturedEnvironment["PGCLIENTENCODING"]);
        Assert.Empty(history.Records);
    }

    [Fact]
    public async Task ProbeAsync_WhenTheToolFails_ReportsTheErrorWithoutRecordingIt()
    {
        var runner = new RecordingProcessRunner
        {
            ResultToReturn = new ProcessResult(1, string.Empty, "pg_restore: bad archive")
        };
        var history = new RecordingHistoryRepository();
        var run = new ClientToolRun(runner, history);

        var result = await run.ProbeAsync(
            @"C:\pg\bin\pg_restore.exe",
            ["--list", @"C:\backups\source.dump"],
            new ConnectionSettings(),
            "Restore_Error_NonZeroExit");

        Assert.False(result.IsSuccess);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("pg_restore: bad archive", result.ErrorMessage);
        Assert.Empty(history.Records);
    }
}
