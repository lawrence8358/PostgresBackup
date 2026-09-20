using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 官方 pg_restore 與 psql 還原參數構建器。
///
/// 回傳未跳脫的 argv 元素清單；引號與拼接由客戶端工具作業統一處理，
/// 連線參數（<c>-h</c>、<c>-p</c>、<c>-U</c>、<c>-d</c>）亦由該模組補上，不在此產生。
/// </summary>
public static class RestoreArgumentsBuilder
{
    public static IReadOnlyList<string> Build(RestoreOptions options, string? useListFilePath = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.SourceFilePath))
            throw new ArgumentException("Source file path cannot be empty.", nameof(options));

        var argv = new List<string>();

        if (options.Format == BackupFormat.Custom)
        {
            // pg_restore 模式旗標
            switch (options.Mode)
            {
                case RestoreMode.Normal:
                    if (string.IsNullOrWhiteSpace(useListFilePath))
                    {
                        throw new InvalidOperationException(
                            "Normal restore requires a filtered archive list.");
                    }

                    argv.Add("--use-list");
                    argv.Add(useListFilePath);
                    // Defense in depth: if a target object appears between inspection
                    // and restore, never copy archive data into that existing table.
                    argv.Add("--no-data-for-failed-tables");
                    break;
                case RestoreMode.CleanAndRecreate:
                    // Restore into the selected target database. --create would use the
                    // database name stored in the archive, which breaks cross-database restores.
                    argv.Add("--clean");
                    argv.Add("--if-exists");
                    break;
                case RestoreMode.DataOnly:
                    if (string.IsNullOrWhiteSpace(useListFilePath))
                    {
                        throw new InvalidOperationException(
                            "Data-only restore requires an ordered archive list.");
                    }

                    argv.Add("--data-only");
                    argv.Add("--use-list");
                    argv.Add(useListFilePath);
                    break;
            }

            // Never continue through the remaining TOC after a genuine restore error.
            argv.Add("--exit-on-error");

            // 詳細進度輸出
            argv.Add("-v");

            // 來源檔案路徑
            argv.Add(options.SourceFilePath);
        }
        else
        {
            // psql 腳本執行模式
            argv.Add("-f");
            argv.Add(options.SourceFilePath);
        }

        return argv;
    }
}
