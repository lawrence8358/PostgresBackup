namespace PostgresBackup.Core.Models;

/// <summary>
/// 備份檔案格式
/// </summary>
public enum BackupFormat
{
    /// <summary>
    /// 自訂二進位格式 (Custom, -Fc, 官方最推薦格式，支援彈性挑選物件還原)
    /// </summary>
    Custom,

    /// <summary>
    /// 純文字 SQL 腳本 (Plain SQL, -Fp)
    /// </summary>
    Plain
}
