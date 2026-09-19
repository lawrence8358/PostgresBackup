using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PostgresBackup.Core.Exceptions;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Resources;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 以 Windows 資料保護 API 的<b>機器範圍</b>加密實作憑證安全存儲，
/// 供命令列連線設定使用：加密結果僅能在同一台機器上解開，
/// 因此以 SYSTEM 身分執行的排程任務亦可讀取。
/// 存放檔案位於機器層級目錄並套用限制性檔案權限。
/// 寫入失敗一律拋出 <see cref="CredentialStorageException"/>，絕不靜默保留明文於行程記憶體；
/// 記憶體儲存路徑僅供非 Windows 平台，以及由 <c>forceMemoryStorage</c> 明確開啟的測試情境使用。
/// </summary>
public class MachineScopedCredentialStorage : ICredentialStorage
{
    // 額外熵值，避免此存放區的加密結果與同機其他使用機器金鑰的資料相互混淆。
    // 這不是一道安全防線：此常數就在公開的原始碼裡，同機的任何程式都讀得到。
    // 真正阻擋同機一般使用者的是存放目錄的檔案權限。
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PostgresBackup.Cli.MachineScopedCredentialStore.v1");

    // 僅於非 Windows 平台或測試情境（forceMemoryStorage）使用。
    private readonly ConcurrentDictionary<string, string> _memoryStore = new();
    private readonly bool _useDataProtection;
    private readonly bool _forceMemoryStorage;
    private readonly MachineScopedStoreLocation _location;
    private readonly string _filePath;
    private readonly object _fileLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <param name="location">
    /// 存放區的位置與權限政策。兩者以單一物件成對傳入，
    /// 讓「寫到哪裡」與「那裡該套用什麼權限」不可能各說各話。
    /// 省略時使用正式環境的預設位置。
    /// </param>
    public MachineScopedCredentialStorage(
        MachineScopedStoreLocation? location = null,
        bool forceMemoryStorage = false)
    {
        _forceMemoryStorage = forceMemoryStorage;
        _useDataProtection = !forceMemoryStorage && RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        _location = location ?? MachineScopedStoreLocation.Default;
        _filePath = _location.CredentialsFilePath;
    }

    public void SetPassword(string key, string password)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Key cannot be null or empty", nameof(key));

        if (!_useDataProtection)
        {
            // 非 Windows 平台無法使用機器範圍加密。除非測試明確要求記憶體儲存，
            // 否則必須明確失敗，不得讓呼叫端誤以為密碼已安全保存。
            if (!_forceMemoryStorage)
            {
                throw new CredentialStorageException(
                    CoreStrings.Get("MachineCredentialStorage_Error_PlatformUnsupported"));
            }

            _memoryStore[key] = password;
            return;
        }

        try
        {
            lock (_fileLock)
            {
                var entries = LoadEntries();
                entries[key] = Convert.ToBase64String(Protect(password));
                SaveEntries(entries);
            }
        }
        catch (Exception ex) when (ex is not CredentialStorageException
                                       and not CredentialStoreCorruptedException
                                       and not ProfileStoreAccessDeniedException)
        {
            throw new CredentialStorageException(
                CoreStrings.Format("MachineCredentialStorage_Error_WriteFailed", ex.Message),
                ex);
        }
    }

    public string? GetPassword(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return null;

        if (!_useDataProtection)
        {
            return _memoryStore.TryGetValue(key, out var val) ? val : null;
        }

        try
        {
            lock (_fileLock)
            {
                var entries = LoadEntries();
                if (!entries.TryGetValue(key, out var encoded) || string.IsNullOrWhiteSpace(encoded))
                    return null;

                return Unprotect(Convert.FromBase64String(encoded));
            }
        }
        catch (Exception ex) when (ex is not CredentialStoreCorruptedException
                                       and not ProfileStoreAccessDeniedException)
        {
            // 解密失敗與「查無此項」在呼叫端的語意相同：取不到密碼。
            // 但「檔案損毀」與「權限不足」不同——兩者都有明確原因且需要使用者處理，
            // 折疊成 null 只會讓人以為密碼從來沒被設定過。
            return null;
        }
    }

    public bool DeletePassword(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        if (!_useDataProtection)
            return _memoryStore.TryRemove(key, out _);

        try
        {
            lock (_fileLock)
            {
                var entries = LoadEntries();
                if (!entries.Remove(key))
                    return false;

                SaveEntries(entries);
                return true;
            }
        }
        catch (Exception ex) when (ex is not CredentialStoreCorruptedException
                                       and not ProfileStoreAccessDeniedException)
        {
            return false;
        }
    }

    private static byte[] Protect(string password)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                CoreStrings.Get("MachineCredentialStorage_Error_PlatformUnsupported"));
        }

        var plaintext = Encoding.UTF8.GetBytes(password);
        try
        {
            return ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.LocalMachine);
        }
        finally
        {
            Array.Clear(plaintext);
        }
    }

    private static string Unprotect(byte[] ciphertext)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException(
                CoreStrings.Get("MachineCredentialStorage_Error_PlatformUnsupported"));
        }

        var plaintext = ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.LocalMachine);
        try
        {
            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            Array.Clear(plaintext);
        }
    }

    private Dictionary<string, string> LoadEntries()
    {
        if (!File.Exists(_filePath))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        string json;
        try
        {
            json = File.ReadAllText(_filePath);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ProfileStoreAccessDeniedException(
                CoreStrings.Format("MachineCredentialStorage_Error_AccessDenied", _filePath), ex);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            // 不得當成空的存放區：那會讓所有密碼同時靜默變成「遺失」，
            // 而下一次寫入還會把損毀的檔案覆蓋掉，連救回來的機會都沒有。
            throw new CredentialStoreCorruptedException(
                CoreStrings.Format("MachineCredentialStorage_Error_FileCorrupted", _filePath, ex.Message),
                ex);
        }
    }

    private void SaveEntries(Dictionary<string, string> entries)
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            MachineScopedStore.EnsureRestrictedDirectory(dir, _location.EnforceRestrictivePermissions);
        }

        // 原子替換：先寫暫存檔、確實落盤，再換上去。整份憑證是單一檔案，
        // 若就地覆寫到一半斷電，留下的截斷內容會讓所有密碼一起失效。
        //
        // 暫存檔名帶隨機值而非固定的 ".tmp"：此存放區的設計前提就是
        // 「管理員的互動式行程」與「SYSTEM 排程行程」會同時存在，
        // 固定檔名會讓兩個行程在同一個暫存檔上互相踩踏。
        var tempPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";

        try
        {
            var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(entries, JsonOptions));

            // WriteThrough + Flush(true)：讓資料在改名之前就真的到達磁碟。
            // 少了這一步，「斷電也不會留下截斷內容」只是作業系統快取的善意，不是保證。
            using (var stream = new FileStream(
                       tempPath, FileMode.Create, FileAccess.Write, FileShare.None,
                       bufferSize: 4096, FileOptions.WriteThrough))
            {
                stream.Write(payload);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch
        {
            try { File.Delete(tempPath); } catch { }
            throw;
        }
    }
}
