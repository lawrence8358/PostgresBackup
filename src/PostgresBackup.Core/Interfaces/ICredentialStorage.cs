namespace PostgresBackup.Core.Interfaces;

/// <summary>
/// 憑證安全存儲接縫介面，隔離作業系統原生憑證庫
/// </summary>
public interface ICredentialStorage
{
    /// <summary>
    /// 安全儲存指定識別鍵的密碼
    /// </summary>
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
