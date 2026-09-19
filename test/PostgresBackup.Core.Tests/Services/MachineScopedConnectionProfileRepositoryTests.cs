using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

/// <summary>
/// 命令列連線設定（機器範圍存放區）的非加密部分測試。
/// 加密本身（Windows 資料保護 API 機器範圍）與真實檔案權限屬整合測試範圍，
/// 此處以 <c>forceMemoryStorage</c> 繞過，專注驗證持久化內容的不變條件。
/// </summary>
public class MachineScopedConnectionProfileRepositoryTests : IDisposable
{
    private readonly MachineScopedStoreLocation _location;
    private readonly string _testFile;
    private readonly MachineScopedCredentialStorage _credStorage;
    private readonly MachineScopedConnectionProfileRepository _repo;

    public MachineScopedConnectionProfileRepositoryTests()
    {
        // 暫存路徑不得套用限制性權限：否則本行程會失去對自身暫存目錄的存取權。
        _location = MachineScopedStoreLocation.CreateUnrestrictedForTesting(
            Path.Combine(Path.GetTempPath(), $"pg_test_cli_store_{Guid.NewGuid():N}"));
        _testFile = _location.ProfilesFilePath;
        _credStorage = new MachineScopedCredentialStorage(_location, forceMemoryStorage: true);
        _repo = new MachineScopedConnectionProfileRepository(_credStorage, _location);
    }

    public void Dispose()
    {
        try { Directory.Delete(_location.Directory, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetAllProfilesAsync_WhenFileDoesNotExist_ReturnsEmptyList()
    {
        var profiles = await _repo.GetAllProfilesAsync();
        Assert.Empty(profiles);
    }

    [Fact]
    public async Task SaveProfileAsync_PersistsNonSecretFields()
    {
        var profile = new ConnectionProfile
        {
            Id = "nightly",
            Name = "nightly",
            Host = "db.internal",
            Port = 5433,
            Database = "billing",
            Username = "svc_backup"
        };

        await _repo.SaveProfileAsync(profile, "SuperSecret!42");

        var reloaded = new MachineScopedConnectionProfileRepository(_credStorage, _location);
        var stored = await reloaded.GetProfileByIdAsync("nightly");

        Assert.NotNull(stored);
        Assert.Equal("nightly", stored.Name);
        Assert.Equal("db.internal", stored.Host);
        Assert.Equal(5433, stored.Port);
        Assert.Equal("billing", stored.Database);
        Assert.Equal("svc_backup", stored.Username);
        Assert.NotEqual(default, stored.CreatedAt);
    }

    [Fact]
    public async Task SaveProfileAsync_PersistedFileDoesNotContainPassword()
    {
        var profile = new ConnectionProfile
        {
            Id = "nightly",
            Name = "nightly",
            Host = "db.internal",
            Database = "billing",
            Username = "svc_backup"
        };

        await _repo.SaveProfileAsync(profile, "SuperSecret!42");

        var fileContent = await File.ReadAllTextAsync(_testFile);
        Assert.DoesNotContain("SuperSecret!42", fileContent);
    }

    [Fact]
    public async Task SaveProfileAsync_WithExistingId_UpdatesInsteadOfAppending()
    {
        await _repo.SaveProfileAsync(new ConnectionProfile
        {
            Id = "nightly",
            Name = "nightly",
            Host = "old-host",
            Database = "billing",
            Username = "old_user"
        }, "OldPassword1");

        await _repo.SaveProfileAsync(new ConnectionProfile
        {
            Id = "nightly",
            Name = "nightly",
            Host = "new-host",
            Database = "billing",
            Username = "new_user"
        }, "NewPassword2");

        var all = await _repo.GetAllProfilesAsync();
        var single = Assert.Single(all);
        Assert.Equal("new-host", single.Host);
        Assert.Equal("new_user", single.Username);
        Assert.Equal("NewPassword2", await _repo.GetPasswordAsync("nightly"));
    }

    [Fact]
    public async Task GetPasswordAsync_ReturnsStoredPassword()
    {
        await _repo.SaveProfileAsync(new ConnectionProfile { Id = "nightly", Name = "nightly" }, "SuperSecret!42");

        Assert.Equal("SuperSecret!42", await _repo.GetPasswordAsync("nightly"));
    }

    [Fact]
    public async Task DeleteProfileAsync_RemovesProfileAndItsPassword()
    {
        await _repo.SaveProfileAsync(new ConnectionProfile { Id = "nightly", Name = "nightly" }, "SuperSecret!42");

        Assert.True(await _repo.DeleteProfileAsync("nightly"));
        Assert.Empty(await _repo.GetAllProfilesAsync());
        Assert.Null(await _repo.GetPasswordAsync("nightly"));
    }

    [Fact]
    public async Task MachineScopedStore_DoesNotShareDataWithGuiStore()
    {
        var guiFile = Path.Combine(Path.GetTempPath(), $"pg_test_gui_conn_{Guid.NewGuid():N}.json");
        try
        {
            var guiRepo = new JsonConnectionProfileRepository(
                new WindowsCredentialStorage(forceMemoryStorage: true), guiFile);

            await _repo.SaveProfileAsync(new ConnectionProfile { Id = "cli-only", Name = "cli-only" }, "CliPassword");

            Assert.Empty(await guiRepo.GetAllProfilesAsync());
        }
        finally
        {
            if (File.Exists(guiFile))
            {
                try { File.Delete(guiFile); } catch { }
            }
        }
    }
}
