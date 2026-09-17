namespace PostgresBackup.Core.Models;

/// <summary>
/// 備份範圍（資料庫物件邊界）
/// </summary>
public enum BackupScope
{
    /// <summary>
    /// 完整資料庫
    /// </summary>
    FullDatabase,

    /// <summary>
    /// 指定綱要 (Schema)
    /// </summary>
    SpecificSchemas,

    /// <summary>
    /// 指定資料表 (Table)
    /// </summary>
    SpecificTables
}
