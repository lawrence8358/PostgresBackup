using Npgsql;

namespace PostgresBackup.Core.Models;

/// <summary>
/// PostgreSQL 連線設定模型
/// </summary>
public class ConnectionSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "postgres";
    public string Username { get; set; } = "postgres";
    public string? Password { get; set; }
    public string? ConnectionString { get; set; }

    /// <summary>
    /// 產生 Npgsql 連線字串。若有指定自訂連線字串則優先使用。
    /// </summary>
    public string ToConnectionString()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString))
        {
            return ConnectionString;
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = Database,
            Username = Username,
            Timeout = 10,
            CommandTimeout = 30
        };

        if (!string.IsNullOrEmpty(Password))
        {
            builder.Password = Password;
        }

        return builder.ConnectionString;
    }
}
