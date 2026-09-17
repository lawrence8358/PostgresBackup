using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

public class ConnectionProfileRepositoryTests : IDisposable
{
    private readonly string _testFile;
    private readonly WindowsCredentialStorage _credStorage;
    private readonly JsonConnectionProfileRepository _repo;

    public ConnectionProfileRepositoryTests()
    {
        _testFile = Path.Combine(Path.GetTempPath(), $"pg_test_conn_{Guid.NewGuid():N}.json");
        _credStorage = new WindowsCredentialStorage(forceMemoryStorage: true);
        _repo = new JsonConnectionProfileRepository(_credStorage, _testFile);
    }

    public void Dispose()
    {
        if (File.Exists(_testFile))
        {
            try { File.Delete(_testFile); } catch { }
        }
    }

    [Fact]
    public async Task GetAllProfilesAsync_WhenFileDoesNotExist_ReturnsEmptyList()
    {
        var profiles = await _repo.GetAllProfilesAsync();
        Assert.Empty(profiles);
    }

    [Fact]
    public async Task SaveProfileAsync_SavesMetadataAndProtectsPassword()
    {
        var profile = new ConnectionProfile
        {
            Id = "test-1",
            Name = "開發伺服器",
            Host = "192.168.1.100",
            Port = 5433,
            Database = "mydb",
            Username = "admin"
        };

        await _repo.SaveProfileAsync(profile, "SecretPassword123");

        var retrieved = await _repo.GetProfileByIdAsync("test-1");
        Assert.NotNull(retrieved);
        Assert.Equal("開發伺服器", retrieved.Name);
        Assert.Equal("192.168.1.100", retrieved.Host);
        Assert.Equal(5433, retrieved.Port);

        // 驗證密碼存於憑證存儲中，且檔案中不包含明文密碼
        var password = await _repo.GetPasswordAsync("test-1");
        Assert.Equal("SecretPassword123", password);

        var fileContent = await File.ReadAllTextAsync(_testFile);
        Assert.DoesNotContain("SecretPassword123", fileContent);
    }

    [Fact]
    public async Task DeleteProfileAsync_RemovesProfileAndCredential()
    {
        var profile = new ConnectionProfile { Id = "test-2", Name = "待刪除連線" };
        await _repo.SaveProfileAsync(profile, "TempPass");

        var deleted = await _repo.DeleteProfileAsync("test-2");
        Assert.True(deleted);

        var list = await _repo.GetAllProfilesAsync();
        Assert.Empty(list);

        var password = await _repo.GetPasswordAsync("test-2");
        Assert.Null(password);
    }
}
