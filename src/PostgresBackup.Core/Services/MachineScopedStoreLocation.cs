namespace PostgresBackup.Core.Services;

/// <summary>
/// 命令列連線設定存放區的實際位置與權限政策。
/// 以相依注入提供，讓「檢查權限的目錄」與「實際寫入的目錄」始終是同一個——
/// 若由呼叫端各自硬編預設路徑，日後一旦允許覆寫存放路徑，
/// 權限檢查會靜默地檢查錯誤的目錄。
/// </summary>
/// <param name="Directory">存放區目錄。</param>
/// <param name="EnforceRestrictivePermissions">
/// 是否要求並套用限制性檔案權限（僅 Administrators 與 SYSTEM）。
/// 刻意不給預設值：這是安全決策，每個建立位置的地方都必須明說，
/// 不該有人因為少傳一個參數就意外放寬了存放區。
/// </param>
public sealed record MachineScopedStoreLocation(
    string Directory,
    bool EnforceRestrictivePermissions)
{
    /// <summary>正式環境使用的預設位置。</summary>
    public static MachineScopedStoreLocation Default { get; } =
        new(MachineScopedStore.DefaultDirectory, EnforceRestrictivePermissions: true);

    /// <summary>此位置的非機密欄位檔案路徑。</summary>
    public string ProfilesFilePath => Path.Combine(Directory, MachineScopedStore.ProfilesFileName);

    /// <summary>此位置的加密密碼檔案路徑。</summary>
    public string CredentialsFilePath => Path.Combine(Directory, MachineScopedStore.CredentialsFileName);

    /// <summary>
    /// 建立一個不套用限制性權限的暫存位置，供測試使用。
    /// 暫存目錄一旦被收緊，非系統管理員的測試行程就會失去對它的存取權。
    /// </summary>
    public static MachineScopedStoreLocation CreateUnrestrictedForTesting(string directory)
        => new(directory, EnforceRestrictivePermissions: false);
}
