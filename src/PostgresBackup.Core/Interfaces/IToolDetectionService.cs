using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Interfaces;

/// <summary>
/// PostgreSQL 官方客戶端工具偵測與相容性檢查服務
/// </summary>
public interface IToolDetectionService
{
    /// <summary>
    /// 執行四層偵測管線尋找本機客戶端工具（自訂路徑 -> PATH -> 常見安裝路徑 -> Registry）
    /// </summary>
    Task<ToolDetectionResult> DetectAsync(string? customPath = null, CancellationToken ct = default);

    /// <summary>
    /// 連線至資料庫伺服器並比對客戶端工具與伺服器主版本相容性
    /// </summary>
    Task<VersionCheckResult> CheckCompatibilityAsync(ToolDetectionResult clientTools, string connectionString, CancellationToken ct = default);

    /// <summary>
    /// 靜態比對客戶端工具版本與已知伺服器版本相容性（客戶端主版本 >= 伺服器主版本）
    /// </summary>
    VersionCheckResult CheckCompatibility(ToolVersion clientVersion, int serverMajorVersion, string? serverVersionString = null);
}
