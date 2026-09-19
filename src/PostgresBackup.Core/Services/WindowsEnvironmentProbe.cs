using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 針對 Windows 環境之檔案系統、環境變數與 Registry 探測實作
/// </summary>
public class WindowsEnvironmentProbe : IEnvironmentProbe
{
    public bool FileExists(string path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    public bool DirectoryExists(string path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    public string? GetEnvironmentVariable(string variable) => Environment.GetEnvironmentVariable(variable);

    public IEnumerable<string> GetPathEntries()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
        {
            return Enumerable.Empty<string>();
        }

        return pathEnv
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Directory.Exists);
    }

    public IEnumerable<string> GetCommonPostgreSqlDirectories()
    {
        var results = new List<(int Version, string BinPath)>();

        var candidateBases = new List<string>();

        var progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(progFiles))
        {
            candidateBases.Add(Path.Combine(progFiles, "PostgreSQL"));
        }

        var progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(progFilesX86) && !string.Equals(progFiles, progFilesX86, StringComparison.OrdinalIgnoreCase))
        {
            candidateBases.Add(Path.Combine(progFilesX86, "PostgreSQL"));
        }

        foreach (var basePath in candidateBases)
        {
            if (!Directory.Exists(basePath))
            {
                continue;
            }

            try
            {
                var dirs = Directory.GetDirectories(basePath);
                foreach (var dir in dirs)
                {
                    var dirName = Path.GetFileName(dir);
                    var binDir = Path.Combine(dir, "bin");
                    if (Directory.Exists(binDir))
                    {
                        if (int.TryParse(dirName, out var versionNum))
                        {
                            results.Add((versionNum, binDir));
                        }
                        else
                        {
                            results.Add((0, binDir));
                        }
                    }
                }
            }
            catch
            {
                // 忽略權限不足或其他存取例外
            }
        }

        // 依主版本號降冪排序（優先選擇最新版本），去重複
        return results
            .OrderByDescending(r => r.Version)
            .Select(r => r.BinPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public DirectoryAccessInfo? GetDirectoryAccessInfo(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!Directory.Exists(path))
        {
            return new DirectoryAccessInfo { Path = path, Exists = false };
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new DirectoryAccessInfo { Path = path, Exists = true };
        }

        return GetDirectoryAccessInfoInternal(path);
    }

    [SupportedOSPlatform("windows")]
    private static DirectoryAccessInfo? GetDirectoryAccessInfoInternal(string path)
    {
        try
        {
            var security = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access);
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));

            var allowed = new List<string>();
            foreach (FileSystemAccessRule rule in rules)
            {
                if (rule.AccessControlType != AccessControlType.Allow)
                {
                    continue;
                }

                var sid = rule.IdentityReference.Value;
                if (!allowed.Contains(sid, StringComparer.OrdinalIgnoreCase))
                {
                    allowed.Add(sid);
                }
            }

            return new DirectoryAccessInfo
            {
                Path = path,
                Exists = true,
                InheritanceEnabled = !security.AreAccessRulesProtected,
                AllowedIdentities = allowed
            };
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }

    public IEnumerable<string> GetRegistryInstallations()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Enumerable.Empty<string>();
        }

        return GetRegistryInstallationsInternal();
    }

    [SupportedOSPlatform("windows")]
    private IEnumerable<string> GetRegistryInstallationsInternal()
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var registryKeys = new[]
        {
            @"SOFTWARE\PostgreSQL\Installations",
            @"SOFTWARE\WOW6432Node\PostgreSQL\Installations"
        };

        foreach (var subKeyPath in registryKeys)
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Default);
                using var key = hklm.OpenSubKey(subKeyPath);
                if (key == null) continue;

                foreach (var installSubKeyName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var installKey = key.OpenSubKey(installSubKeyName);
                        if (installKey == null) continue;

                        var baseDir = installKey.GetValue("Base Directory") as string;
                        if (!string.IsNullOrWhiteSpace(baseDir))
                        {
                            var binDir = Path.Combine(baseDir, "bin");
                            paths.Add(binDir);
                        }
                    }
                    catch
                    {
                        // 忽略個別 subkey 存取錯誤
                    }
                }
            }
            catch
            {
                // 忽略 Registry 存取權限錯誤
            }
        }

        return paths;
    }
}
