namespace PostgresBackup.Core.Interfaces;

/// <summary>
/// 憑證安全存儲接縫介面，隔離作業系統原生憑證庫
/// </summary>
public interface ICredentialStorage
{
    /// <summary>
    /// 安全儲存指定識別鍵的密碼。
    /// 儲存失敗時必須拋出 <see cref="Exceptions.CredentialStorageException"/>，
    /// 不得以任何形式靜默保留明文密碼後回報成功。
    /// </summary>
    /// <exception cref="Exceptions.CredentialStorageException">密碼無法寫入憑證存儲。</exception>
    void SetPassword(string key, string password);

    /// <summary>
    /// 取得指定識別鍵的密碼，若不存在則回傳 null
    /// </summary>
    string? GetPassword(string key);

    /// <summary>
    /// 刪除指定識別鍵的密碼
    /// </summary>
    bool DeletePassword(string key);
}
