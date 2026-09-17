namespace PostgresBackup.Core.Models;

/// <summary>
/// 備份作業選項
/// </summary>
public class BackupOptions
{
    public ConnectionSettings Connection { get; set; } = new();

    public BackupFormat Format { get; set; } = BackupFormat.Custom;

    public BackupMode Mode { get; set; } = BackupMode.SchemaAndData;

    public BackupScope Scope { get; set; } = BackupScope.FullDatabase;

    public List<string> Schemas { get; set; } = [];

    public List<string> Tables { get; set; } = [];

    public string OutputDirectory { get; set; } = string.Empty;

    public string? CustomFileName { get; set; }

    /// <summary>
    /// 依命名規範產生檔案名稱：{database}_{yyyyMMddHHmmss}.{dump|sql}
    /// </summary>
    public string GenerateDefaultFileName(DateTimeOffset? timestamp = null)
    {
        var time = timestamp ?? DateTimeOffset.Now;
        var ext = Format == BackupFormat.Custom ? "dump" : "sql";
        var db = string.IsNullOrWhiteSpace(Connection.Database) ? "postgres" : Connection.Database;
        return $"{db}_{time:yyyyMMddHHmmss}.{ext}";
    }

    /// <summary>
    /// 取得輸出檔案的完整實體路徑
    /// </summary>
    public string GetTargetFilePath(DateTimeOffset? timestamp = null)
    {
        var fileName = !string.IsNullOrWhiteSpace(CustomFileName)
            ? CustomFileName
            : GenerateDefaultFileName(timestamp);

        return Path.Combine(OutputDirectory, fileName);
    }
}
