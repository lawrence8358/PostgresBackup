using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;
using PostgresBackup.Core.Interfaces;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 使用 Windows Credential Manager 實作憑證安全存儲。
/// 非 Windows 平台或本機環境不支援時提供記憶體安全備援。
/// </summary>
public class WindowsCredentialStorage : ICredentialStorage
{
    private const uint CRED_TYPE_GENERIC = 1;
    private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

    // 備援記憶體字典（非 Windows 環境或單元測試模擬）
    private readonly ConcurrentDictionary<string, string> _memoryFallback = new();
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
            _memoryFallback[key] = password;
            return;
        }

        try
        {
            var credential = new CREDENTIAL();
            var targetBytes = Encoding.Unicode.GetBytes(key + "\0");
            var passwordBytes = Encoding.UTF8.GetBytes(password);

            var targetPtr = Marshal.AllocHGlobal(targetBytes.Length);
            var passwordPtr = Marshal.AllocHGlobal(passwordBytes.Length);

            try
            {
                Marshal.Copy(targetBytes, 0, targetPtr, targetBytes.Length);
                Marshal.Copy(passwordBytes, 0, passwordPtr, passwordBytes.Length);

                credential.Flags = 0;
                credential.Type = CRED_TYPE_GENERIC;
                credential.TargetName = targetPtr;
                credential.CredentialBlobSize = (uint)passwordBytes.Length;
                credential.CredentialBlob = passwordPtr;
                credential.Persist = CRED_PERSIST_LOCAL_MACHINE;

                if (!CredWriteW(ref credential, 0))
                {
                    // 若寫入失敗則回退至記憶體字典
                    _memoryFallback[key] = password;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(targetPtr);
                Marshal.FreeHGlobal(passwordPtr);
            }
        }
        catch
        {
            _memoryFallback[key] = password;
        }
    }

    public string? GetPassword(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        if (!_useWindowsCred)
        {
            return _memoryFallback.TryGetValue(key, out var val) ? val : null;
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
            // 忽略原生讀取錯誤並檢查 fallback
        }

        return _memoryFallback.TryGetValue(key, out var fallbackVal) ? fallbackVal : null;
    }

    public bool DeletePassword(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        _memoryFallback.TryRemove(key, out _);

        if (!_useWindowsCred)
            return true;

        try
        {
            return CredDeleteW(key, CRED_TYPE_GENERIC, 0);
        }
        catch
        {
            return false;
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
