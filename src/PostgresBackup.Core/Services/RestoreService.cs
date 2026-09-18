using System.Diagnostics;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 實作安全還原作業服務，具備還原前強制安全快照防護與阻斷機制
/// </summary>
public class RestoreService : IRestoreService
{
    private readonly IProcessRunner _processRunner;
    private readonly IToolDetectionService _toolDetector;
    private readonly IBackupService? _backupService;
    private readonly IBackupHistoryRepository? _historyRepo;

    public RestoreService(
        IProcessRunner processRunner,
        IToolDetectionService toolDetector,
        IBackupService? backupService = null,
        IBackupHistoryRepository? historyRepo = null)
    {
        _processRunner = processRunner;
        _toolDetector = toolDetector;
        _backupService = backupService;
        _historyRepo = historyRepo;
    }

    public async Task<RestoreResult> RestoreAsync(
        RestoreOptions options,
        Action<string>? onLogLine = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var stopwatch = Stopwatch.StartNew();

        // 1. 驗證來源檔案是否存在
        if (!File.Exists(options.SourceFilePath))
        {
            var errMsg = $"來源備份檔案不存在: '{options.SourceFilePath}'";
            onLogLine?.Invoke($"[ERROR] {errMsg}");
            return RestoreResult.Failure(errMsg, -1, TimeSpan.Zero, string.Empty);
        }

        // 自動推斷格式
        if (options.Format == BackupFormat.Custom && options.SourceFilePath.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            options.Format = BackupFormat.Plain;
        }

        // 2. 尋找與驗證工具路徑
        var detection = await _toolDetector.DetectAsync(options.ClientToolDirectory, ct);
        if (!detection.IsReady)
        {
            var errMsg = "未偵測到 PostgreSQL 官方客戶端工具！請先於設定頁面確認安裝。";
            onLogLine?.Invoke($"[ERROR] {errMsg}");
            return RestoreResult.Failure(errMsg, -1, TimeSpan.Zero, string.Empty);
        }

        var toolExecutablePath = options.Format == BackupFormat.Custom
            ? detection.PgRestorePath
            : detection.PsqlPath;

        if (string.IsNullOrWhiteSpace(toolExecutablePath))
        {
            var toolName = options.Format == BackupFormat.Custom ? "pg_restore" : "psql";
            var errMsg = $"未找到執行所需之官方工具: {toolName}";
            onLogLine?.Invoke($"[ERROR] {errMsg}");
            return RestoreResult.Failure(errMsg, -1, TimeSpan.Zero, string.Empty);
        }

        var targetDb = !string.IsNullOrWhiteSpace(options.TargetDatabase)
            ? options.TargetDatabase
            : options.Connection.Database;

        string? snapshotFilePath = null;

        // 3. 還原前安全快照 (Pre-Restore Snapshot) — 透過 BackupService 作業模組安全委任
        if (options.CreatePreRestoreSnapshot)
        {
            onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SAFETY] 正在對目標資料庫 '{targetDb}' 執行還原前安全快照 (Pre-Restore Snapshot)...");

            var snapshotDir = options.SnapshotDirectory;
            if (string.IsNullOrWhiteSpace(snapshotDir))
            {
                var baseDir = Path.GetDirectoryName(options.SourceFilePath);
                if (string.IsNullOrEmpty(baseDir))
                {
                    baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PostgresBackups");
                }
                snapshotDir = Path.Combine(baseDir, "snapshots");
            }

            if (!Directory.Exists(snapshotDir))
            {
                Directory.CreateDirectory(snapshotDir);
            }

            var snapshotFileName = $"{targetDb}_snapshot_{DateTime.Now:yyyyMMddHHmmss}.dump";

            var snapshotOptions = new BackupOptions
            {
                Connection = options.Connection,
                Format = BackupFormat.Custom,
                Mode = BackupMode.SchemaAndData,
                Scope = BackupScope.FullDatabase,
                OutputDirectory = snapshotDir,
                CustomFileName = snapshotFileName,
                ClientToolDirectory = options.ClientToolDirectory,
                OperationType = BackupOperationType.PreRestoreSnapshot
            };

            if (_backupService == null)
            {
                var errMsg = "安全快照失敗：未注入備份作業服務 (IBackupService)，為保護既有資料庫，已強制終止還原作業。";
                onLogLine?.Invoke($"[CRITICAL ABORT] {errMsg}");
                return RestoreResult.Failure(errMsg, -1, stopwatch.Elapsed, string.Empty);
            }

            var snapshotResult = await _backupService.BackupAsync(
                snapshotOptions,
                onLogLine: line => onLogLine?.Invoke($"[snapshot] {line}"),
                ct: ct);

            if (!snapshotResult.IsSuccess)
            {
                var errMsg = $"安全快照建立失敗 (ExitCode: {snapshotResult.ExitCode}): {snapshotResult.ErrorMessage ?? "無法產生快照檔案"}。為保護既有資料庫免受破壞，系統已強制終止還原作業！";
                onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [CRITICAL ABORT] {errMsg}");
                return RestoreResult.Failure(errMsg, snapshotResult.ExitCode, stopwatch.Elapsed, snapshotResult.Arguments);
            }

            snapshotFilePath = snapshotResult.OutputFilePath;
            onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SAFETY SUCCESS] 安全快照已建立完畢（大小: {snapshotResult.FileSizeBytes} 位元組）");
        }

        // 4. 執行還原作業
        var restoreArgs = RestoreArgumentsBuilder.Build(options);
        var toolNameDisplay = Path.GetFileNameWithoutExtension(toolExecutablePath);

        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] 啟動還原作業: 工具 '{toolNameDisplay}', 目標資料庫 '{targetDb}'");
        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] 來源檔案: {options.SourceFilePath}");
        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] 還原模式: {options.Mode}");

        var envVars = new Dictionary<string, string?>();
        if (!string.IsNullOrEmpty(options.Connection.Password))
        {
            envVars["PGPASSWORD"] = options.Connection.Password;
        }

        var restoreProc = await _processRunner.RunAsync(
            toolExecutablePath,
            restoreArgs,
            envVars,
            onOutputLine: line => onLogLine?.Invoke($"[{toolNameDisplay}] {line}"),
            onErrorLine: line => onLogLine?.Invoke($"[{toolNameDisplay}] {line}"),
            ct: ct);

        stopwatch.Stop();

        if (restoreProc.ExitCode == 0)
        {
            onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SUCCESS] 還原作業成功完成！總耗時: {stopwatch.Elapsed.TotalSeconds:F2} 秒");

            if (_historyRepo != null)
            {
                try
                {
                    await _historyRepo.AddRecordAsync(new BackupRecord
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        OperationType = BackupOperationType.Restore,
                        DatabaseName = targetDb,
                        TargetDatabase = targetDb,
                        FilePath = options.SourceFilePath,
                        Format = options.Format,
                        FileSizeBytes = new FileInfo(options.SourceFilePath).Length,
                        DurationMs = (long)stopwatch.Elapsed.TotalMilliseconds,
                        Status = BackupStatus.Success,
                        Arguments = restoreArgs
                    }, ct);
                }
                catch { }
            }

            return RestoreResult.Success(stopwatch.Elapsed, restoreArgs, snapshotFilePath);
        }
        else
        {
            var err = !string.IsNullOrWhiteSpace(restoreProc.StandardError)
                ? restoreProc.StandardError
                : restoreProc.ErrorMessage ?? "還原程序失敗，工具回傳非零退出碼。";

            onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [ERROR] 還原作業失敗 (ExitCode: {restoreProc.ExitCode}): {err}");

            if (_historyRepo != null)
            {
                try
                {
                    await _historyRepo.AddRecordAsync(new BackupRecord
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        OperationType = BackupOperationType.Restore,
                        DatabaseName = targetDb,
                        TargetDatabase = targetDb,
                        FilePath = options.SourceFilePath,
                        Format = options.Format,
                        FileSizeBytes = new FileInfo(options.SourceFilePath).Length,
                        DurationMs = (long)stopwatch.Elapsed.TotalMilliseconds,
                        Status = BackupStatus.Failed,
                        ErrorMessage = err,
                        Arguments = restoreArgs
                    }, ct);
                }
                catch { }
            }

            return RestoreResult.Failure(err, restoreProc.ExitCode, stopwatch.Elapsed, restoreArgs, snapshotFilePath);
        }
    }
}
