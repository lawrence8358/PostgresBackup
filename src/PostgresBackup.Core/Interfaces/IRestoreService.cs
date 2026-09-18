using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Interfaces;

/// <summary>
/// 安全還原作業服務介面
/// </summary>
public interface IRestoreService
{
    /// <summary>
    /// 執行安全還原作業（預設強制觸發還原前安全快照）
    /// </summary>
    Task<RestoreResult> RestoreAsync(
        RestoreOptions options,
        Action<string>? onLogLine = null,
        CancellationToken ct = default);
}
