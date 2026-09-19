using PostgresBackup.Core.Interfaces;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 命令列連線設定的存放區：非機密欄位以明文 JSON 存於機器層級的共用應用程式資料目錄，
/// 密碼另交由機器範圍加密的憑證存放區保管。
/// 與介面連線設定（使用者範圍）為兩組彼此獨立的實作，
/// 獨立性由相依注入決定——命令列與圖形介面各自註冊一組，執行期看不到對方的資料。
/// </summary>
public class MachineScopedConnectionProfileRepository : JsonConnectionProfileRepository
{
    private readonly MachineScopedStoreLocation _location;

    /// <param name="location">
    /// 存放區的位置與權限政策。兩者以單一物件成對傳入，
    /// 讓「寫到哪裡」與「那裡該套用什麼權限」不可能各說各話。
    /// 省略時使用正式環境的預設位置。
    /// </param>
    public MachineScopedConnectionProfileRepository(
        ICredentialStorage credentialStorage,
        MachineScopedStoreLocation? location = null)
        : base(credentialStorage, (location ?? MachineScopedStoreLocation.Default).ProfilesFilePath)
    {
        _location = location ?? MachineScopedStoreLocation.Default;
    }

    /// <summary>
    /// 憑證識別鍵前綴刻意與介面連線設定不同，確保兩套設定的密碼亦不相交。
    /// </summary>
    protected override string CredentialKeyPrefix => "PostgresBackup:Cli:Profile:";

    /// <summary>
    /// 存放目錄依位置的權限政策套用限制性檔案權限
    /// （停用繼承，僅 Administrators 與 SYSTEM 可完全控制）。
    /// </summary>
    protected override void EnsureStorageDirectory(string directory)
        => MachineScopedStore.EnsureRestrictedDirectory(directory, _location.EnforceRestrictivePermissions);
}
