namespace PostgresBackup.Core.Models;

/// <summary>
/// 客戶端工具之偵測來源
/// </summary>
public enum DetectionSource
{
    None,
    CustomPath,
    Path,
    CommonDirectory,
    Registry
}
