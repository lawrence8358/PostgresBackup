using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Interfaces;

/// <summary>
/// 連線設定檔倉儲接縫介面
/// </summary>
public interface IConnectionProfileRepository
{
    /// <summary>
    /// 取得所有已儲存的連線設定檔
    /// </summary>
    Task<IReadOnlyList<ConnectionProfile>> GetAllProfilesAsync(CancellationToken ct = default);

    /// <summary>
    /// 依識別碼取得特定連線設定檔
    /// </summary>
    Task<ConnectionProfile?> GetProfileByIdAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// 儲存或更新連線設定檔（若提供密碼則安全保存至憑證管理器）
    /// </summary>
    Task SaveProfileAsync(ConnectionProfile profile, string? password = null, CancellationToken ct = default);

    /// <summary>
    /// 刪除連線設定檔及其憑證
    /// </summary>
    Task<bool> DeleteProfileAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// 取得設定檔對應的安全存儲密碼
    /// </summary>
    Task<string?> GetPasswordAsync(string profileId, CancellationToken ct = default);
}
