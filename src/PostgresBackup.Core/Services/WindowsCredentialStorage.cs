using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using PostgresBackup.Core.Exceptions;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Resources;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 使用 Windows 認證管理員實作憑證安全存儲。
/// 寫入失敗一律拋出 <see cref="CredentialStorageException"/>，絕不靜默保留明文於行程記憶體；
/// 記憶體儲存路徑僅供非 Windows 平台，以及由 <c>forceMemoryStorage</c> 明確開啟的測試情境使用。
/// </summary>
public class WindowsCredentialStorage : ICredentialStorage
{
    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    // 僅於非 Windows 平台或測試情境（forceMemoryStorage）使用。
    private readonly ConcurrentDictionary<string, string> _memoryStore = new();
    private readonly bool _useWindowsCred;

    public WindowsCredentialStorage(bool forceMemoryStorage = false)
    {
        _useWindowsCred = !forceMemoryStorage && RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }

    public void SetPassword(string key, string password)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        if (!_useWindowsCred)
        {
            _memoryStore[key] = password;
            return;
        }

        WriteToCredentialManager(key, password);
    }

    public string? GetPassword(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        if (!_useWindowsCred)
        {
            return _memoryStore.TryGetValue(key, out var val) ? val : null;
        }

        try
        {
            if (CredReadW(key, CRED_TYPE_GENERIC, 0, out var credPtr))
            {
                try
                {
                    var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                    if (cred.CredentialBlobSize > 0 && cred.CredentialBlob != IntPtr.Zero)
                    {
                        var bytes = new byte[cred.CredentialBlobSize];
                        Marshal.Copy(cred.CredentialBlob, bytes, 0, (int)cred.CredentialBlobSize);
                        return Encoding.UTF8.GetString(bytes);
                    }
                    return string.Empty;
                }
                finally
                {
                    CredFree(credPtr);
                }
            }
        }
        catch
        {
            // 讀取失敗與「查無此項」在呼叫端的語意相同：取不到密碼。
        }

        return null;
    }

    public bool DeletePassword(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        if (!_useWindowsCred)
            return _memoryStore.TryRemove(key, out _);

        try
        {
            return CredDeleteW(key, CRED_TYPE_GENERIC, 0);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 寫入 Windows 認證管理員；失敗時附上 Win32 錯誤碼拋出，不保留任何明文。
    /// </summary>
    private static void WriteToCredentialManager(string key, string password)
    {
        var targetBytes = Encoding.Unicode.GetBytes(key + "\0");
        var passwordBytes = Encoding.UTF8.GetBytes(password);

        var targetPtr = Marshal.AllocHGlobal(targetBytes.Length);
        var passwordPtr = Marshal.AllocHGlobal(passwordBytes.Length);

        try
        {
            Marshal.Copy(targetBytes, 0, targetPtr, targetBytes.Length);
            Marshal.Copy(passwordBytes, 0, passwordPtr, passwordBytes.Length);

            var credential = new CREDENTIAL
            {
                Flags = 0,
                Type = CRED_TYPE_GENERIC,
                TargetName = targetPtr,
                CredentialBlobSize = (uint)passwordBytes.Length,
                CredentialBlob = passwordPtr,
                Persist = CRED_PERSIST_LOCAL_MACHINE
            };

            if (!CredWriteW(ref credential, 0))
            {
                var errorCode = Marshal.GetLastWin32Error();
                throw new CredentialStorageException(
                    CoreStrings.Format("CredentialStorage_Error_WriteFailed", errorCode),
                    errorCode);
            }
        }
        catch (Exception ex) when (ex is not CredentialStorageException)
        {
            throw new CredentialStorageException(
                CoreStrings.Format("CredentialStorage_Error_WriteFailedWithReason", ex.Message),
                ex);
        }
        finally
        {
            // 明文位元組不得殘留於記憶體——受管副本與交給原生 API 的非受管副本皆然。
            // 釋放非受管記憶體只是把它還給配置器，內容原封不動留在原處，
            // 因此必須先清零再釋放，否則行程記憶體被傾印時密碼仍讀得到。
            Array.Clear(passwordBytes);
            Marshal.Copy(new byte[passwordBytes.Length], 0, passwordPtr, passwordBytes.Length);
            Marshal.FreeHGlobal(targetPtr);
            Marshal.FreeHGlobal(passwordPtr);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWriteW(ref CREDENTIAL userCredential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredReadW(string target, uint type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDeleteW(string target, uint type, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);
}
