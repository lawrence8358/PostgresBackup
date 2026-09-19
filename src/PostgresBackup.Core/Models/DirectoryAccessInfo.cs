namespace PostgresBackup.Core.Models;

/// <summary>
/// 目錄的存取權限查詢結果。
/// <see cref="AllowedIdentities"/> 以安全識別碼（SID）字串呈現，
/// 使呼叫端的判斷不受作業系統語系影響。
/// </summary>
public class DirectoryAccessInfo
{
    /// <summary>被查詢的目錄路徑。</summary>
    public required string Path { get; init; }

    /// <summary>目錄是否存在。</summary>
    public bool Exists { get; init; }

    /// <summary>是否仍自父目錄繼承存取規則。限制性存放區應為 <c>false</c>。</summary>
    public bool InheritanceEnabled { get; init; }

    /// <summary>具有允許存取規則的身分之 SID 字串清單。</summary>
    public IReadOnlyList<string> AllowedIdentities { get; init; } = [];
}
