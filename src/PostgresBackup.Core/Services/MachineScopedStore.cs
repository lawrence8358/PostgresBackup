using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 命令列連線設定（機器範圍存放區）的共用路徑與檔案權限規則。
/// 存放位置刻意位於機器層級的共用應用程式資料目錄，否則以 SYSTEM 身分執行的排程任務讀不到；
/// 檔名亦刻意與介面連線設定的 connections.json 不同，以避免混淆。
/// </summary>
public static class MachineScopedStore
{
    /// <summary>Administrators 群組的知名 SID。</summary>
    public const string AdministratorsSid = "S-1-5-32-544";

    /// <summary>LocalSystem（SYSTEM）的知名 SID。</summary>
    public const string LocalSystemSid = "S-1-5-18";

    /// <summary>命令列連線設定非機密欄位的檔名。</summary>
    public const string ProfilesFileName = "cli-connection-profiles.json";

    /// <summary>命令列連線設定加密密碼的檔名。刻意與介面連線設定的檔名不同，以避免混淆。</summary>
    public const string CredentialsFileName = "cli-credentials.dat";

    /// <summary>機器層級共用應用程式資料目錄下的存放目錄。</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "PostgresBackup");

    /// <summary>
    /// 建立存放目錄；於 Windows 上同時套用限制性權限
    /// （停用繼承，僅 Administrators 與 SYSTEM 可完全控制）。
    /// </summary>
    public static void EnsureRestrictedDirectory(string directory, bool applyRestrictivePermissions = true)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;

        // 套用限制性權限會讓非系統管理員的行程失去對該目錄的存取權，
        // 因此測試注入自有暫存路徑時會關閉此行為；正式路徑一律開啟。
        if (!applyRestrictivePermissions || !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return;
        }

        if (!Directory.Exists(directory))
        {
            // 建立與套用權限必須是同一個動作。若先 CreateDirectory 再 SetAccessControl，
            // 兩者之間存在一段目錄帶著繼承而來的寬鬆權限的空窗，搶先建立的對手
            // 仍有機會在那一瞬間放進東西——這正是本修正要堵的漏洞，不該自己留一個。
            CreateDirectoryWithRestrictiveAcl(directory);
            return;
        }

        // 既有目錄亦一律套用。機器層級的共用目錄（ProgramData）預設允許一般使用者
        // 建立子資料夾，因此低權限使用者可以「搶先建立」此目錄，使其保留繼承而來的
        // 寬鬆權限；若此時略過套用，之後寫入的加密密碼就會落在同機一般使用者
        // 讀得到的位置。加密本身擋不住這種情形，檔案權限才是那道防線。
        //
        // 注意收緊不等於安全：目錄擁有者隱含具備變更權限的能力，因此
        // 「目錄已存在但權限不符」在 profile set 是被拒絕的，不是被靜默接手的。
        ApplyRestrictiveAcl(directory);
    }

    /// <summary>
    /// 以限制性存取控制清單「建立」目錄：權限在目錄誕生的同一刻就位，
    /// 不存在任何一段它帶著繼承權限的空窗。
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static void CreateDirectoryWithRestrictiveAcl(string directory)
    {
        try
        {
            new DirectoryInfo(directory).Create(BuildRestrictiveSecurity());
        }
        catch (UnauthorizedAccessException)
        {
            // 非系統管理員身分無法設定權限；目錄仍須建立，
            // 權限狀態交由存放區權限檢查向使用者呈現。
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
        catch (PlatformNotSupportedException)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
    }

    /// <summary>停用繼承，且僅 Administrators 與 SYSTEM 可完全控制。</summary>
    [SupportedOSPlatform("windows")]
    private static DirectorySecurity BuildRestrictiveSecurity()
    {
        var security = new DirectorySecurity();

        // 停用繼承並丟棄自父目錄繼承而來的規則，避免一般使用者經由繼承取得存取權。
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        foreach (var sid in new[] { AdministratorsSid, LocalSystemSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(sid),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }

        return security;
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyRestrictiveAcl(string directory)
    {
        try
        {
            new DirectoryInfo(directory).SetAccessControl(BuildRestrictiveSecurity());
        }
        catch (UnauthorizedAccessException)
        {
            // 非系統管理員身分無法設定權限；目錄仍會建立，
            // 權限狀態交由票 06 的存放區權限檢查向使用者呈現。
        }
        catch (PlatformNotSupportedException)
        {
        }
    }
}
