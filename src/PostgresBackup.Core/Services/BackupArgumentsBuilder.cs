using System.Text;
using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 官方 pg_dump 參數構建器
/// </summary>
public static class BackupArgumentsBuilder
{
    public static string Build(BackupOptions options, string outputFilePath)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(outputFilePath))
            throw new ArgumentException("Output file path cannot be empty.", nameof(outputFilePath));

        var sb = new StringBuilder();

        // 伺服器端點與連線資訊
        var conn = options.Connection;
        var host = string.IsNullOrWhiteSpace(conn.Host) ? "localhost" : conn.Host;
        var port = conn.Port > 0 ? conn.Port : 5432;
        var username = string.IsNullOrWhiteSpace(conn.Username) ? "postgres" : conn.Username;
        var database = string.IsNullOrWhiteSpace(conn.Database) ? "postgres" : conn.Database;

        sb.Append($"-h \"{Escape(host)}\" -p {port} -U \"{Escape(username)}\" -d \"{Escape(database)}\"");

        // 格式指定 (-Fc: 自訂格式, -Fp: 純文字腳本)
        sb.Append(options.Format == BackupFormat.Custom ? " -Fc" : " -Fp");

        // 備份模式
        switch (options.Mode)
        {
            case BackupMode.SchemaOnly:
                sb.Append(" --schema-only");
                break;
            case BackupMode.DataOnly:
                sb.Append(" --data-only");
                break;
        }

        // 備份範圍
        if (options.Scope == BackupScope.SpecificSchemas && options.Schemas.Count > 0)
        {
            foreach (var schema in options.Schemas.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                sb.Append($" -n \"{Escape(schema.Trim())}\"");
            }
        }
        else if (options.Scope == BackupScope.SpecificTables && options.Tables.Count > 0)
        {
            foreach (var table in options.Tables.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                sb.Append($" -t \"{Escape(table.Trim())}\"");
            }
        }

        // 詳細輸出旗標 (使 pg_dump 能輸出即時進度日誌)
        sb.Append(" -v");

        // 輸出檔案路徑
        sb.Append($" -f \"{Escape(outputFilePath)}\"");

        return sb.ToString();
    }

    private static string Escape(string val) => val.Replace("\"", "\\\"");
}
