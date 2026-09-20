using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 官方 pg_dump 參數構建器。
///
/// 回傳未跳脫的 argv 元素清單，宣告的是「要求什麼」而非「命令列長什麼樣」——
/// 引號與拼接由客戶端工具作業統一處理，引號缺陷因此不可能源自此處。
/// 連線參數（<c>-h</c>、<c>-p</c>、<c>-U</c>、<c>-d</c>）亦由該模組補上，不在此產生。
/// </summary>
public static class BackupArgumentsBuilder
{
    public static IReadOnlyList<string> Build(BackupOptions options, string outputFilePath)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(outputFilePath))
            throw new ArgumentException("Output file path cannot be empty.", nameof(outputFilePath));

        var argv = new List<string>
        {
            // 格式指定 (-Fc: 自訂格式, -Fp: 純文字腳本)
            options.Format == BackupFormat.Custom ? "-Fc" : "-Fp"
        };

        // 備份模式
        switch (options.Mode)
        {
            case BackupMode.SchemaOnly:
                argv.Add("--schema-only");
                break;
            case BackupMode.DataOnly:
                argv.Add("--data-only");
                break;
        }

        // 備份範圍
        if (options.Scope == BackupScope.SpecificSchemas && options.Schemas.Count > 0)
        {
            foreach (var schema in options.Schemas.Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                argv.Add("-n");
                argv.Add(schema.Trim());
            }
        }
        else if (options.Scope == BackupScope.SpecificTables && options.Tables.Count > 0)
        {
            foreach (var table in options.Tables.Where(t => !string.IsNullOrWhiteSpace(t)))
            {
                argv.Add("-t");
                argv.Add(table.Trim());
            }
        }

        // 詳細輸出旗標 (使 pg_dump 能輸出即時進度日誌)
        argv.Add("-v");

        // 輸出檔案路徑
        argv.Add("-f");
        argv.Add(outputFilePath);

        return argv;
    }
}
