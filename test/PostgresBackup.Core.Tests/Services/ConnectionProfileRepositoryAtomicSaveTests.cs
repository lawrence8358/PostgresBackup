using PostgresBackup.Core.Exceptions;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

/// <summary>
/// 「儲存連線設定」必須是全有或全無的操作。
/// 密碼寫入失敗後若仍留下非機密欄位，使用者會在清單中看到一筆
/// 看起來存在、實際上不能用的設定，並據此以為設定已建立。
/// </summary>
public class ConnectionProfileRepositoryAtomicSaveTests : IDisposable
{
    private readonly string _testFile;

    public ConnectionProfileRepositoryAtomicSaveTests()
    {
        _testFile = Path.Combine(Path.GetTempPath(), $"pg_test_atomic_{Guid.NewGuid():N}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_testFile))
        {
            try { File.Delete(_testFile); } catch { }
        }
        GC.SuppressFinalize(this);
    }

    private static ConnectionProfile NewProfile(string id = "nightly") => new()
    {
        Id = id,
        Name = id,
        Host = "db.internal",
        Database = "billing",
        Username = "svc_backup"
    };

    [Fact]
    public async Task SaveProfileAsync_WhenPasswordWriteFails_DoesNotPersistProfile()
    {
        var credentials = new FakeCredentialStorage { FailOnSet = true };
        var repo = new JsonConnectionProfileRepository(credentials, _testFile);

        await Assert.ThrowsAsync<CredentialStorageException>(
            () => repo.SaveProfileAsync(NewProfile(), "SuperSecret!42"));

        Assert.Empty(await repo.GetAllProfilesAsync());

        var reloaded = new JsonConnectionProfileRepository(credentials, _testFile);
        Assert.Empty(await reloaded.GetAllProfilesAsync());
    }

    [Fact]
    public async Task SaveProfileAsync_WhenPasswordWriteFails_LeavesExistingProfileUntouched()
    {
        var credentials = new FakeCredentialStorage();
        var repo = new JsonConnectionProfileRepository(credentials, _testFile);

        var original = NewProfile();
        original.Host = "original-host";
        await repo.SaveProfileAsync(original, "OriginalPassword1");

        credentials.FailOnSet = true;
        var updated = NewProfile();
        updated.Host = "updated-host";

        await Assert.ThrowsAsync<CredentialStorageException>(
            () => repo.SaveProfileAsync(updated, "NewPassword2"));

        var stored = Assert.Single(await repo.GetAllProfilesAsync());
        Assert.Equal("original-host", stored.Host);
        Assert.Equal("OriginalPassword1", await repo.GetPasswordAsync("nightly"));
    }

    [Fact]
    public async Task SaveProfileAsync_WhenProfileFileWriteFails_DoesNotLeaveOrphanCredential()
    {
        var credentials = new FakeCredentialStorage();
        // 目錄位置被一個同名檔案佔住，非機密欄位必定寫入失敗。
        var blocked = Path.Combine(_testFile, "profiles.json");
        File.WriteAllText(_testFile, "not a directory");

        var repo = new JsonConnectionProfileRepository(credentials, blocked);

        await Assert.ThrowsAnyAsync<Exception>(
            () => repo.SaveProfileAsync(NewProfile(), "SuperSecret!42"));

        Assert.Empty(credentials.Entries);
    }

    [Fact]
    public async Task SaveProfileAsync_WhenProfileFileWriteFails_RestoresPreviousPassword()
    {
        var credentials = new FakeCredentialStorage();
        var repo = new JsonConnectionProfileRepository(credentials, _testFile);

        await repo.SaveProfileAsync(NewProfile(), "OriginalPassword1");

        credentials.FailOnSet = false;
        var failing = new JsonConnectionProfileRepository(
            credentials, Path.Combine(_testFile, "nested", "profiles.json"));

        await Assert.ThrowsAnyAsync<Exception>(
            () => failing.SaveProfileAsync(NewProfile(), "NewPassword2"));

        Assert.Equal("OriginalPassword1", await repo.GetPasswordAsync("nightly"));
    }

    [Fact]
    public async Task DeleteProfileAsync_ClearsThePasswordBeforeTheProfileFile()
    {
        var credentials = new FakeCredentialStorage();
        var repo = new JsonConnectionProfileRepository(credentials, _testFile);

        await repo.SaveProfileAsync(NewProfile(), "SuperSecret!42");
        Assert.NotEmpty(credentials.Entries);

        Assert.True(await repo.DeleteProfileAsync("nightly"));

        Assert.Empty(await repo.GetAllProfilesAsync());
        Assert.Empty(credentials.Entries);
    }

    [Fact]
    public async Task DeleteProfileAsync_WhenPasswordDeletionFails_DoesNotRemoveTheProfile()
    {
        // 刪除的意圖是「讓這組憑證消失」。密碼清不掉時就不能宣告成功、
        // 更不能把設定從清單中拿走——那會留下一份無主但仍可解開的密碼，
        // 而且從此沒有任何介面看得到它、能再刪一次。
        var credentials = new FakeCredentialStorage();
        var repo = new JsonConnectionProfileRepository(credentials, _testFile);

        await repo.SaveProfileAsync(NewProfile(), "SuperSecret!42");

        credentials.FailOnDelete = true;

        await Assert.ThrowsAsync<CredentialStoreCorruptedException>(
            () => repo.DeleteProfileAsync("nightly"));

        Assert.Single(await repo.GetAllProfilesAsync());
    }

    [Fact]
    public async Task GetAllProfilesAsync_WhenFileIsTruncated_ReportsCorruptionInsteadOfAnEmptyStore()
    {
        var credentials = new FakeCredentialStorage();
        var repo = new JsonConnectionProfileRepository(credentials, _testFile);

        await repo.SaveProfileAsync(NewProfile(), "SuperSecret!42");

        var truncated = await File.ReadAllTextAsync(_testFile);
        await File.WriteAllTextAsync(_testFile, truncated[..(truncated.Length / 2)]);

        await Assert.ThrowsAsync<ProfileStoreCorruptedException>(() => repo.GetAllProfilesAsync());
    }

    [Fact]
    public async Task SaveProfileAsync_WhenFileIsCorrupted_RefusesToOverwriteIt()
    {
        // 默默把損毀檔案當成空的，下一次儲存就會整份覆蓋掉，連人工救回來的機會都沒有。
        var credentials = new FakeCredentialStorage();
        var repo = new JsonConnectionProfileRepository(credentials, _testFile);

        await repo.SaveProfileAsync(NewProfile(), "SuperSecret!42");

        var truncated = (await File.ReadAllTextAsync(_testFile))[..20];
        await File.WriteAllTextAsync(_testFile, truncated);

        await Assert.ThrowsAsync<ProfileStoreCorruptedException>(
            () => repo.SaveProfileAsync(NewProfile("other"), "AnotherSecret"));

        Assert.Equal(truncated, await File.ReadAllTextAsync(_testFile));
    }

    private sealed class FakeCredentialStorage : ICredentialStorage
    {
        public Dictionary<string, string> Entries { get; } = new(StringComparer.Ordinal);

        public bool FailOnSet { get; set; }

        public void SetPassword(string key, string password)
        {
            if (FailOnSet)
                throw new CredentialStorageException("simulated credential write failure");

            Entries[key] = password;
        }

        public string? GetPassword(string key)
            => Entries.TryGetValue(key, out var value) ? value : null;

        public bool FailOnDelete { get; set; }

        public bool DeletePassword(string key)
        {
            if (FailOnDelete)
                throw new CredentialStoreCorruptedException("simulated credential store corruption");

            return Entries.Remove(key);
        }
    }
}
