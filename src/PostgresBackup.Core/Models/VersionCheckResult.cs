using PostgresBackup.Core.Resources;

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
            Message = CoreStrings.Format("Version_Compatible", clientVersion, serverVersionString ?? serverMajor.ToString())
        };

    public static VersionCheckResult Incompatible(ToolVersion clientVersion, int serverMajor, string? serverVersionString = null) =>
        new()
        {
            IsCompatible = false,
            ClientVersion = clientVersion,
            ServerMajorVersion = serverMajor,
            ServerVersionString = serverVersionString,
            Message = CoreStrings.Format("Version_Incompatible", clientVersion.Major, serverMajor)
        };

    public static VersionCheckResult Failed(string reason) =>
        new()
        {
            IsCompatible = false,
            Message = reason
        };
}
