namespace PostgresBackup.Core.Models;

/// <summary>
/// 客戶端工具之偵測狀態
/// </summary>
public enum ToolStatus
{
    /// <summary>
    /// 工具已就緒且版本相容
    /// </summary>
    Ready,

    /// <summary>
    /// 工具已偵測到，但版本低於伺服器版本（不相容）
    /// </summary>
    Incompatible,

    /// <summary>
    /// 未偵測到必要的客戶端工具
    /// </summary>
    NotFound
}
