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
}
