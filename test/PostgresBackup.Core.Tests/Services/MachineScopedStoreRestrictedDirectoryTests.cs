using System.Runtime.InteropServices;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

/// <summary>
/// 釘住一個真實漏洞的修正：存放目錄<b>即使早已存在</b>也必須被套用限制性權限。
/// <para>
/// 機器層級的共用應用程式資料目錄（ProgramData）預設允許一般使用者建立子資料夾，
/// 因此低權限使用者可以搶先建立存放目錄，使其保留繼承而來的寬鬆權限。
/// 若程式只在「自己建立目錄時」才收緊權限，日後系統管理員寫入的加密密碼
/// 就會落在同機一般使用者讀得到的資料夾裡。
/// 機器範圍加密擋不住這件事（額外熵值是原始碼中的公開常數），檔案權限才是那道防線。
/// </para>
/// </summary>
public class MachineScopedStoreRestrictedDirectoryTests
{
    [RestrictiveAclFact]
    public void EnsureRestrictedDirectory_WhenDirectoryIsNew_IsCreatedAlreadyRestricted()
    {
        // 建立與套用權限若分成兩步，兩者之間會有一段目錄帶著繼承權限的空窗。
        // 此測試無法直接觀測那個瞬間，但可以釘住結果：目錄建立完成後不得有任何
        // 非預期身分的存取規則，且繼承必須已經停用。
        var directory = Path.Combine(Path.GetTempPath(), $"pg_test_new_store_{Guid.NewGuid():N}");

        try
        {
            Assert.False(Directory.Exists(directory), "前置條件不成立：目錄應尚未存在。");

            MachineScopedStore.EnsureRestrictedDirectory(directory, applyRestrictivePermissions: true);

            var info = new WindowsEnvironmentProbe().GetDirectoryAccessInfo(directory);
            Assert.NotNull(info);
            Assert.True(info.Exists);
            Assert.False(info.InheritanceEnabled,
                "新建目錄的繼承未被停用：它仍保有父目錄的寬鬆權限。");
            Assert.NotEmpty(info.AllowedIdentities);
            Assert.All(info.AllowedIdentities, sid => Assert.Contains(
                sid,
                new[] { MachineScopedStore.AdministratorsSid, MachineScopedStore.LocalSystemSid },
                StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    [RestrictiveAclFact]
    public void EnsureRestrictedDirectory_WhenDirectoryAlreadyExists_StillAppliesRestrictivePermissions()
    {
        // 此目錄收緊後本行程可能即失去存取權，因此刻意獨立、且事後不再讀寫。
        var directory = Path.Combine(Path.GetTempPath(), $"pg_test_squatted_store_{Guid.NewGuid():N}");

        // 模擬「搶先建立」：目錄先以繼承而來的預設權限存在。
        Directory.CreateDirectory(directory);

        try
        {
            var probe = new WindowsEnvironmentProbe();

            var before = probe.GetDirectoryAccessInfo(directory);
            Assert.NotNull(before);
            Assert.True(before.Exists);
            Assert.True(before.InheritanceEnabled,
                "前置條件不成立：搶先建立的目錄本應仍繼承父目錄的寬鬆權限。");

            MachineScopedStore.EnsureRestrictedDirectory(directory, applyRestrictivePermissions: true);

            var after = probe.GetDirectoryAccessInfo(directory);
            Assert.NotNull(after);

            // 繼承必須被停用，否則父目錄的寬鬆權限仍然有效。
            Assert.False(after.InheritanceEnabled,
                "既有目錄的繼承未被停用：搶先建立的目錄仍保有父目錄的寬鬆權限。");

            // 允許存取的身分只剩 Administrators 與 SYSTEM。
            Assert.NotEmpty(after.AllowedIdentities);
            Assert.Contains(MachineScopedStore.AdministratorsSid, after.AllowedIdentities);
            Assert.Contains(MachineScopedStore.LocalSystemSid, after.AllowedIdentities);
            Assert.All(after.AllowedIdentities, sid => Assert.Contains(
                sid,
                new[] { MachineScopedStore.AdministratorsSid, MachineScopedStore.LocalSystemSid },
                StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            // 權限收緊後本行程可能已無權刪除，刪除失敗屬預期。
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }
}

/// <summary>
/// 僅在「此行程確實能變更自建目錄的存取控制清單」時才執行的測試。
/// <para>
/// <c>ApplyRestrictiveAcl</c> 會吞掉 <see cref="UnauthorizedAccessException"/>，
/// 因此在無權變更 DACL 的環境下沒有任何可供斷言的結果——
/// 此時必須略過，而不是讓斷言在毫無驗證力的情況下通過。
/// xUnit v2 沒有 <c>Assert.Skip</c>，故以探索期評估的條件式 Skip 達成同樣效果。
/// </para>
/// </summary>
public sealed class RestrictiveAclFactAttribute : FactAttribute
{
    public RestrictiveAclFactAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Skip = "限制性檔案權限僅適用於 Windows。";
            return;
        }

        if (!CanApplyRestrictiveAcl())
        {
            Skip = "此行程無權變更自建目錄的存取控制清單，無法驗證權限收緊。";
        }
    }

    private static bool CanApplyRestrictiveAcl()
    {
        var probeDirectory = Path.Combine(Path.GetTempPath(), $"pg_test_acl_probe_{Guid.NewGuid():N}");

        try
        {
            MachineScopedStore.EnsureRestrictedDirectory(probeDirectory, applyRestrictivePermissions: true);
            var info = new WindowsEnvironmentProbe().GetDirectoryAccessInfo(probeDirectory);
            return info is { Exists: true, InheritanceEnabled: false };
        }
        catch
        {
            return false;
        }
        finally
        {
            try { Directory.Delete(probeDirectory, recursive: true); } catch { }
        }
    }
}
