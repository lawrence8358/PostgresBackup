using System.Text.Json;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 基於 JSON 檔案與安全憑證庫實作的連線設定檔倉儲
/// </summary>
public class JsonConnectionProfileRepository : IConnectionProfileRepository
{
    private readonly string _filePath;
    private readonly ICredentialStorage _credentialStorage;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private const string CredentialKeyPrefix = "PostgresBackup:Profile:";

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

            await SaveInternalAsync(list, ct);

            // 儲存密碼至安全憑證存儲
            if (password != null)
            {
                _credentialStorage.SetPassword(GetCredentialKey(profile.Id), password);
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
            var removed = list.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) > 0;

            if (removed)
            {
                await SaveInternalAsync(list, ct);
                _credentialStorage.DeletePassword(GetCredentialKey(id));
            }

            return removed;
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
        catch
        {
            return [];
        }
    }

    private async Task SaveInternalAsync(List<ConnectionProfile> items, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(items, JsonOptions);
        await File.WriteAllTextAsync(_filePath, json, ct);
    }
}
