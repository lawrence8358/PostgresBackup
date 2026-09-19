namespace PostgresBackup.Core.Exceptions;

/// <summary>
/// 密碼無法寫入或移除於作業系統憑證存儲時拋出。
/// 呼叫端須將此視為儲存失敗並向使用者明確呈現，不得視為成功。
/// </summary>
public class CredentialStorageException : Exception
{
    /// <summary>作業系統原生錯誤碼；若失敗原因非原生呼叫則為 0。</summary>
    public int NativeErrorCode { get; }

    public CredentialStorageException(string message, int nativeErrorCode = 0)
        : base(message)
    {
        NativeErrorCode = nativeErrorCode;
    }

    public CredentialStorageException(string message, Exception innerException, int nativeErrorCode = 0)
        : base(message, innerException)
    {
        NativeErrorCode = nativeErrorCode;
    }
}
