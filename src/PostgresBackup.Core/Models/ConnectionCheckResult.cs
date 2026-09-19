namespace PostgresBackup.Core.Models;

/// <summary>
/// 對 PostgreSQL 伺服器實際建立連線的結果。
/// 成功代表伺服器可達且該組帳號密碼確實通過驗證；失敗訊息來自資料庫驅動程式，
/// 刻意不含連線字串內容，以免回吐密碼。
/// </summary>
public record ConnectionCheckResult
{
    public bool IsSuccess { get; init; }

    /// <summary>伺服器回報的版本字串（僅連線成功時有值）。</summary>
    public string? ServerVersionString { get; init; }

    /// <summary>伺服器主版本號（僅連線成功時有值）。</summary>
    public int? ServerMajorVersion { get; init; }

    /// <summary>失敗原因；成功時為空字串。</summary>
    public string Message { get; init; } = string.Empty;

    public static ConnectionCheckResult Success(string? serverVersionString, int serverMajorVersion) =>
        new()
        {
            IsSuccess = true,
            ServerVersionString = serverVersionString,
            ServerMajorVersion = serverMajorVersion
        };

    public static ConnectionCheckResult Failure(string reason) =>
        new()
        {
            IsSuccess = false,
            Message = reason
        };
}
