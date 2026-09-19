using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

/// <summary>
/// 機器範圍憑證存放區的可測試部分。
/// 真實的 Windows 資料保護 API 加解密往返屬整合測試範圍，不簽入版控；
/// 此處以 <c>forceMemoryStorage</c> 驗證契約行為。
/// </summary>
public class MachineScopedCredentialStorageTests
{
    [Fact]
    public void SetPassword_ThenGetPassword_RoundTrips()
    {
        var storage = new MachineScopedCredentialStorage(forceMemoryStorage: true);

        storage.SetPassword("PostgresBackup:Cli:Profile:nightly", "SuperSecret!42");

        Assert.Equal("SuperSecret!42", storage.GetPassword("PostgresBackup:Cli:Profile:nightly"));
    }

    [Fact]
    public void GetPassword_WhenKeyUnknown_ReturnsNull()
    {
        var storage = new MachineScopedCredentialStorage(forceMemoryStorage: true);

        Assert.Null(storage.GetPassword("PostgresBackup:Cli:Profile:missing"));
    }

    [Fact]
    public void SetPassword_WithEmptyKey_Throws()
    {
        var storage = new MachineScopedCredentialStorage(forceMemoryStorage: true);

        Assert.Throws<ArgumentException>(() => storage.SetPassword("  ", "SuperSecret!42"));
    }

    [Fact]
    public void DeletePassword_RemovesEntry()
    {
        var storage = new MachineScopedCredentialStorage(forceMemoryStorage: true);
        storage.SetPassword("PostgresBackup:Cli:Profile:nightly", "SuperSecret!42");

        Assert.True(storage.DeletePassword("PostgresBackup:Cli:Profile:nightly"));
        Assert.Null(storage.GetPassword("PostgresBackup:Cli:Profile:nightly"));
    }

    [Fact]
    public void DeletePassword_WhenKeyUnknown_ReturnsFalse()
    {
        var storage = new MachineScopedCredentialStorage(forceMemoryStorage: true);

        Assert.False(storage.DeletePassword("PostgresBackup:Cli:Profile:missing"));
    }
}
