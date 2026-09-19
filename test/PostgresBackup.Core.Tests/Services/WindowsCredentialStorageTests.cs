using System.Runtime.InteropServices;
using PostgresBackup.Core.Exceptions;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

/// <summary>
/// 僅於 Windows 平台執行之測試；其他平台自動略過。
/// </summary>
public sealed class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Skip = "此測試需要 Windows 認證管理員。";
        }
    }
}

public class WindowsCredentialStorageTests
{
    /// <summary>
    /// Windows 認證管理員的憑證內容上限為 CRED_MAX_CREDENTIAL_BLOB_SIZE（2560 位元組），
    /// 超過即由 CredWriteW 以 ERROR_INVALID_PARAMETER 拒絕。此為無須系統管理員權限、
    /// 亦無須變更機器狀態即可穩定重現寫入失敗的途徑。
    /// </summary>
    private static string OversizedPassword => new('A', 4096);

    private static string UniqueKey() => $"PostgresBackup:Test:{Guid.NewGuid():N}";

    [Fact]
    public void SetPassword_WhenMemoryStorageForced_StoresAndRetrievesPassword()
    {
        var storage = new WindowsCredentialStorage(forceMemoryStorage: true);
        var key = UniqueKey();

        storage.SetPassword(key, "MemorySecret123");

        Assert.Equal("MemorySecret123", storage.GetPassword(key));
        Assert.True(storage.DeletePassword(key));
        Assert.Null(storage.GetPassword(key));
    }

    [Fact]
    public void SetPassword_WhenKeyIsEmpty_ThrowsArgumentException()
    {
        var storage = new WindowsCredentialStorage(forceMemoryStorage: true);

        Assert.Throws<ArgumentException>(() => storage.SetPassword(" ", "whatever"));
    }

    [WindowsOnlyFact]
    public void SetPassword_OnWindowsCredentialManager_CompletesWriteReadDeleteRoundTrip()
    {
        var storage = new WindowsCredentialStorage();
        var key = UniqueKey();

        try
        {
            storage.SetPassword(key, "RoundTripSecret!42");

            Assert.Equal("RoundTripSecret!42", storage.GetPassword(key));
        }
        finally
        {
            Assert.True(storage.DeletePassword(key));
        }

        Assert.Null(storage.GetPassword(key));
    }

    [WindowsOnlyFact]
    public void SetPassword_OnWindowsCredentialManager_OverwritesExistingPassword()
    {
        var storage = new WindowsCredentialStorage();
        var key = UniqueKey();

        try
        {
            storage.SetPassword(key, "FirstSecret");
            storage.SetPassword(key, "SecondSecret");

            Assert.Equal("SecondSecret", storage.GetPassword(key));
        }
        finally
        {
            storage.DeletePassword(key);
        }
    }

    [WindowsOnlyFact]
    public void SetPassword_WhenCredentialManagerWriteFails_ThrowsWithWin32ErrorCode()
    {
        var storage = new WindowsCredentialStorage();
        var key = UniqueKey();

        var ex = Assert.Throws<CredentialStorageException>(
            () => storage.SetPassword(key, OversizedPassword));

        Assert.NotEqual(0, ex.NativeErrorCode);
        Assert.Contains(ex.NativeErrorCode.ToString(), ex.Message);
    }

    [WindowsOnlyFact]
    public void SetPassword_WhenCredentialManagerWriteFails_DoesNotRetainPasswordInMemory()
    {
        var storage = new WindowsCredentialStorage();
        var key = UniqueKey();

        Assert.Throws<CredentialStorageException>(() => storage.SetPassword(key, OversizedPassword));

        Assert.Null(storage.GetPassword(key));
    }

    [WindowsOnlyFact]
    public void GetPassword_OnWindowsWithoutMemoryFlag_ReturnsNullForUnknownKey()
    {
        var storage = new WindowsCredentialStorage();

        Assert.Null(storage.GetPassword(UniqueKey()));
    }
}
