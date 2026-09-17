namespace PostgresBackup.Core.Models;

/// <summary>
/// 歷史紀錄查詢篩選條件
/// </summary>
public class BackupHistoryFilter
{
    public BackupOperationType? OperationType { get; set; }
    public BackupStatus? Status { get; set; }
    public string? DatabaseName { get; set; }
    public DateTimeOffset? FromDate { get; set; }
    public DateTimeOffset? ToDate { get; set; }
    public int Limit { get; set; } = 100;
}
