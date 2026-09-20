using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

using PostgresBackup.Core.Resources;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 實作 PostgreSQL 備份作業服務。
///
/// 工具執行、環境變數、計時、成敗判定與紀錄寫入全數委由客戶端工具作業，
/// 此處只保留備份專屬的責任：客戶端工具偵測、輸出目錄、產出檔案存在判定與結果轉換。
/// </summary>
public class BackupService : IBackupService
{
    private readonly ClientToolRun _clientToolRun;
    private readonly IToolDetectionService _toolDetector;

    public BackupService(
        ClientToolRun clientToolRun,
        IToolDetectionService toolDetector)
    {
        _clientToolRun = clientToolRun;
        _toolDetector = toolDetector;
    }

    public async Task<BackupResult> BackupAsync(
        BackupOptions options,
        Action<string>? onLogLine = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

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

        // 3. 計算輸出檔案路徑與作業專屬參數
        var targetFilePath = options.GetTargetFilePath();
        var arguments = BackupArgumentsBuilder.Build(options, targetFilePath);

        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Format("Backup_Log_Start", options.Connection.Database)}");
        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Format("Backup_Log_Options", options.Format, options.Mode, options.Scope)}");
        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Format("Backup_Log_OutputPath", targetFilePath)}");
        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Format("Backup_Log_Tool", pgDumpPath)}");

        // 4. 執行一次客戶端工具作業
        var run = await _clientToolRun.RunAsync(
            new ClientToolRunRequest
            {
                ExecutablePath = pgDumpPath,
                Arguments = arguments,
                Connection = options.Connection,
                LogPrefix = "pg_dump",
                NonZeroExitErrorKey = "Backup_Error_NonZeroExit",
                OperationType = options.OperationType,
                RecordedFilePath = targetFilePath,
                RecordedFormat = options.Format,
                // 離開碼為零仍不足以稱為成功：備份檔沒有落地就不是一份備份。
                ConfirmSuccess = () => File.Exists(targetFilePath),
                MeasureRecordedFileSize = succeeded =>
                    succeeded ? new FileInfo(targetFilePath).Length : 0
            },
            onLogLine,
            ct);

        // 5. 轉換為備份作業結果
        if (run.IsSuccess)
        {
            onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SUCCESS] {CoreStrings.Get("Backup_Log_Success")}");
            onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Format("Backup_Log_FileInfo", FormatBytes(run.RecordedFileSizeBytes), CoreStrings.Format("Format_DurationSeconds", run.Elapsed.TotalSeconds.ToString("F2")))}");

            return BackupResult.Success(
                targetFilePath,
                run.RecordedFileSizeBytes,
                run.Elapsed,
                run.CommandLine,
                options.Format);
        }

        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [ERROR] {CoreStrings.Format("Backup_Log_Failed", run.ExitCode)}");
        if (!string.IsNullOrWhiteSpace(run.ErrorMessage))
        {
            onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Format("Backup_Log_ErrorDetail", run.ErrorMessage)}");
        }

        return BackupResult.Failure(
            run.ErrorMessage,
            run.ExitCode,
            run.Elapsed,
            run.CommandLine,
            targetFilePath,
            options.Format);
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
