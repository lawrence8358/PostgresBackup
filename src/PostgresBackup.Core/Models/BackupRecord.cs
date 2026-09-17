namespace PostgresBackup.Core.Models;

/// <summary>
/// 備份或還原之不可變歷史稽核紀錄
/// </summary>
public class BackupRecord
{
    public long Id { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public BackupOperationType OperationType { get; set; } = BackupOperationType.Backup;
    public string DatabaseName { get; set; } = string.Empty;
    public string? TargetDatabase { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public BackupFormat Format { get; set; } = BackupFormat.Custom;
    public long FileSizeBytes { get; set; }
    public long DurationMs { get; set; }
    public BackupStatus Status { get; set; } = BackupStatus.Success;
    public string? ErrorMessage { get; set; }
    public string Arguments { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string FormattedSize
    {
        get
        {
            if (FileSizeBytes >= 1024 * 1024 * 1024)
                return $"{(double)FileSizeBytes / (1024 * 1024 * 1024):F2} GB";
            if (FileSizeBytes >= 1024 * 1024)
                return $"{(double)FileSizeBytes / (1024 * 1024):F2} MB";
            if (FileSizeBytes >= 1024)
                return $"{(double)FileSizeBytes / 1024:F2} KB";
            return $"{FileSizeBytes} B";
        }
    }

    public string FormattedDuration => $"{(double)DurationMs / 1000:F2} 秒";

    public string OperationTypeDisplay => OperationType switch
    {
        BackupOperationType.Backup => "備份作業",
        BackupOperationType.Restore => "還原作業",
        BackupOperationType.PreRestoreSnapshot => "還原前安全快照",
        _ => OperationType.ToString()
    };
}
