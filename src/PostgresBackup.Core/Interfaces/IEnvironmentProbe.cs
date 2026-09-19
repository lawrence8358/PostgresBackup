using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Interfaces;

/// <summary>
/// 探測作業系統環境（檔案系統、環境變數、常見路徑、Registry）的抽象介面
/// </summary>
public interface IEnvironmentProbe
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    string? GetEnvironmentVariable(string variable);
    IEnumerable<string> GetPathEntries();
    IEnumerable<string> GetCommonPostgreSqlDirectories();
    IEnumerable<string> GetRegistryInstallations();

    /// <summary>
    /// 查詢目錄的存取權限。權限查詢刻意置於本介面而非連線設定倉儲：
    /// 探測作業系統環境即為本介面的既有職責，且倉儲介面不應為命令列專屬需求
    /// 強迫圖形介面實作一個它永遠用不到的方法。
    /// 目錄不存在時回傳 <c>Exists</c> 為 <c>false</c> 的結果；無法查詢時回傳 <c>null</c>。
    /// </summary>
    DirectoryAccessInfo? GetDirectoryAccessInfo(string path);
}
