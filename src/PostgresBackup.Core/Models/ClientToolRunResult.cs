namespace PostgresBackup.Core.Models;

/// <summary>
/// 一次客戶端工具作業的執行結果。
///
/// 此型別只在 Core 內部流通：<see cref="BackupResult"/> 與 <see cref="RestoreResult"/>
/// 各自從它轉換而來，兩者的形狀確實不同，不合併。
/// </summary>
public sealed record ClientToolRunResult
{
    /// <summary>離開碼為零，且呼叫端的額外確認（若有）也通過。</summary>
    public bool IsSuccess { get; init; }

    public int ExitCode { get; init; }

    public TimeSpan Elapsed { get; init; }

    /// <summary>拼接完成的命令列字串，亦即寫入備份紀錄的那一份。</summary>
    public string CommandLine { get; init; } = string.Empty;

    public string StandardOutput { get; init; } = string.Empty;

    /// <summary>失敗時的錯誤訊息；成功時為 null。</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>寫入備份紀錄的檔案大小，供呼叫端一併用於結果與記錄輸出。</summary>
    public long RecordedFileSizeBytes { get; init; }
}
