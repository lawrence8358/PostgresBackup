namespace PostgresBackup.Core.Models;

/// <summary>
/// 還原作業選項
/// </summary>
public class RestoreOptions
{
    public ConnectionSettings Connection { get; set; } = new();

    public string SourceFilePath { get; set; } = string.Empty;

    public string TargetDatabase { get; set; } = string.Empty;

    public BackupFormat Format { get; set; } = BackupFormat.Custom;

    public RestoreMode Mode { get; set; } = RestoreMode.Normal;

    /// <summary>
    /// 還原前是否強制建立目標資料庫的安全快照（預設為 true）
    /// </summary>
    public bool CreatePreRestoreSnapshot { get; set; } = true;

    /// <summary>
    /// 快照存放目錄（若為空則預設於備份目錄下的 snapshots 資料夾）
    /// </summary>
    public string? SnapshotDirectory { get; set; }

    /// <summary>
    /// 自動根據來源檔案副檔名推斷備份格式
    /// </summary>
    public static BackupFormat DetectFormatFromFilePath(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".sql" ? BackupFormat.Plain : BackupFormat.Custom;
    }
}
