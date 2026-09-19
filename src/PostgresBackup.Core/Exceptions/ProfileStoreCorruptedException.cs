namespace PostgresBackup.Core.Exceptions;

/// <summary>
/// 連線設定檔存在但無法解析時拋出。
/// <para>
/// 與存取被拒同理：損毀不是「沒有任何設定」。默默回傳空清單除了診斷錯誤之外
/// 還有更糟的後果——下一次儲存會把損毀的檔案整份覆蓋掉，
/// 連人工救回來的機會都沒有。
/// </para>
/// </summary>
public class ProfileStoreCorruptedException : Exception
{
    public ProfileStoreCorruptedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
