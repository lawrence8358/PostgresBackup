using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

using PostgresBackup.Core.Resources;

namespace PostgresBackup.Core.Services;

/// <summary>
/// PostgreSQL 官方客戶端工具偵測與相容性檢查服務
/// </summary>
public class ToolDetectionService : IToolDetectionService
{
    private readonly IProcessRunner _processRunner;
    private readonly IEnvironmentProbe _envProbe;
    private readonly ILogger<ToolDetectionService> _logger;

    public ToolDetectionService(
        IProcessRunner processRunner,
        IEnvironmentProbe envProbe,
        ILogger<ToolDetectionService>? logger = null)
    {
        _processRunner = processRunner;
        _envProbe = envProbe;
        _logger = logger ?? NullLogger<ToolDetectionService>.Instance;
    }

    public async Task<ToolDetectionResult> DetectAsync(string? customPath = null, CancellationToken ct = default)
    {
        // 1. 自訂路徑檢查
        if (!string.IsNullOrWhiteSpace(customPath))
        {
            var customCandidates = new[]
            {
                customPath,
                Path.Combine(customPath, "bin")
            };

            foreach (var candidate in customCandidates)
            {
                if (_envProbe.DirectoryExists(candidate))
                {
                    var found = await TryResolveToolsFromDirAsync(candidate, DetectionSource.CustomPath, ct);
                    if (found != null)
                    {
                        _logger.LogInformation("Tools found via the user-specified path: {Path}", found.PgDumpPath);
                        return found;
                    }
                }
            }
        }

        // 2. 系統環境變數 PATH 檢查
        foreach (var pathDir in _envProbe.GetPathEntries())
        {
            var found = await TryResolveToolsFromDirAsync(pathDir, DetectionSource.Path, ct);
            if (found != null)
            {
                _logger.LogInformation("Tools found via the PATH environment variable: {Path}", found.PgDumpPath);
                return found;
            }
        }

        // 3. 常見 Windows 安裝路徑掃描
        foreach (var commonDir in _envProbe.GetCommonPostgreSqlDirectories())
        {
            var found = await TryResolveToolsFromDirAsync(commonDir, DetectionSource.CommonDirectory, ct);
            if (found != null)
            {
                _logger.LogInformation("Tools found via a common install directory: {Path}", found.PgDumpPath);
                return found;
            }
        }

        // 4. Windows Registry 登錄檔掃描
        foreach (var regDir in _envProbe.GetRegistryInstallations())
        {
            var found = await TryResolveToolsFromDirAsync(regDir, DetectionSource.Registry, ct);
            if (found != null)
            {
                _logger.LogInformation("Tools found via the Windows Registry: {Path}", found.PgDumpPath);
                return found;
            }
        }

        _logger.LogWarning("PostgreSQL client tools were not detected in any default or specified location.");
        return ToolDetectionResult.CreateNotFound(CoreStrings.Get("ToolDetection_NotFound_Detail"));
    }

    public async Task<VersionCheckResult> CheckCompatibilityAsync(
        ToolDetectionResult clientTools,
        string connectionString,
        CancellationToken ct = default)
    {
        if (!clientTools.IsReady || clientTools.Version == null)
        {
            return VersionCheckResult.Failed(CoreStrings.Get("ToolDetection_Error_NotReady"));
        }

        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(ct);

            string serverVersionStr;
            int serverMajor;

            await using (var cmd = new NpgsqlCommand("SHOW server_version;", connection))
            {
                var val = await cmd.ExecuteScalarAsync(ct);
                serverVersionStr = val?.ToString() ?? string.Empty;
            }

            await using (var cmdNum = new NpgsqlCommand("SHOW server_version_num;", connection))
            {
                var val = await cmdNum.ExecuteScalarAsync(ct);
                if (val != null && int.TryParse(val.ToString(), out var num))
                {
                    serverMajor = num / 10000;
                }
                else if (ToolVersion.TryParse(serverVersionStr, out var parsedVer) && parsedVer != null)
                {
                    serverMajor = parsedVer.Major;
                }
                else
                {
                    serverMajor = 0;
                }
            }

            return CheckCompatibility(clientTools.Version, serverMajor, serverVersionStr);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while connecting to the database server to check its version.");
            return VersionCheckResult.Failed(CoreStrings.Format("ToolDetection_Error_ServerCheckFailed", ex.Message));
        }
    }

    public VersionCheckResult CheckCompatibility(
        ToolVersion clientVersion,
        int serverMajorVersion,
        string? serverVersionString = null)
    {
        if (clientVersion.Major >= serverMajorVersion)
        {
            return VersionCheckResult.Compatible(clientVersion, serverMajorVersion, serverVersionString);
        }

        return VersionCheckResult.Incompatible(clientVersion, serverMajorVersion, serverVersionString);
    }

    private async Task<ToolDetectionResult?> TryResolveToolsFromDirAsync(
        string directory,
        DetectionSource source,
        CancellationToken ct)
    {
        string[] dumpCandidates = OperatingSystem.IsWindows()
            ? new[] { Path.Combine(directory, "pg_dump.exe"), Path.Combine(directory, "pg_dump") }
            : new[] { Path.Combine(directory, "pg_dump"), Path.Combine(directory, "pg_dump.exe") };

        string? dumpPath = dumpCandidates.FirstOrDefault(p => _envProbe.FileExists(p));
        if (dumpPath == null)
        {
            return null;
        }

        string[] restoreCandidates = OperatingSystem.IsWindows()
            ? new[] { Path.Combine(directory, "pg_restore.exe"), Path.Combine(directory, "pg_restore") }
            : new[] { Path.Combine(directory, "pg_restore"), Path.Combine(directory, "pg_restore.exe") };

        string? restorePath = restoreCandidates.FirstOrDefault(p => _envProbe.FileExists(p));

        string[] psqlCandidates = OperatingSystem.IsWindows()
            ? new[] { Path.Combine(directory, "psql.exe"), Path.Combine(directory, "psql") }
            : new[] { Path.Combine(directory, "psql"), Path.Combine(directory, "psql.exe") };

        string? psqlPath = psqlCandidates.FirstOrDefault(p => _envProbe.FileExists(p));

        ToolVersion? version = null;
        try
        {
            var procResult = await _processRunner.RunAsync(dumpPath, "--version", null, ct);
            if (procResult.Success && !string.IsNullOrWhiteSpace(procResult.StandardOutput))
            {
                ToolVersion.TryParse(procResult.StandardOutput, out version);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse the version number from {DumpPath} --version", dumpPath);
        }

        return ToolDetectionResult.CreateFound(dumpPath, restorePath, psqlPath, version, source);
    }
}
