namespace PostgresBackup.Core.Exceptions;

/// <summary>
/// 連線設定存放區存在，但目前的執行身分沒有權限讀取時拋出。
/// <para>
/// 命令列存放區刻意只有 Administrators 與 SYSTEM 可讀，因此「以一般使用者身分執行」
/// 是常態情境而非異常。若把存取被拒折疊成「沒有任何設定」或「找不到該設定」，
/// 使用者會被引導去重建一份其實已經存在的設定——那是錯誤的診斷，不是較溫和的診斷。
/// </para>
/// </summary>
public class ProfileStoreAccessDeniedException : Exception
{
    public ProfileStoreAccessDeniedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
