using System.Diagnostics;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

using PostgresBackup.Core.Resources;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 實作 PostgreSQL 備份作業服務
/// </summary>
public class BackupService : IBackupService
{
    private readonly IProcessRunner _processRunner;
    private readonly IToolDetectionService _toolDetector;
    private readonly IBackupHistoryRepository? _historyRepo;

    public BackupService(
        IProcessRunner processRunner,
        IToolDetectionService toolDetector,
        IBackupHistoryRepository? historyRepo = null)
    {
        _processRunner = processRunner;
        _toolDetector = toolDetector;
        _historyRepo = historyRepo;
    }

    public async Task<BackupResult> BackupAsync(
        BackupOptions options,
        Action<string>? onLogLine = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // 0. 拒絕以完整連線字串描述的連線。pg_dump 的參數只由主機／連接埠／使用者／
        // 資料庫欄位組成，不認得連線字串；靜默忽略會讓使用者以為備份的是連線字串
        // 指向的那台伺服器。
        if (!string.IsNullOrWhiteSpace(options.Connection.ConnectionString))
        {
            var errMsg = CoreStrings.Get("Backup_Error_ConnectionStringNotSupported");
            onLogLine?.Invoke($"[ERROR] {errMsg}");
            return BackupResult.Failure(errMsg, -1, TimeSpan.Zero, string.Empty);
        }

        var stopwatch = Stopwatch.StartNew();

        // 1. 驗證與解析客戶端工具路徑
        var detection = await _toolDetector.DetectAsync(options.ClientToolDirectory, ct);
        if (!detection.IsReady || string.IsNullOrWhiteSpace(detection.PgDumpPath))
        {
            var errMsg = CoreStrings.Get("Backup_Error_PgDumpNotFound");
            onLogLine?.Invoke($"[ERROR] {errMsg}");
            return BackupResult.Failure(errMsg, -1, TimeSpan.Zero, string.Empty);
        }
        var pgDumpPath = detection.PgDumpPath;

        // 2. 確保輸出目錄存在
        if (string.IsNullOrWhiteSpace(options.OutputDirectory))
        {
            var defaultDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "PostgresBackups");
            options.OutputDirectory = defaultDir;
        }

        if (!Directory.Exists(options.OutputDirectory))
        {
            Directory.CreateDirectory(options.OutputDirectory);
        }

        // 3. 計算輸出檔案路徑與命令參數
        var targetFilePath = options.GetTargetFilePath();
        var arguments = BackupArgumentsBuilder.Build(options, targetFilePath);

        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Format("Backup_Log_Start", options.Connection.Database)}");
        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Format("Backup_Log_Options", options.Format, options.Mode, options.Scope)}");
        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Format("Backup_Log_OutputPath", targetFilePath)}");
        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Format("Backup_Log_Tool", pgDumpPath)}");

        // 4. 執行 pg_dump 並串流捕獲標準輸出與錯誤輸出
        var envVars = new Dictionary<string, string?>();
        if (!string.IsNullOrEmpty(options.Connection.Password))
        {
            envVars["PGPASSWORD"] = options.Connection.Password;
        }
        // Keep dump data encoding explicit; diagnostic messages are forced to the C locale below.
        envVars["PGCLIENTENCODING"] = "UTF8";
        // Keep localized Windows messages from being emitted in an unknown code page.
        envVars["LC_ALL"] = "C";
        envVars["LC_MESSAGES"] = "C";
        envVars["LANG"] = "C";
        envVars["LANGUAGE"] = null;

        var processResult = await _processRunner.RunAsync(
            pgDumpPath,
            arguments,
            envVars,
            onOutputLine: line => onLogLine?.Invoke($"[pg_dump] {line}"),
            onErrorLine: line => onLogLine?.Invoke($"[pg_dump] {line}"),
            ct: ct);

        stopwatch.Stop();

        // 5. 評估執行結果並寫入歷史稽核紀錄
        if (processResult.ExitCode == 0 && File.Exists(targetFilePath))
        {
            var fileInfo = new FileInfo(targetFilePath);
            var size = fileInfo.Length;

            onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SUCCESS] {CoreStrings.Get("Backup_Log_Success")}");
            onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Format("Backup_Log_FileInfo", FormatBytes(size), CoreStrings.Format("Format_DurationSeconds", stopwatch.Elapsed.TotalSeconds.ToString("F2")))}");

            if (_historyRepo != null)
            {
                try
                {
                    await _historyRepo.AddRecordAsync(new BackupRecord
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        OperationType = options.OperationType,
                        DatabaseName = options.Connection.Database,
                        FilePath = targetFilePath,
                        Format = options.Format,
                        FileSizeBytes = size,
                        DurationMs = (long)stopwatch.Elapsed.TotalMilliseconds,
                        Status = BackupStatus.Success,
                        Arguments = arguments
                    }, ct);
                }
                catch (Exception ex)
                {
                    onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [WARNING] {CoreStrings.Format("Backup_Log_HistoryWriteFailed", ex.Message)}");
                }
            }

            return BackupResult.Success(
                targetFilePath,
                size,
                stopwatch.Elapsed,
                arguments,
                options.Format);
        }
        else
        {
            var err = !string.IsNullOrWhiteSpace(processResult.StandardError)
                ? processResult.StandardError
                : processResult.ErrorMessage ?? CoreStrings.Get("Backup_Error_NonZeroExit");

            onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [ERROR] {CoreStrings.Format("Backup_Log_Failed", processResult.ExitCode)}");
            if (!string.IsNullOrWhiteSpace(err))
            {
                onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Format("Backup_Log_ErrorDetail", err)}");
            }

            if (_historyRepo != null)
            {
                try
                {
                    await _historyRepo.AddRecordAsync(new BackupRecord
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        OperationType = options.OperationType,
                        DatabaseName = options.Connection.Database,
                        FilePath = targetFilePath,
                        Format = options.Format,
                        FileSizeBytes = 0,
                        DurationMs = (long)stopwatch.Elapsed.TotalMilliseconds,
                        Status = BackupStatus.Failed,
                        ErrorMessage = err,
                        Arguments = arguments
                    }, ct);
                }
                catch { }
            }

            return BackupResult.Failure(
                err,
                processResult.ExitCode,
                stopwatch.Elapsed,
                arguments,
                targetFilePath,
                options.Format);
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024)
            return $"{(double)bytes / (1024 * 1024 * 1024):F2} GB";
        if (bytes >= 1024 * 1024)
            return $"{(double)bytes / (1024 * 1024):F2} MB";
        if (bytes >= 1024)
            return $"{(double)bytes / 1024:F2} KB";
        return $"{bytes} B";
    }
}
