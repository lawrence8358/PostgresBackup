namespace PostgresBackup.Core.Services;

/// <summary>
/// 移除呈現給使用者或寫入記錄的文字中可能夾帶的密碼。
/// <para>
/// 資料庫驅動程式的例外訊息在連線字串格式異常時有可能回吐其內容，
/// 而連線字串含密碼。錯誤訊息不值得為此成為新的外洩管道——
/// 這是最後一道防線，不是主要防線：連線字串一律以
/// <c>NpgsqlConnectionStringBuilder</c> 組出，本身就會正確跳脫。
/// </para>
/// </summary>
public static class SensitiveText
{
    /// <summary>
    /// 將 <paramref name="message"/> 中出現的 <paramref name="password"/> 換成遮蔽符號。
    /// 兩者任一為空時原樣回傳。
    /// </summary>
    public static string Redact(string? message, string? password)
    {
        if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(password))
        {
            return message ?? string.Empty;
        }

        return message.Replace(password, "***", StringComparison.Ordinal);
    }
}
