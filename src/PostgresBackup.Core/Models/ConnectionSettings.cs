using Npgsql;

namespace PostgresBackup.Core.Models;

/// <summary>
/// PostgreSQL 連線設定模型。
/// </summary>
/// <remarks>
/// 此處刻意只以分開的欄位描述連線，沒有「整條連線字串」的欄位。
///
/// 曾經有一個 <c>ConnectionString</c> 欄位，且 <see cref="ToConnectionString"/> 會優先採用它，
/// 但備份與還原的參數只由 Host／Port／Username／Database 組成、完全不讀它。單次還原同時走
/// 這兩條路 —— 目標資料庫的檢查與清空經由前者、<c>pg_restore</c> 經由後者 —— 兩者若指向不同
/// 伺服器，一次還原會檢查並清空某一台，卻把資料寫進另一台。
///
/// 該欄位的全部用途是讓 <c>check-tools</c> 的 <c>-s</c> 繞進物件再繞出來，而它要餵的
/// <c>CheckCompatibilityAsync</c> 本來就只收一個字串。欄位移除後，上述狀態在型別上無法表示，
/// 兩個服務的執行期守門因而一併撤除。若日後真需要接受整條連線字串，正確做法是讓呼叫端直接
/// 持有那個字串，而不是把它藏進這個型別。
/// </remarks>
public class ConnectionSettings
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5432;
    public string Database { get; set; } = "postgres";
    public string Username { get; set; } = "postgres";
    public string? Password { get; set; }

    /// <summary>
    /// 依分開的欄位產生 Npgsql 連線字串。
    /// </summary>
    public string ToConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Port,
            Database = Database,
            Username = Username,
            Timeout = 10,
            CommandTimeout = 30
        };

        if (!string.IsNullOrEmpty(Password))
        {
            builder.Password = Password;
        }

        return builder.ConnectionString;
    }
}
