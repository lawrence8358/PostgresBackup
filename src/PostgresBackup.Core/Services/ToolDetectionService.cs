using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

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
                        _logger.LogInformation("已自使用者指定路徑找到工具: {Path}", found.PgDumpPath);
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
                _logger.LogInformation("已自 PATH 環境變數找到工具: {Path}", found.PgDumpPath);
                return found;
            }
        }

        // 3. 常見 Windows 安裝路徑掃描
        foreach (var commonDir in _envProbe.GetCommonPostgreSqlDirectories())
        {
            var found = await TryResolveToolsFromDirAsync(commonDir, DetectionSource.CommonDirectory, ct);
            if (found != null)
            {
                _logger.LogInformation("已自常見安裝路徑找到工具: {Path}", found.PgDumpPath);
                return found;
            }
        }

        // 4. Windows Registry 登錄檔掃描
        foreach (var regDir in _envProbe.GetRegistryInstallations())
        {
            var found = await TryResolveToolsFromDirAsync(regDir, DetectionSource.Registry, ct);
            if (found != null)
            {
                _logger.LogInformation("已自 Windows Registry 找到工具: {Path}", found.PgDumpPath);
                return found;
            }
        }

        _logger.LogWarning("未在任何預設或指定路徑中偵測到 PostgreSQL 客戶端工具。");
        return ToolDetectionResult.CreateNotFound(
            "未在指定目錄、系統 PATH、常見安裝路徑或 Registry 中找到官方客戶端工具 (pg_dump / pg_restore)。");
    }

    public async Task<VersionCheckResult> CheckCompatibilityAsync(
        ToolDetectionResult clientTools,
        string connectionString,
        CancellationToken ct = default)
    {
        if (!clientTools.IsReady || clientTools.Version == null)
        {
            return VersionCheckResult.Failed("客戶端工具尚未就緒，無法進行版本相容性檢查。");
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
            _logger.LogError(ex, "連線資料庫伺服器檢查版本時發生錯誤");
            return VersionCheckResult.Failed($"連線資料庫驗證伺服器版本失敗: {ex.Message}");
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
            _logger.LogWarning(ex, "執行 {DumpPath} --version 解析版本號失敗", dumpPath);
        }

        return ToolDetectionResult.CreateFound(dumpPath, restorePath, psqlPath, version, source);
    }
}
