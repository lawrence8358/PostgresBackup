namespace PostgresBackup.Core.Models;

/// <summary>
/// 一次客戶端工具作業的下單內容。呼叫端交出的是資料 —— 未跳脫的 argv 元素清單與
/// 連線設定 —— 引號與命令列拼接一律由客戶端工具作業負責。
/// </summary>
public sealed record ClientToolRunRequest
{
    /// <summary>客戶端工具執行檔的完整路徑。</summary>
    public required string ExecutablePath { get; init; }

    /// <summary>
    /// 作業專屬的 argv 元素清單，每個元素對應一個 argv 元素且**未經跳脫**。
    /// 連線參數（<c>-h</c>、<c>-p</c>、<c>-U</c>、<c>-d</c>）不必列入，由模組補在前方。
    /// </summary>
    public required IReadOnlyList<string> Arguments { get; init; }

    /// <summary>連線設定。提供連線參數的值與 <c>PGPASSWORD</c>。</summary>
    public required ConnectionSettings Connection { get; init; }

    /// <summary>
    /// 目標資料庫。非空白時優先於 <see cref="ConnectionSettings.Database"/>，
    /// 同時成為備份紀錄的目標資料庫欄位。
    /// </summary>
    public string? TargetDatabase { get; init; }

    /// <summary>串流輸出時冠在每一行前方的工具名稱，例如 <c>pg_dump</c>。</summary>
    public required string LogPrefix { get; init; }

    /// <summary>離開碼非零且標準錯誤為空時，用以產生錯誤訊息的資源鍵。</summary>
    public required string NonZeroExitErrorKey { get; init; }

    /// <summary>備份紀錄的作業類型。</summary>
    public BackupOperationType OperationType { get; init; } = BackupOperationType.Backup;

    /// <summary>
    /// 備份紀錄的檔案路徑。備份為產出檔，還原為來源檔。
    /// </summary>
    public required string RecordedFilePath { get; init; }

    /// <summary>備份紀錄的備份格式。</summary>
    public BackupFormat RecordedFormat { get; init; } = BackupFormat.Custom;

    /// <summary>
    /// 依作業成敗取得備份紀錄的檔案大小。備份取產出檔（且僅於成功時），還原取來源檔。
    /// 未提供時記為 0。呼叫端須確保委派被呼叫時該檔案確實存在。
    /// </summary>
    public Func<bool, long>? MeasureRecordedFileSize { get; init; }

    /// <summary>
    /// 離開碼為零時，再由呼叫端確認作業確實成功 —— 備份以此檢查產出檔案是否存在。
    /// 未提供時視為恆真。
    /// </summary>
    public Func<bool>? ConfirmSuccess { get; init; }
}
