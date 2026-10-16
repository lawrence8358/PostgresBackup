namespace PostgresBackup.Core.Models;

/// <summary>
/// 作業結果狀態
/// </summary>
public enum BackupStatus
{
    /// <summary>
    /// 成功
    /// </summary>
    Success,

    /// <summary>
    /// 失敗
    /// </summary>
    Failed,

    /// <summary>使用者取消，檔案可能不完整。</summary>
    Cancelled
}
