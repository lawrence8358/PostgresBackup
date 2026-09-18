using PostgresBackup.Core.Resources;

namespace PostgresBackup.Core.Models;

/// <summary>
/// PostgreSQL 連線設定檔（不包含明文密碼）
/// </summary>
public class ConnectionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = CoreStrings.Get("Profile_DefaultName");
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "postgres";
    public string Username { get; set; } = "postgres";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>
    /// 顯示名稱，便於下拉清單識別
    /// </summary>
    public string DisplayName => $"{Name} ({Username}@{Host}:{Port}/{Database})";

    /// <summary>
    /// 轉換為執行時連線設定
    /// </summary>
    public ConnectionSettings ToConnectionSettings(string? password = null)
    {
        return new ConnectionSettings
        {
            Host = Host,
            Port = Port,
            Database = Database,
            Username = Username,
            Password = password
        };
    }
}
