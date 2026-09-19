using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

namespace PostgresBackup.Cli.Commands;

public static class RestoreCommand
{
    public static Command Create(IServiceProvider services)
    {
        var fileOption = new Option<string>("--file", "-f")
        {
            Description = "來源備份檔案路徑 (.dump 或 .sql)",
            Required = true
        };

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
            Description = "PostgreSQL 伺服器連接埠"
        };

        var databaseOption = new Option<string?>("--database", "-d")
        {
            Description = "目標資料庫名稱"
        };

        var usernameOption = new Option<string?>("--username", "-u")
        {
            Description = "資料庫使用者名稱"
        };

        var passwordOption = new Option<string?>("--password", "-p")
        {
            Description = "資料庫密碼"
        };

        var modeOption = new Option<string?>("--mode", "-m")
        {
            Description = "還原模式：normal (一般還原), clean (清除並重建 --clean --create), data (僅資料 --data-only)，預設為 normal"
        };

        var noSnapshotOption = new Option<bool>("--no-snapshot")
        {
            Description = "關閉還原前強制安全快照（預設會先對目標資料庫執行一次備份以防誤操作）"
        };

        var yesOption = new Option<bool>("--yes", "-y")
        {
            Description = "自動同意高危險操作確認，不跳出互動提示（適合自動化腳本）"
        };

        var pgBinPathOption = new Option<string?>("--pg-bin-path")
        {
            Description = "自訂 PostgreSQL 客戶端工具所在目錄"
        };

        var logFileOption = new Option<string?>("--log")
        {
            Description = "指定額外寫入日誌之檔案路徑"
        };

        var command = new Command("restore", "執行 PostgreSQL 安全還原作業（預設具備安全快照防護）");
        command.Add(fileOption);
        command.Add(profileOption);
        command.Add(hostOption);
        command.Add(portOption);
        command.Add(databaseOption);
        command.Add(usernameOption);
        command.Add(passwordOption);
        command.Add(modeOption);
        command.Add(noSnapshotOption);
        command.Add(yesOption);
        command.Add(pgBinPathOption);
        command.Add(logFileOption);

        command.SetAction(async parseResult =>
        {
            var filePath = parseResult.GetValue(fileOption)!;
            var profileName = parseResult.GetValue(profileOption);
            var host = parseResult.GetValue(hostOption);
            var port = parseResult.GetValue(portOption);
            var database = parseResult.GetValue(databaseOption);
            var username = parseResult.GetValue(usernameOption);
            var password = parseResult.GetValue(passwordOption);
            var modeStr = parseResult.GetValue(modeOption);
            var noSnapshot = parseResult.GetValue(noSnapshotOption);
            var yes = parseResult.GetValue(yesOption);
            var pgBinPath = parseResult.GetValue(pgBinPathOption);
            var logFilePath = parseResult.GetValue(logFileOption);

            var profileRepo = services.GetRequiredService<IConnectionProfileRepository>();
            var restoreService = services.GetRequiredService<IRestoreService>();

            var connSettings = new ConnectionSettings();

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
                    Console.WriteLine($"[ERROR] 找不到名為 '{profileName}' 的連線設定檔，還原作業已中止。");
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

            if (!string.IsNullOrWhiteSpace(host)) connSettings.Host = host;
            if (port.HasValue && port.Value > 0) connSettings.Port = port.Value;
            if (!string.IsNullOrWhiteSpace(database)) connSettings.Database = database;
            if (!string.IsNullOrWhiteSpace(username)) connSettings.Username = username;
            if (!string.IsNullOrWhiteSpace(password)) connSettings.Password = password;

            var targetDb = !string.IsNullOrWhiteSpace(database) ? database : connSettings.Database;

            // 高危險操作確認
            if (!yes)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[⚠️ 高危險操作警告]");
                Console.WriteLine($"您即將對資料庫 '{targetDb}' 執行還原作業，既有資料可能被覆蓋或刪除！");
                Console.ResetColor();
                Console.Write($"若確定執行，請輸入目標資料庫名稱 '{targetDb}': ");
                var input = Console.ReadLine();
                if (!string.Equals(input?.Trim(), targetDb, StringComparison.OrdinalIgnoreCase))
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("輸入不符，還原作業已安全取消。");
                    Console.ResetColor();
                    return 1;
                }
            }

            var mode = modeStr?.ToLowerInvariant() switch
            {
                "clean" => RestoreMode.CleanAndRecreate,
                "data" => RestoreMode.DataOnly,
                _ => RestoreMode.Normal
            };

            var format = RestoreOptions.DetectFormatFromFilePath(filePath);

            var options = new RestoreOptions
            {
                Connection = connSettings,
                SourceFilePath = filePath,
                TargetDatabase = targetDb,
                Format = format,
                Mode = mode,
                CreatePreRestoreSnapshot = !noSnapshot,
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
                catch { }
            }

            void OnLog(string line)
            {
                Console.WriteLine(line);
                logWriter?.WriteLine(line);
            }

            Console.WriteLine("=================================================");
            Console.WriteLine(" PostgresBackup CLI — 安全還原作業");
            Console.WriteLine("=================================================");

            var result = await restoreService.RestoreAsync(options, OnLog);

            logWriter?.Dispose();

            if (result.IsSuccess)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n還原作業成功完成！");
                if (result.SnapshotCreated)
                {
                    Console.WriteLine($"前置安全快照保留於: {result.SnapshotFilePath}");
                }
                Console.ResetColor();
                return 0;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n還原作業中止或失敗：{result.ErrorMessage}");
                Console.ResetColor();
                return 1;
            }
        });

        return command;
    }
}
