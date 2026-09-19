namespace PostgresBackup.Core.Exceptions;

/// <summary>
/// 加密密碼存放檔案存在但無法解析時拋出。
/// <para>
/// 整份憑證存放於單一檔案，若寫入途中斷電會留下被截斷的內容。
/// 此時若默默當成空的存放區，所有排程密碼會同時且無聲地變成「遺失」，
/// 使用者只會看到一連串莫名其妙的排程失敗，而看不到真正的原因。
/// </para>
/// </summary>
public class CredentialStoreCorruptedException : Exception
{
    public CredentialStoreCorruptedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
