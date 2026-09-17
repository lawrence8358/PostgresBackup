namespace PostgresBackup.Core.Models;

/// <summary>
/// 備份模式（萃取內容範疇）
/// </summary>
public enum BackupMode
{
    /// <summary>
    /// 結構與資料 (Schema + Data)
    /// </summary>
    SchemaAndData,

    /// <summary>
    /// 僅結構 (Schema Only, -s)
    /// </summary>
    SchemaOnly,

    /// <summary>
    /// 僅資料 (Data Only, -a)
    /// </summary>
    DataOnly
}
