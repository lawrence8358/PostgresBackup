using PostgresBackup.Core.Exceptions;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

/// <summary>
/// 機器範圍憑證存放區在真實檔案上的行為。
/// 機器範圍的資料保護 API 加解密不需要系統管理員權限，因此可在一般測試行程中往返；
/// 存放路徑注入至暫存目錄，不觸碰正式的機器層級目錄。
/// </summary>
public class MachineScopedCredentialStorageFileTests : IDisposable
{
    private readonly string _directory;
    private readonly string _filePath;

    public MachineScopedCredentialStorageFileTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"pg_test_creds_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
        _filePath = Path.Combine(_directory, "cli-credentials.dat");
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); } catch { }
        GC.SuppressFinalize(this);
    }

    private MachineScopedCredentialStorage CreateStorage()
        // 暫存路徑不得套用限制性權限：否則本行程會失去對自身暫存目錄的存取權。
        => new(MachineScopedStoreLocation.CreateUnrestrictedForTesting(_directory));

    [WindowsOnlyFact]
    public void SetPassword_ThenGetPassword_RoundTripsThroughTheFile()
    {
        CreateStorage().SetPassword("PostgresBackup:Cli:Profile:nightly", "SuperSecret!42");

        Assert.Equal("SuperSecret!42", CreateStorage().GetPassword("PostgresBackup:Cli:Profile:nightly"));
    }

    [WindowsOnlyFact]
    public void SetPassword_StoresCiphertextOnly()
    {
        CreateStorage().SetPassword("PostgresBackup:Cli:Profile:nightly", "SuperSecret!42");

        Assert.DoesNotContain("SuperSecret!42", File.ReadAllText(_filePath));
    }

    [WindowsOnlyFact]
    public void SetPassword_DoesNotLeaveTemporaryFilesBehind()
    {
        CreateStorage().SetPassword("PostgresBackup:Cli:Profile:nightly", "SuperSecret!42");

        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [WindowsOnlyFact]
    public void GetPassword_WhenFileIsTruncated_ReportsCorruptionInsteadOfMissingPassword()
    {
        CreateStorage().SetPassword("PostgresBackup:Cli:Profile:nightly", "SuperSecret!42");

        // 寫入途中斷電會留下這種截斷的內容。
        var truncated = File.ReadAllText(_filePath);
        File.WriteAllText(_filePath, truncated[..(truncated.Length / 2)]);

        Assert.Throws<CredentialStoreCorruptedException>(
            () => CreateStorage().GetPassword("PostgresBackup:Cli:Profile:nightly"));
    }

    [WindowsOnlyFact]
    public void SetPassword_WhenFileIsCorrupted_RefusesToOverwriteTheRemainingPasswords()
    {
        CreateStorage().SetPassword("PostgresBackup:Cli:Profile:nightly", "SuperSecret!42");

        var truncated = File.ReadAllText(_filePath);
        File.WriteAllText(_filePath, truncated[..(truncated.Length / 2)]);

        Assert.Throws<CredentialStoreCorruptedException>(
            () => CreateStorage().SetPassword("PostgresBackup:Cli:Profile:other", "AnotherSecret"));
    }
}
