using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Interfaces;

/// <summary>
/// 安全還原作業服務介面
/// </summary>
public interface IRestoreService
{
    /// <summary>
    /// 執行安全還原作業（預設強制觸發還原前安全快照）
    /// 取消時丟出 OperationCanceledException，訊息包含目標資料庫與可用安全快照路徑。
    /// 取消不會復原已完成的資料庫修改。
    /// </summary>
    Task<RestoreResult> RestoreAsync(
        RestoreOptions options,
        Action<string>? onLogLine = null,
        CancellationToken ct = default);
}
