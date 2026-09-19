using PostgresBackup.Core.Exceptions;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

namespace PostgresBackup.Cli.Commands;

/// <summary>
/// 讀取命令列連線設定存放區時的共用錯誤處理。
/// <para>
/// 此存放區刻意只有 Administrators 與 SYSTEM 可讀，因此「以一般使用者身分執行」
/// 是常態情境。所有讀取點都必須把「權限不足」與「存放區損毀」說清楚，
/// 不得折疊成「沒有任何設定」或「找不到該設定」——那會把人引去重建
/// 一份其實已經存在的設定，或誤以為密碼從未設定過。
/// </para>
/// </summary>
internal static class ProfileStoreAccess
{
    /// <summary>
    /// 讀取所有連線設定；存取被拒或存放區損毀時印出明確錯誤並回傳 <c>null</c>，
    /// 由呼叫端以非零退出碼結束。
    /// </summary>
    public static async Task<IReadOnlyList<ConnectionProfile>?> TryLoadAllAsync(
        IConnectionProfileRepository repo,
        CancellationToken ct = default)
    {
        try
        {
            return await repo.GetAllProfilesAsync(ct);
        }
        catch (Exception ex) when (IsStoreAccessProblem(ex))
        {
            ConsoleMessage.WriteError(ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 讀取指定連線設定的密碼。<paramref name="password"/> 為 <c>null</c> 時代表
    /// 這筆設定確實沒有密碼；回傳 <c>false</c> 則代表存放區讀不到（錯誤已印出）。
    /// </summary>
    public static async Task<(bool Ok, string? Password)> TryGetPasswordAsync(
        IConnectionProfileRepository repo,
        string profileId,
        CancellationToken ct = default)
    {
        try
        {
            return (true, await repo.GetPasswordAsync(profileId, ct));
        }
        catch (Exception ex) when (IsStoreAccessProblem(ex))
        {
            ConsoleMessage.WriteError(ex.Message);
            return (false, null);
        }
    }

    /// <summary>
    /// 密碼狀態為「遺失」時提醒使用者。若靜默帶著 null 密碼去連線，
    /// 使用者只會看到驅動程式的驗證失敗訊息，看不出真正的原因是密碼不在存放區裡。
    /// </summary>
    public static void WarnIfPasswordMissing(string profileName, string? password)
    {
        if (!string.IsNullOrEmpty(password))
        {
            return;
        }

        ConsoleMessage.WriteWarning(
            $"連線設定 '{profileName}' 的密碼狀態為「遺失」，存放區中找不到它的密碼。" +
            $"接下來的連線很可能會以驗證失敗收場。" +
            $"請執行 pgbackup profile set --name {profileName} 重新設定密碼。");
    }

    private static bool IsStoreAccessProblem(Exception ex)
        => ex is ProfileStoreAccessDeniedException
               or ProfileStoreCorruptedException
               or CredentialStoreCorruptedException;
}
