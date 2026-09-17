namespace PostgresBackup.Core.Models;

/// <summary>
/// 備份或還原之操作類型
/// </summary>
public enum BackupOperationType
{
    /// <summary>
    /// 備份作業 (Backup Job)
    /// </summary>
    Backup,

    /// <summary>
    /// 還原作業 (Restore Job)
    /// </summary>
    Restore,

    /// <summary>
    /// 還原前安全快照 (Pre-Restore Snapshot)
    /// </summary>
    PreRestoreSnapshot
}
