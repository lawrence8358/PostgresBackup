using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

namespace PostgresBackup.Cli.Commands;

public static class BackupCommand
{
    public static Command Create(IServiceProvider services)
    {
        var profileOption = new Option<string?>("--profile")
        {
            Description = "指定已儲存之連線設定檔名稱或識別碼"
        };

        var hostOption = new Option<string?>("--host", "-H")
        {
            Description = "PostgreSQL 伺服器主機位址"
        };

        var portOption = new Option<int?>("--port", "-P")
        {
            Description = "PostgreSQL 伺服器連接埠 (預設 5432)"
        };

        var databaseOption = new Option<string?>("--database", "-d")
        {
            Description = "資料庫名稱"
        };

        var usernameOption = new Option<string?>("--username", "-u")
        {
            Description = "資料庫使用者名稱"
        };

        var passwordOption = new Option<string?>("--password", "-p")
        {
            Description = "資料庫密碼"
        };

        var formatOption = new Option<string?>("--format", "-f")
        {
            Description = "備份格式：custom (自訂二進位 -Fc) 或 plain (純文字 SQL -Fp)，預設為 custom"
        };

        var modeOption = new Option<string?>("--mode", "-m")
        {
            Description = "備份模式：all (結構與資料), schema (僅結構), data (僅資料)，預設為 all"
        };

        var schemaOption = new Option<string[]>("--schema", "-n")
        {
            Description = "指定備份綱要 (Schema)，可重複指定多個",
            AllowMultipleArgumentsPerToken = true
        };

        var tableOption = new Option<string[]>("--table", "-t")
        {
            Description = "指定備份資料表 (Table)，可重複指定多個",
            AllowMultipleArgumentsPerToken = true
        };

        var outputDirOption = new Option<string?>("--output-dir", "-o")
        {
            Description = "備份檔案輸出目錄"
        };

        var outputFileOption = new Option<string?>("--output-file")
        {
            Description = "自訂輸出檔案名稱 (預設自動依 {database}_{yyyyMMddHHmmss} 規範命名)"
        };

        var pgBinPathOption = new Option<string?>("--pg-bin-path")
        {
            Description = "自訂 pg_dump 工具所在目錄"
        };

        var logFileOption = new Option<string?>("--log")
        {
            Description = "指定額外寫入日誌之檔案路徑"
        };

        var command = new Command("backup", "執行 PostgreSQL 資料庫備份作業");
        command.Add(profileOption);
        command.Add(hostOption);
        command.Add(portOption);
        command.Add(databaseOption);
        command.Add(usernameOption);
        command.Add(passwordOption);
        command.Add(formatOption);
        command.Add(modeOption);
        command.Add(schemaOption);
        command.Add(tableOption);
        command.Add(outputDirOption);
        command.Add(outputFileOption);
        command.Add(pgBinPathOption);
        command.Add(logFileOption);

        command.SetAction(async parseResult =>
        {
            var profileName = parseResult.GetValue(profileOption);
            var host = parseResult.GetValue(hostOption);
            var port = parseResult.GetValue(portOption);
            var database = parseResult.GetValue(databaseOption);
            var username = parseResult.GetValue(usernameOption);
            var password = parseResult.GetValue(passwordOption);
            var formatStr = parseResult.GetValue(formatOption);
            var modeStr = parseResult.GetValue(modeOption);
            var schemas = parseResult.GetValue(schemaOption);
            var tables = parseResult.GetValue(tableOption);
            var outputDir = parseResult.GetValue(outputDirOption);
            var outputFile = parseResult.GetValue(outputFileOption);
            var pgBinPath = parseResult.GetValue(pgBinPathOption);
            var logFilePath = parseResult.GetValue(logFileOption);

            var profileRepo = services.GetRequiredService<IConnectionProfileRepository>();
            var backupService = services.GetRequiredService<IBackupService>();

            var connSettings = new ConnectionSettings();

            // 若有指定 Profile，先讀取 Profile
            if (!string.IsNullOrWhiteSpace(profileName))
            {
                // 存取被拒與「找不到設定」是兩回事：此存放區只有系統管理員讀得到，
                // 折疊成後者會讓一般使用者去重建一份其實已經存在的設定。
                var profiles = await ProfileStoreAccess.TryLoadAllAsync(profileRepo);
                if (profiles == null)
                {
                    return 1;
                }

                var matched = profiles.FirstOrDefault(p =>
                    string.Equals(p.Id, profileName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Name, profileName, StringComparison.OrdinalIgnoreCase));

                if (matched != null)
                {
                    var (passwordRead, profilePassword) =
                        await ProfileStoreAccess.TryGetPasswordAsync(profileRepo, matched.Id);
                    if (!passwordRead)
                    {
                        return 1;
                    }

                    ProfileStoreAccess.WarnIfPasswordMissing(matched.Name, profilePassword);

                    connSettings.Host = matched.Host;
                    connSettings.Port = matched.Port;
                    connSettings.Database = matched.Database;
                    connSettings.Username = matched.Username;
                    connSettings.Password = profilePassword;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[ERROR] 找不到名為 '{profileName}' 的連線設定檔，備份作業已中止。");
                    Console.ResetColor();
                    return 1;
                }
            }

            if (!string.IsNullOrWhiteSpace(password))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[WARNING] 使用 -p/--password 傳入密碼會暴露於行程資訊中（例如工作管理員、`Get-CimInstance Win32_Process` 或 PowerShell 歷史紀錄），建議改用命令列連線設定 (--profile)。");
                Console.ResetColor();
            }

            // 命令列參數覆蓋
            if (!string.IsNullOrWhiteSpace(host)) connSettings.Host = host;
            if (port.HasValue && port.Value > 0) connSettings.Port = port.Value;
            if (!string.IsNullOrWhiteSpace(database)) connSettings.Database = database;
            if (!string.IsNullOrWhiteSpace(username)) connSettings.Username = username;
            if (!string.IsNullOrWhiteSpace(password)) connSettings.Password = password;

            // 格式與模式
            var format = string.Equals(formatStr, "plain", StringComparison.OrdinalIgnoreCase)
                ? BackupFormat.Plain
                : BackupFormat.Custom;

            var mode = modeStr?.ToLowerInvariant() switch
            {
                "schema" => BackupMode.SchemaOnly,
                "data" => BackupMode.DataOnly,
                _ => BackupMode.SchemaAndData
            };

            var scope = BackupScope.FullDatabase;
            var schemaList = new List<string>();
            var tableList = new List<string>();

            if (schemas != null && schemas.Length > 0)
            {
                scope = BackupScope.SpecificSchemas;
                schemaList.AddRange(schemas);
            }
            else if (tables != null && tables.Length > 0)
            {
                scope = BackupScope.SpecificTables;
                tableList.AddRange(tables);
            }

            var options = new BackupOptions
            {
                Connection = connSettings,
                Format = format,
                Mode = mode,
                Scope = scope,
                Schemas = schemaList,
                Tables = tableList,
                OutputDirectory = outputDir ?? Path.Combine(Environment.CurrentDirectory, "backups"),
                CustomFileName = outputFile,
                ClientToolDirectory = pgBinPath
            };

            StreamWriter? logWriter = null;
            if (!string.IsNullOrWhiteSpace(logFilePath))
            {
                try
                {
                    var logDir = Path.GetDirectoryName(logFilePath);
                    if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
                    {
                        Directory.CreateDirectory(logDir);
                    }
                    logWriter = new StreamWriter(logFilePath, append: true) { AutoFlush = true };
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[WARNING] 無法開啟日誌檔案 '{logFilePath}': {ex.Message}");
                    Console.ResetColor();
                }
            }

            void OnLog(string line)
            {
                Console.WriteLine(line);
                logWriter?.WriteLine(line);
            }

            Console.WriteLine("=================================================");
            Console.WriteLine(" PostgresBackup CLI — 備份作業");
            Console.WriteLine("=================================================");

            var result = await backupService.BackupAsync(options, OnLog);

            logWriter?.Dispose();

            if (result.IsSuccess)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n備份成功完成！產出檔案：{result.OutputFilePath}");
                Console.ResetColor();
                return 0;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n備份失敗：{result.ErrorMessage}");
                Console.ResetColor();
                return 1;
            }
        });

        return command;
    }
}
