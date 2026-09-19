namespace PostgresBackup.Core.Models;

/// <summary>
/// 還原模式（物件覆寫與寫入策略）
/// </summary>
public enum RestoreMode
{
    /// <summary>
    /// 一般還原（建立遺漏物件，不刪除既有物件）
    /// </summary>
    Normal,

    /// <summary>
    /// 清除並重建目標資料庫中的物件（--clean --if-exists，不改用備份檔內的資料庫名稱）
    /// </summary>
    CleanAndRecreate,

    /// <summary>
    /// 僅寫入資料 (--data-only)
    /// </summary>
    DataOnly
}
