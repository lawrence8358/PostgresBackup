using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Interfaces;

/// <summary>
/// 備份與還原歷史稽核倉儲接縫介面
/// </summary>
public interface IBackupHistoryRepository
{
    /// <summary>
    /// 初始化倉儲（建立資料表與索引）
    /// </summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>
    /// 新增一筆不可變之作業稽核紀錄
    /// </summary>
    Task<BackupRecord> AddRecordAsync(BackupRecord record, CancellationToken ct = default);

    /// <summary>
    /// 依篩選條件查詢歷史紀錄清單（依時間遞減排序）
    /// </summary>
    Task<IReadOnlyList<BackupRecord>> GetRecordsAsync(BackupHistoryFilter? filter = null, CancellationToken ct = default);

    /// <summary>
    /// 依識別碼取得單筆歷史紀錄
    /// </summary>
    Task<BackupRecord?> GetRecordByIdAsync(long id, CancellationToken ct = default);

    /// <summary>
    /// 刪除指定之歷史紀錄
    /// </summary>
    Task<bool> DeleteRecordAsync(long id, CancellationToken ct = default);
}
