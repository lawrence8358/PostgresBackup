using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Interfaces;

/// <summary>
/// 官方客戶端工具執行接縫介面，封裝 pg_dump / pg_restore / psql 外部呼叫
/// </summary>
public interface IClientToolRunner
{
    /// <summary>
    /// 執行指定之官方客戶端工具，並以串流回調即時接收日誌輸出
    /// </summary>
    Task<ProcessResult> RunToolAsync(
        string executablePath,
        string arguments,
        string? password = null,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null,
        CancellationToken ct = default);
}
