using System.Text;
using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 官方 pg_restore 與 psql 還原參數構建器
/// </summary>
public static class RestoreArgumentsBuilder
{
    public static string Build(RestoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.SourceFilePath))
            throw new ArgumentException("Source file path cannot be empty.", nameof(options));

        var sb = new StringBuilder();

        var conn = options.Connection;
        var host = string.IsNullOrWhiteSpace(conn.Host) ? "localhost" : conn.Host;
        var port = conn.Port > 0 ? conn.Port : 5432;
        var username = string.IsNullOrWhiteSpace(conn.Username) ? "postgres" : conn.Username;
        var targetDb = !string.IsNullOrWhiteSpace(options.TargetDatabase)
            ? options.TargetDatabase
            : (string.IsNullOrWhiteSpace(conn.Database) ? "postgres" : conn.Database);

        sb.Append($"-h \"{Escape(host)}\" -p {port} -U \"{Escape(username)}\" -d \"{Escape(targetDb)}\"");

        if (options.Format == BackupFormat.Custom)
        {
            // pg_restore 模式旗標
            switch (options.Mode)
            {
                case RestoreMode.CleanAndRecreate:
                    sb.Append(" --clean --create");
                    break;
                case RestoreMode.DataOnly:
                    sb.Append(" --data-only");
                    break;
            }

            // 詳細進度輸出
            sb.Append(" -v");

            // 來源檔案路徑
            sb.Append($" \"{Escape(options.SourceFilePath)}\"");
        }
        else
        {
            // psql 腳本執行模式
            sb.Append($" -f \"{Escape(options.SourceFilePath)}\"");
        }

        return sb.ToString();
    }

    private static string Escape(string val) => val.Replace("\"", "\\\"");
}
