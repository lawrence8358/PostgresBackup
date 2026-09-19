using System.Text.Json;
using PostgresBackup.Core.Exceptions;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Core.Resources;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 基於 JSON 檔案與安全憑證庫實作的連線設定檔倉儲
/// </summary>
public class JsonConnectionProfileRepository : IConnectionProfileRepository
{
    private readonly string _filePath;
    private readonly ICredentialStorage _credentialStorage;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// 憑證識別鍵前綴。衍生的存放區須使用不同前綴，
    /// 以確保兩套連線設定的密碼在憑證存放區中亦不相交。
    /// </summary>
    protected virtual string CredentialKeyPrefix => "PostgresBackup:Profile:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public JsonConnectionProfileRepository(
        ICredentialStorage credentialStorage,
        string? filePath = null)
    {
        _credentialStorage = credentialStorage;

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            _filePath = filePath;
        }
        else
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "PostgresBackup");
            _filePath = Path.Combine(dir, "connections.json");
        }
    }

    public async Task<IReadOnlyList<ConnectionProfile>> GetAllProfilesAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await LoadInternalAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ConnectionProfile?> GetProfileByIdAsync(string id, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var list = await LoadInternalAsync(ct);
            return list.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveProfileAsync(ConnectionProfile profile, string? password = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await _lock.WaitAsync(ct);
        try
        {
            var list = await LoadInternalAsync(ct);
            var existingIndex = list.FindIndex(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                list[existingIndex] = profile;
            }
            else
            {
                list.Add(profile);
            }

            // 密碼先寫、非機密欄位後寫。JSON 檔是一筆設定「存在」的唯一依據，
            // 因此必須最後才落地：密碼寫入失敗時直接往外拋，磁碟上不會留下
            // 一筆看起來存在、實際上不能用的孤兒設定。
            var credentialKey = GetCredentialKey(profile.Id);
            string? previousPassword = null;
            var credentialWritten = false;

            if (password != null)
            {
                previousPassword = TryGetPassword(credentialKey);
                _credentialStorage.SetPassword(credentialKey, password);
                credentialWritten = true;
            }

            try
            {
                await SaveInternalAsync(list, ct);
            }
            catch
            {
                // 反向的同一問題：密碼寫成功但設定沒寫成，會留下一筆無主的憑證。
                if (credentialWritten)
                {
                    RollBackCredential(credentialKey, previousPassword);
                }

                throw;
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> DeleteProfileAsync(string id, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        await _lock.WaitAsync(ct);
        try
        {
            var list = await LoadInternalAsync(ct);

            if (!list.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            // 先清密碼、後寫設定檔——與儲存時同一個道理，只是理由相反。
            // 刪除的意圖是「讓這組憑證消失」，而設定檔是這筆設定唯一看得見的入口：
            // 若先移除設定、清密碼才失敗，磁碟上會留下一份無主但仍可解開的密碼，
            // 而且從此沒有任何介面看得到它、能再刪一次。
            // 反過來失敗，留下的只是一筆密碼狀態為「遺失」的設定——看得見，也重建得回來。
            _credentialStorage.DeletePassword(GetCredentialKey(id));

            list.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            await SaveInternalAsync(list, ct);

            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<string?> GetPasswordAsync(string profileId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return Task.FromResult<string?>(null);

        var password = _credentialStorage.GetPassword(GetCredentialKey(profileId));
        return Task.FromResult(password);
    }

    private string GetCredentialKey(string profileId) => $"{CredentialKeyPrefix}{profileId}";

    /// <summary>
    /// 取得回滾用的原密碼。讀不到（例如權限不足或存放區損毀）時視為沒有原值——
    /// 回滾只需要「不留下這次新寫入的密碼」，不值得為了取原值而讓儲存流程失敗。
    /// </summary>
    private string? TryGetPassword(string credentialKey)
    {
        try
        {
            return _credentialStorage.GetPassword(credentialKey);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 還原密碼至寫入前的狀態。回滾本身失敗不得掩蓋原始的儲存失敗，
    /// 呼叫端看到的必須是「為什麼存不起來」那個例外。
    /// </summary>
    private void RollBackCredential(string credentialKey, string? previousPassword)
    {
        try
        {
            if (previousPassword != null)
            {
                _credentialStorage.SetPassword(credentialKey, previousPassword);
            }
            else
            {
                _credentialStorage.DeletePassword(credentialKey);
            }
        }
        catch
        {
            // 已在處理另一個例外，此處無可補救之處。
        }
    }

    private async Task<List<ConnectionProfile>> LoadInternalAsync(CancellationToken ct)
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct);
            var items = JsonSerializer.Deserialize<List<ConnectionProfile>>(json, JsonOptions);
            return items ?? [];
        }
        catch (UnauthorizedAccessException ex)
        {
            // 存取被拒不得折疊成「沒有任何設定」：那會讓使用者看到
            // 「目前沒有任何連線設定」，並據此重建一份其實已經存在的設定。
            // 讀不到與沒有是兩回事，只有後者才是空清單。
            throw new ProfileStoreAccessDeniedException(
                CoreStrings.Format("ProfileStore_Error_AccessDenied", _filePath), ex);
        }
        catch (JsonException ex)
        {
            // 同理，檔案損毀也不是「沒有任何設定」。默默回傳空清單還有更糟的後果：
            // 下一次儲存會把損毀的檔案整份覆蓋掉，連人工救回來的機會都沒有。
            throw new ProfileStoreCorruptedException(
                CoreStrings.Format("ProfileStore_Error_FileCorrupted", _filePath, ex.Message), ex);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// 確保存放目錄存在。衍生的存放區可於此套用限制性檔案權限。
    /// </summary>
    protected virtual void EnsureStorageDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private async Task SaveInternalAsync(List<ConnectionProfile> items, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            EnsureStorageDirectory(dir);
        }

        var json = JsonSerializer.Serialize(items, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json, ct);
    }
}
