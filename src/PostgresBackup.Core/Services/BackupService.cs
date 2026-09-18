using System.Diagnostics;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

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

        var stopwatch = Stopwatch.StartNew();

        // 1. 驗證與解析客戶端工具路徑
        var detection = await _toolDetector.DetectAsync(options.ClientToolDirectory, ct);
        if (!detection.IsReady || string.IsNullOrWhiteSpace(detection.PgDumpPath))
        {
            var errMsg = "未偵測到 PostgreSQL 客戶端工具 (pg_dump)！請先於設定頁面確認安裝或指定工具目錄。";
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

        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] 啟動備份作業: 資料庫 '{options.Connection.Database}'");
        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] 格式: {options.Format}, 模式: {options.Mode}, 範圍: {options.Scope}");
        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] 輸出路徑: {targetFilePath}");
        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] 執行工具: {pgDumpPath}");

        // 4. 執行 pg_dump 並串流捕獲標準輸出與錯誤輸出
        var envVars = new Dictionary<string, string?>();
        if (!string.IsNullOrEmpty(options.Connection.Password))
        {
            envVars["PGPASSWORD"] = options.Connection.Password;
        }

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

            onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SUCCESS] 備份作業順利完成！");
            onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] 產出檔案大小: {FormatBytes(size)}, 耗時: {stopwatch.Elapsed.TotalSeconds:F2} 秒");

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
                    onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [WARNING] 寫入歷史倉儲失敗: {ex.Message}");
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
                : processResult.ErrorMessage ?? "備份作業失敗，pg_dump 回傳非零退出碼。";

            onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [ERROR] 備份作業失敗 (ExitCode: {processResult.ExitCode})");
            if (!string.IsNullOrWhiteSpace(err))
            {
                onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] 錯誤訊息: {err}");
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
