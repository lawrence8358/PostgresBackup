using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using PostgresBackup.Core.Exceptions;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Cli.Commands;

/// <summary>
/// 命令列連線設定的管理指令群組。
/// 此處建立的設定儲存於機器層級的加密存放區，可供以 SYSTEM 身分執行的排程任務讀取，
/// 與圖形介面的介面連線設定彼此獨立。
/// </summary>
public static class ProfileCommand
{
    public static Command Create(IServiceProvider services)
    {
        var command = new Command("profile", "管理命令列連線設定（供排程任務使用，與圖形介面的連線設定各自獨立）");
        command.Add(CreateSetCommand(services));
        command.Add(CreateListCommand(services));
        command.Add(CreateRemoveCommand(services));
        return command;
    }

    private static Command CreateSetCommand(IServiceProvider services)
    {
        var nameOption = new Option<string>("--name")
        {
            Description = "連線設定名稱；使用既有名稱即為更新該設定",
            Required = true
        };

        var hostOption = new Option<string?>("--host", "-H")
        {
            Description = "PostgreSQL 伺服器主機位址 (預設 localhost)"
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

        var passwordStdinOption = new Option<bool>("--password-stdin")
        {
            Description = "自標準輸入讀取密碼（供自動化部署以管線提供），不接受任何密碼值"
        };

        var forceOption = new Option<bool>("--force")
        {
            Description = "略過存檔前的連線驗證，直接儲存；僅在資料庫暫時無法連線時使用"
        };

        // 刻意不提供以命令列參數傳入密碼的選項：密碼一律以互動式遮蔽輸入或標準輸入取得，
        // 以免在修正密碼暴露問題的同時又重製同一個問題。
        var command = new Command("set",
            "建立或更新命令列連線設定；密碼以互動式遮蔽輸入或標準輸入取得，並於存檔前實際連線驗證");
        command.Add(nameOption);
        command.Add(hostOption);
        command.Add(portOption);
        command.Add(databaseOption);
        command.Add(usernameOption);
        command.Add(passwordStdinOption);
        command.Add(forceOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var name = parseResult.GetValue(nameOption);
            var host = parseResult.GetValue(hostOption);
            var port = parseResult.GetValue(portOption);
            var database = parseResult.GetValue(databaseOption);
            var username = parseResult.GetValue(usernameOption);
            var fromStandardInput = parseResult.GetValue(passwordStdinOption);
            var force = parseResult.GetValue(forceOption);

            if (string.IsNullOrWhiteSpace(name))
            {
                WriteError("必須以 --name 指定連線設定名稱。");
                return 1;
            }

            var profileRepo = services.GetRequiredService<IConnectionProfileRepository>();
            var passwordReader = services.GetRequiredService<IPasswordReader>();

            var allProfiles = await ProfileStoreAccess.TryLoadAllAsync(profileRepo, ct);
            if (allProfiles == null)
            {
                return 1;
            }

            var existing = allProfiles
                .FirstOrDefault(p =>
                    string.Equals(p.Id, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            // 密碼取得的單一入口：只有 --password-stdin 決定來源，其餘流程一律相同。
            var password = fromStandardInput
                ? passwordReader.ReadPasswordFromStandardInput()
                : passwordReader.ReadPassword($"請輸入 '{name}' 的資料庫密碼（輸入不會顯示）：");

            if (string.IsNullOrEmpty(password))
            {
                WriteError(fromStandardInput
                    ? "標準輸入中沒有密碼，連線設定並未儲存。請以管線提供密碼，例如：echo 密碼 | pgbackup profile set --name ... --password-stdin"
                    : "未取得密碼，連線設定並未儲存。密碼必須以互動式遮蔽輸入提供。");
                return 1;
            }

            // 相同名稱視為更新：沿用既有識別碼與建立時間，覆寫其餘欄位。
            var profile = new ConnectionProfile
            {
                Id = existing?.Id ?? name,
                Name = name,
                Host = host ?? existing?.Host ?? "localhost",
                Port = port is > 0 ? port.Value : existing?.Port ?? 5432,
                Database = database ?? existing?.Database ?? "postgres",
                Username = username ?? existing?.Username ?? "postgres",
                CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow,
                LastUsedAt = existing?.LastUsedAt
            };

            // 預設先實際連線驗證帳號密碼，驗證不通過即拒絕儲存：
            // 讓使用者當場就發現帳密打錯，而不是等到半夜排程失敗。
            if (!force)
            {
                var toolDetection = services.GetRequiredService<IToolDetectionService>();
                var connectionSettings = new ConnectionSettings
                {
                    Host = profile.Host,
                    Port = profile.Port,
                    Database = profile.Database,
                    Username = profile.Username,
                    Password = password
                };

                Console.WriteLine($"正在連線至 {profile.Host}:{profile.Port} 驗證帳號密碼……");

                var verification = await toolDetection.VerifyConnectionAsync(
                    connectionSettings.ToConnectionString(), ct);

                if (!verification.IsSuccess)
                {
                    WriteError($"連線驗證失敗，連線設定並未儲存：{SensitiveText.Redact(verification.Message, password)}");
                    Console.WriteLine("請確認主機、連接埠、資料庫、使用者名稱與密碼是否正確。");
                    Console.WriteLine("若資料庫目前暫時無法連線，可加上 --force 略過驗證先行儲存。");
                    return 1;
                }

                Console.WriteLine(string.IsNullOrWhiteSpace(verification.ServerVersionString)
                    ? "連線驗證成功。"
                    : $"連線驗證成功（伺服器版本 {verification.ServerVersionString}）。");
            }

            // 權限必須在密碼落地「之前」確認。若先寫入再警告，使用者看到警告時
            // 加密密碼已經躺在同機一般使用者讀得到的資料夾裡，警告已無法補救。
            var environmentProbe = services.GetRequiredService<IEnvironmentProbe>();
            if (!EnsureStoreDirectoryIsSecure(environmentProbe, GetStoreLocation(services)))
            {
                return 1;
            }

            try
            {
                await profileRepo.SaveProfileAsync(profile, password, ct);
            }
            catch (CredentialStorageException ex)
            {
                WriteError(ex.Message);
                return 1;
            }
            catch (UnauthorizedAccessException)
            {
                WriteError("沒有權限寫入命令列連線設定存放區。請以系統管理員身分重新執行此指令。");
                return 1;
            }
            catch (Exception ex)
            {
                WriteError($"儲存命令列連線設定失敗：{ex.Message}");
                return 1;
            }

            Console.WriteLine(existing != null
                ? $"已更新命令列連線設定 '{profile.Name}'。"
                : $"已建立命令列連線設定 '{profile.Name}'。");
            Console.WriteLine($"  主機：{profile.Host}:{profile.Port}");
            Console.WriteLine($"  資料庫：{profile.Database}");
            Console.WriteLine($"  使用者名稱：{profile.Username}");
            Console.WriteLine($"排程腳本即可直接使用 --profile {profile.Name}，不需在腳本中放入任何密碼。");

            if (force)
            {
                WriteWarning(
                    $"已使用 --force 略過連線驗證，這份設定尚未被證實可以連上資料庫。" +
                    $"其中的主機、資料庫、使用者名稱或密碼若有任何一項是錯的，現在不會有人發現，" +
                    $"要等到排程真正執行時才會失敗。資料庫恢復連線後，" +
                    $"請重新執行一次不加 --force 的 pgbackup profile set --name {profile.Name} 以確認設定可用。");
            }

            return 0;
        });

        return command;
    }

    private static Command CreateListCommand(IServiceProvider services)
    {
        var command = new Command("list", "列出目前所有命令列連線設定（供排程任務使用），包含密碼狀態與存放區權限檢查");

        command.SetAction(async (parseResult, ct) =>
        {
            var profileRepo = services.GetRequiredService<IConnectionProfileRepository>();
            var environmentProbe = services.GetRequiredService<IEnvironmentProbe>();

            WarnIfStoreDirectoryPermissionsAreUnexpected(environmentProbe, GetStoreLocation(services));

            var profiles = await ProfileStoreAccess.TryLoadAllAsync(profileRepo, ct);
            if (profiles == null)
            {
                return 1;
            }

            if (profiles.Count == 0)
            {
                Console.WriteLine("目前沒有任何命令列連線設定。請先執行 pgbackup profile set 建立一筆。");
                return 0;
            }

            Console.WriteLine("命令列連線設定（來源：命令列，與圖形介面的連線設定各自獨立）：");

            foreach (var profile in profiles.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                // 密碼狀態僅呈現有無，不得呈現內容或長度：僅檢查是否為 null/空字串。
                var (passwordRead, password) =
                    await ProfileStoreAccess.TryGetPasswordAsync(profileRepo, profile.Id, ct);
                if (!passwordRead)
                {
                    return 1;
                }

                var passwordStatus = string.IsNullOrEmpty(password) ? "遺失" : "已設定";

                Console.WriteLine($"- {profile.Name}");
                Console.WriteLine($"    主機：{profile.Host}:{profile.Port}");
                Console.WriteLine($"    資料庫：{profile.Database}");
                Console.WriteLine($"    使用者名稱：{profile.Username}");
                Console.WriteLine("    來源：命令列連線設定");
                Console.WriteLine($"    建立時間：{profile.CreatedAt:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"    密碼狀態：{passwordStatus}");
            }

            return 0;
        });

        return command;
    }

    private static Command CreateRemoveCommand(IServiceProvider services)
    {
        var nameOption = new Option<string>("--name")
        {
            Description = "要刪除的命令列連線設定名稱",
            Required = true
        };

        var command = new Command("remove", "刪除命令列連線設定，並一併清除其加密密碼");
        command.Add(nameOption);

        command.SetAction(async (parseResult, ct) =>
        {
            var name = parseResult.GetValue(nameOption);

            if (string.IsNullOrWhiteSpace(name))
            {
                WriteError("必須以 --name 指定要刪除的連線設定名稱。");
                return 1;
            }

            var profileRepo = services.GetRequiredService<IConnectionProfileRepository>();

            var storedProfiles = await ProfileStoreAccess.TryLoadAllAsync(profileRepo, ct);
            if (storedProfiles == null)
            {
                return 1;
            }

            var existing = storedProfiles
                .FirstOrDefault(p =>
                    string.Equals(p.Id, name, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                WriteError($"找不到名為 '{name}' 的命令列連線設定，無法刪除。");
                return 1;
            }

            // DeleteProfileAsync 先清密碼、後移除設定，不留下孤兒憑證。
            // 清不掉時它會拋出而非回傳 false，因此不能無條件宣告「已一併清除」。
            try
            {
                if (!await profileRepo.DeleteProfileAsync(existing.Id, ct))
                {
                    WriteError($"刪除命令列連線設定 '{existing.Name}' 失敗：該設定已不存在。");
                    return 1;
                }
            }
            catch (Exception ex)
            {
                WriteError(
                    $"刪除命令列連線設定 '{existing.Name}' 失敗，設定與密碼都沒有被移除：{ex.Message}");
                return 1;
            }

            Console.WriteLine($"已刪除命令列連線設定 '{existing.Name}'，其加密密碼已一併清除。");

            return 0;
        });

        return command;
    }

    /// <summary>
    /// 取得存放區位置。權限檢查與實際寫入必須指向同一個目錄，
    /// 因此位置一律自相依注入取得，不在此硬編預設路徑。
    /// </summary>
    private static MachineScopedStoreLocation GetStoreLocation(IServiceProvider services)
        => services.GetService<MachineScopedStoreLocation>() ?? MachineScopedStoreLocation.Default;

    /// <summary>
    /// 判斷存放區目錄的權限是否仍限定於 Administrators 與 SYSTEM。
    /// 尚未建立、無法查詢，或此位置未要求限制性權限時回傳 <c>null</c>
    /// （沒有可評估的權限狀態）。
    /// </summary>
    private static bool? IsStoreDirectorySecure(
        IEnvironmentProbe environmentProbe,
        MachineScopedStoreLocation location)
    {
        if (!location.EnforceRestrictivePermissions)
        {
            return null;
        }

        var info = environmentProbe.GetDirectoryAccessInfo(location.Directory);

        if (info == null || !info.Exists)
        {
            return null;
        }

        var expectedSids = new HashSet<string>(
            [MachineScopedStore.AdministratorsSid, MachineScopedStore.LocalSystemSid],
            StringComparer.OrdinalIgnoreCase);

        return !info.InheritanceEnabled
            && info.AllowedIdentities.Count > 0
            && info.AllowedIdentities.All(sid => expectedSids.Contains(sid));
    }

    /// <summary>
    /// 在密碼寫入磁碟之前確認存放區的狀態。此檢查必須在 <c>SaveProfileAsync</c> 之前執行：
    /// 事後才警告已經太遲，密碼屆時已落在同機一般使用者讀得到的位置。
    /// <para>
    /// 對「目錄已存在但權限不符預期」刻意採取拒絕而非自動收緊。Windows 的目錄擁有者
    /// 隱含具備變更權限的能力，因此若目錄是被他人搶先建立的，我們即使把權限收緊，
    /// 對方仍是擁有者、仍能事後把自己加回去——收緊後就當作安全是假的保證。
    /// 這種狀態需要人為介入釐清，不該由本指令靜默接手。
    /// </para>
    /// </summary>
    private static bool EnsureStoreDirectoryIsSecure(
        IEnvironmentProbe environmentProbe,
        MachineScopedStoreLocation location)
    {
        if (!location.EnforceRestrictivePermissions)
        {
            try
            {
                MachineScopedStore.EnsureRestrictedDirectory(location.Directory, false);
                return true;
            }
            catch (Exception ex)
            {
                WriteError($"無法建立命令列連線設定存放區：{ex.Message}");
                return false;
            }
        }

        var existing = environmentProbe.GetDirectoryAccessInfo(location.Directory);

        if (existing is { Exists: true })
        {
            // 目錄已存在：只接受權限本來就符合預期的情形。
            if (IsStoreDirectorySecure(environmentProbe, location) == true)
            {
                return true;
            }

            WriteError(
                "命令列連線設定存放區已經存在，但其檔案權限並未限定於系統管理員與 SYSTEM。" +
                "為避免密碼落在同機一般使用者讀得到的位置，這次不會儲存任何資料。");
            Console.WriteLine($"存放區位置：{location.Directory}");
            Console.WriteLine("這有可能只是權限被人不慎放寬，也有可能是他人搶先建立了這個資料夾。");
            Console.WriteLine("由於資料夾的建立者始終保有變更權限的能力，僅僅把權限改回來並不足以確保安全。");
            Console.WriteLine("請以系統管理員身分確認該資料夾的來歷：若其中沒有你需要保留的連線設定，");
            Console.WriteLine("最乾淨的做法是直接刪除整個資料夾，再重新執行一次本指令，由本工具重新建立。");
            return false;
        }

        if (existing is { Exists: false } or null && Directory.Exists(location.Directory))
        {
            // 目錄確實存在，卻讀不到它的權限——這是最可疑的狀態，不應視為安全。
            WriteError(
                "命令列連線設定存放區已經存在，但無法讀取其檔案權限，因此無法確認它是否安全。" +
                "這次不會儲存任何資料。");
            Console.WriteLine($"存放區位置：{location.Directory}");
            Console.WriteLine("請以系統管理員身分重新執行此指令。");
            return false;
        }

        // 目錄尚不存在：由本工具建立，並在建立當下套用限制性權限。
        try
        {
            MachineScopedStore.EnsureRestrictedDirectory(location.Directory, true);
        }
        catch (Exception ex)
        {
            WriteError($"無法建立命令列連線設定存放區：{ex.Message}");
            return false;
        }

        if (IsStoreDirectorySecure(environmentProbe, location) != true)
        {
            WriteError(
                "已建立命令列連線設定存放區，但無法確認其檔案權限已限定於系統管理員與 SYSTEM，" +
                "因此這次不會儲存任何資料。");
            Console.WriteLine($"存放區位置：{location.Directory}");
            Console.WriteLine("請以系統管理員身分重新執行此指令。");
            return false;
        }

        return true;
    }

    /// <summary>
    /// 存放區權限已被放寬時印出警告。僅在 <c>profile set</c> 與 <c>profile list</c> 執行；
    /// 備份與還原作業不檢查，以免每次備份都重複產生無人閱讀的警告。
    /// </summary>
    private static void WarnIfStoreDirectoryPermissionsAreUnexpected(
        IEnvironmentProbe environmentProbe,
        MachineScopedStoreLocation location)
    {
        if (IsStoreDirectorySecure(environmentProbe, location) == false)
        {
            WriteWarning(
                "命令列連線設定存放區的檔案權限似乎已被放寬，可能已不再限定於系統管理員與 SYSTEM。" +
                "請確認資料夾權限，避免同機的一般使用者取得存取權：" +
                location.Directory);
        }
    }

    private static void WriteWarning(string message) => ConsoleMessage.WriteWarning(message);

    private static void WriteError(string message) => ConsoleMessage.WriteError(message);
}
