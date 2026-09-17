namespace PostgresBackup.Core.Models;

/// <summary>
/// 客戶端工具與 PostgreSQL 伺服器版本相容性檢查結果
/// </summary>
public record VersionCheckResult
{
    public bool IsCompatible { get; init; }
    public ToolVersion? ClientVersion { get; init; }
    public int? ServerMajorVersion { get; init; }
    public string? ServerVersionString { get; init; }
    public string Message { get; init; } = string.Empty;

    public static VersionCheckResult Compatible(ToolVersion clientVersion, int serverMajor, string? serverVersionString = null) =>
        new()
        {
            IsCompatible = true,
            ClientVersion = clientVersion,
            ServerMajorVersion = serverMajor,
            ServerVersionString = serverVersionString,
            Message = $"客戶端工具版本相容（客戶端: {clientVersion}, 伺服器: {serverVersionString ?? serverMajor.ToString()}）。"
        };

    public static VersionCheckResult Incompatible(ToolVersion clientVersion, int serverMajor, string? serverVersionString = null) =>
        new()
        {
            IsCompatible = false,
            ClientVersion = clientVersion,
            ServerMajorVersion = serverMajor,
            ServerVersionString = serverVersionString,
            Message = $"版本不相容警告：客戶端工具主版本 ({clientVersion.Major}) 低於資料庫伺服器主版本 ({serverMajor})。PostgreSQL 官方要求 pg_dump 版本必須大於或等於伺服器版本以確保備份完整性。"
        };

    public static VersionCheckResult Failed(string reason) =>
        new()
        {
            IsCompatible = false,
            Message = reason
        };
}
