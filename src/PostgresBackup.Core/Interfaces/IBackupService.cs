using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Interfaces;

/// <summary>
/// 備份作業服務介面
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// 執行 PostgreSQL 備份作業並串流輸出即時日誌
    /// 取消時丟出 OperationCanceledException；已產出的部分檔案保留，訊息包含其路徑。
    /// </summary>
    Task<BackupResult> BackupAsync(
        BackupOptions options,
        Action<string>? onLogLine = null,
        CancellationToken ct = default);
}
