using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

namespace PostgresBackup.Cli.Commands;

public static class CheckToolsCommand
{
    public static Command Create(IServiceProvider services)
    {
        var binPathOption = new Option<string?>("--pg-bin-path")
        {
            Description = "指定 pg_dump / pg_restore 所在之自訂目錄"
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

        var connStringOption = new Option<string?>("--connection-string", "-s")
        {
            Description = "完整 PostgreSQL 連線字串（覆蓋其他個別連線參數）"
        };

        var jsonOption = new Option<bool>("--json")
        {
            Description = "以 JSON 格式輸出診斷結果"
        };

        var command = new Command("check-tools", "檢查 PostgreSQL 官方客戶端工具 (pg_dump / pg_restore) 狀態與相容性");
        command.Add(binPathOption);
        command.Add(hostOption);
        command.Add(portOption);
        command.Add(databaseOption);
        command.Add(usernameOption);
        command.Add(passwordOption);
        command.Add(connStringOption);
        command.Add(jsonOption);

        command.SetAction(async parseResult =>
        {
            var binPath = parseResult.GetValue(binPathOption);
            var host = parseResult.GetValue(hostOption);
            var port = parseResult.GetValue(portOption);
            var database = parseResult.GetValue(databaseOption);
            var username = parseResult.GetValue(usernameOption);
            var password = parseResult.GetValue(passwordOption);
            var connStr = parseResult.GetValue(connStringOption);
            var asJson = parseResult.GetValue(jsonOption);

            var detector = services.GetRequiredService<IToolDetectionService>();
            var detectionResult = await detector.DetectAsync(binPath);

            VersionCheckResult? versionCheck = null;
            bool shouldCheckServer = !string.IsNullOrWhiteSpace(connStr) ||
                                     !string.IsNullOrWhiteSpace(host) ||
                                     !string.IsNullOrWhiteSpace(database);

            if (shouldCheckServer && detectionResult.IsReady)
            {
                var connSettings = new ConnectionSettings
                {
                    Host = host ?? "localhost",
                    Port = port ?? 5432,
                    Database = database ?? "postgres",
                    Username = username ?? "postgres",
                    Password = password,
                    ConnectionString = connStr
                };

                versionCheck = await detector.CheckCompatibilityAsync(detectionResult, connSettings.ToConnectionString());
            }

            if (asJson)
            {
                var jsonModel = new
                {
                    Status = detectionResult.Status.ToString(),
                    Source = detectionResult.Source.ToString(),
                    PgDumpPath = detectionResult.PgDumpPath,
                    PgRestorePath = detectionResult.PgRestorePath,
                    PsqlPath = detectionResult.PsqlPath,
                    ClientVersion = detectionResult.Version?.ToString(),
                    ServerCheck = versionCheck == null ? null : new
                    {
                        versionCheck.IsCompatible,
                        versionCheck.ServerVersionString,
                        versionCheck.ServerMajorVersion,
                        versionCheck.Message
                    }
                };

                Console.WriteLine(JsonSerializer.Serialize(jsonModel, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                PrintDiagnosticReport(detectionResult, versionCheck);
            }

            int exitCode = detectionResult.Status switch
            {
                ToolStatus.Ready => (versionCheck?.IsCompatible == false ? 2 : 0),
                ToolStatus.Incompatible => 2,
                _ => 1
            };

            return exitCode;
        });

        return command;
    }

    private static void PrintDiagnosticReport(ToolDetectionResult result, VersionCheckResult? versionCheck)
    {
        Console.WriteLine();
        Console.WriteLine("================================================================================");
        Console.WriteLine("  PostgreSQL 客戶端工具診斷報告 (Client Tools Diagnostic Report)");
        Console.WriteLine("================================================================================");

        string statusText = result.Status switch
        {
            ToolStatus.Ready => "[ 就緒 / READY ]",
            ToolStatus.Incompatible => "[ 版本不相容 / INCOMPATIBLE ]",
            _ => "[ 未偵測到 / NOT FOUND ]"
        };

        var originalColor = Console.ForegroundColor;
        Console.Write("  工具狀態: ");
        Console.ForegroundColor = result.Status switch
        {
            ToolStatus.Ready => ConsoleColor.Green,
            ToolStatus.Incompatible => ConsoleColor.Yellow,
            _ => ConsoleColor.Red
        };
        Console.WriteLine(statusText);
        Console.ForegroundColor = originalColor;

        Console.WriteLine($"  偵測來源: {result.Source}");
        Console.WriteLine($"  pg_dump   : {result.PgDumpPath ?? "(未找到)"}");
        Console.WriteLine($"  pg_restore: {result.PgRestorePath ?? "(未找到)"}");
        Console.WriteLine($"  psql      : {result.PsqlPath ?? "(未找到)"}");
        Console.WriteLine($"  工具版本  : {result.Version?.ToString() ?? "(未知)"}");

        if (versionCheck != null)
        {
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.WriteLine("  伺服器版本相容性檢查:");
            Console.Write("  相容結果: ");
            Console.ForegroundColor = versionCheck.IsCompatible ? ConsoleColor.Green : ConsoleColor.Yellow;
            Console.WriteLine(versionCheck.IsCompatible ? "[ 相容 / COMPATIBLE ]" : "[ 警告: 不相容 / INCOMPATIBLE ]");
            Console.ForegroundColor = originalColor;
            Console.WriteLine($"  伺服器版本: {versionCheck.ServerVersionString ?? versionCheck.ServerMajorVersion?.ToString() ?? "(未知)"}");
            Console.WriteLine($"  檢查訊息  : {versionCheck.Message}");
        }

        if (result.Status != ToolStatus.Ready || (versionCheck != null && !versionCheck.IsCompatible))
        {
            Console.WriteLine();
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.WriteLine("  官方安裝與設定指引 (Official Installation Guide):");
            Console.WriteLine("  PostgresBackup 需要官方客戶端工具才能執行完整備份與還原作業。");
            Console.WriteLine();
            Console.WriteLine("  [1] 使用 Windows 內建 winget 一鍵安裝:");
            Console.WriteLine("      > winget install PostgreSQL.PostgreSQL");
            Console.WriteLine();
            Console.WriteLine("  [2] 前往 PostgreSQL 官方 Windows 下載頁面安裝:");
            Console.WriteLine("      https://www.postgresql.org/download/windows/");
            Console.WriteLine();
            Console.WriteLine("  [3] EnterpriseDB (EDB) 下載專區:");
            Console.WriteLine("      https://www.enterprisedb.com/downloads/postgres-postgresql-downloads");
            Console.WriteLine();
            Console.WriteLine("  若已安裝至自訂目錄，請使用 --pg-bin-path 參數指定 bin 目錄位置。");
        }

        Console.WriteLine("================================================================================");
        Console.WriteLine();
    }
}
