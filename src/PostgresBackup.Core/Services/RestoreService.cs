using System.Diagnostics;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

using PostgresBackup.Core.Resources;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 實作安全還原作業服務，具備還原前強制安全快照防護與阻斷機制
/// </summary>
public class RestoreService : IRestoreService
{
    private readonly IProcessRunner _processRunner;
    private readonly IToolDetectionService _toolDetector;
    private readonly IBackupService? _backupService;
    private readonly IBackupHistoryRepository? _historyRepo;
    private readonly IRestoreTargetCatalogReader _catalogReader;
    private readonly IRestoreDataPreparationService _dataPreparationService;

    public RestoreService(
        IProcessRunner processRunner,
        IToolDetectionService toolDetector,
        IBackupService? backupService = null,
        IBackupHistoryRepository? historyRepo = null,
        IRestoreTargetCatalogReader? catalogReader = null,
        IRestoreDataPreparationService? dataPreparationService = null)
    {
        _processRunner = processRunner;
        _toolDetector = toolDetector;
        _backupService = backupService;
        _historyRepo = historyRepo;
        _catalogReader = catalogReader ?? new NpgsqlRestoreTargetCatalogReader();
        _dataPreparationService = dataPreparationService ?? new NpgsqlRestoreDataPreparationService();
    }

    public async Task<RestoreResult> RestoreAsync(
        RestoreOptions options,
        Action<string>? onLogLine = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // 0. 拒絕以完整連線字串描述的連線。目標資料庫的檢查與清空走 ToConnectionString()
        // （認得連線字串），pg_restore 的參數卻只由主機／連接埠／使用者／資料庫欄位組成
        // （不認得連線字串）。兩者若指向不同伺服器，還原會清空一台、寫入另一台。
        if (!string.IsNullOrWhiteSpace(options.Connection.ConnectionString))
        {
            var errMsg = CoreStrings.Get("Restore_Error_ConnectionStringNotSupported");
            onLogLine?.Invoke($"[ERROR] {errMsg}");
            return RestoreResult.Failure(errMsg, -1, TimeSpan.Zero, string.Empty);
        }

        var stopwatch = Stopwatch.StartNew();

        // 1. 驗證來源檔案是否存在
        if (!File.Exists(options.SourceFilePath))
        {
            var errMsg = CoreStrings.Format("Restore_Error_SourceMissing", options.SourceFilePath);
            onLogLine?.Invoke($"[ERROR] {errMsg}");
            return RestoreResult.Failure(errMsg, -1, TimeSpan.Zero, string.Empty);
        }

        // 自動推斷格式
        if (options.Format == BackupFormat.Custom && options.SourceFilePath.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
        {
            options.Format = BackupFormat.Plain;
        }

        // 2. 尋找與驗證工具路徑
        var detection = await _toolDetector.DetectAsync(options.ClientToolDirectory, ct);
        if (!detection.IsReady)
        {
            var errMsg = CoreStrings.Get("Restore_Error_ToolsNotFound");
            onLogLine?.Invoke($"[ERROR] {errMsg}");
            return RestoreResult.Failure(errMsg, -1, TimeSpan.Zero, string.Empty);
        }

        var toolExecutablePath = options.Format == BackupFormat.Custom
            ? detection.PgRestorePath
            : detection.PsqlPath;

        if (string.IsNullOrWhiteSpace(toolExecutablePath))
        {
            var toolName = options.Format == BackupFormat.Custom ? "pg_restore" : "psql";
            var errMsg = CoreStrings.Format("Restore_Error_ToolMissing", toolName);
            onLogLine?.Invoke($"[ERROR] {errMsg}");
            return RestoreResult.Failure(errMsg, -1, TimeSpan.Zero, string.Empty);
        }

        var targetDb = !string.IsNullOrWhiteSpace(options.TargetDatabase)
            ? options.TargetDatabase
            : options.Connection.Database;

        var envVars = BuildEnvironmentVariables(options.Connection.Password);
        RestoreArchivePlan? restorePlan = null;
        RestoreDataPlan? dataPlan = null;

        // Planning is read-only. Run it before the safety snapshot so a no-op restore
        // does not spend minutes producing an unnecessary full backup.
        if (options.Format == BackupFormat.Custom
            && options.Mode is RestoreMode.Normal or RestoreMode.DataOnly)
        {
            var planLogKey = options.Mode == RestoreMode.Normal
                ? "Restore_Log_PlanStart"
                : "Restore_Log_DataPlanStart";
            onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Get(planLogKey)}");
            var listArgs = $"--list \"{EscapeArgument(options.SourceFilePath)}\"";
            var listResult = await _processRunner.RunAsync(
                toolExecutablePath,
                listArgs,
                envVars,
                ct: ct);

            if (!listResult.Success)
            {
                var detail = listResult.ErrorMessage ?? CoreStrings.Get("Restore_Error_NonZeroExit");
                var errMsg = CoreStrings.Format("Restore_Error_ArchiveListFailed", detail);
                onLogLine?.Invoke($"[ERROR] {errMsg}");
                return RestoreResult.Failure(errMsg, listResult.ExitCode, stopwatch.Elapsed, listArgs);
            }

            try
            {
                var targetCatalog = await _catalogReader.ReadAsync(options.Connection, targetDb, ct);
                if (options.Mode == RestoreMode.Normal)
                {
                    restorePlan = RestoreArchivePlanner.Build(listResult.StandardOutput, targetCatalog);
                    if (restorePlan.UnsupportedDescriptions.Count > 0)
                    {
                        var descriptions = string.Join(", ", restorePlan.UnsupportedDescriptions);
                        var errMsg = CoreStrings.Format("Restore_Error_UnsupportedArchiveEntries", descriptions);
                        onLogLine?.Invoke($"[ERROR] {errMsg}");
                        return RestoreResult.Failure(errMsg, -1, stopwatch.Elapsed, listArgs);
                    }

                    onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Format(
                        "Restore_Log_PlanSummary",
                        restorePlan.IncludedCount,
                        restorePlan.SkippedExistingCount,
                        restorePlan.SkippedMetadataCount)}");
                }
                else
                {
                    dataPlan = RestoreArchivePlanner.BuildDataOnly(listResult.StandardOutput, targetCatalog);
                    if (dataPlan.UnsupportedDescriptions.Count > 0)
                    {
                        var descriptions = string.Join(", ", dataPlan.UnsupportedDescriptions);
                        var errMsg = CoreStrings.Format("Restore_Error_UnsupportedDataEntries", descriptions);
                        onLogLine?.Invoke($"[ERROR] {errMsg}");
                        return RestoreResult.Failure(errMsg, -1, stopwatch.Elapsed, listArgs);
                    }

                    if (dataPlan.MissingObjects.Count > 0)
                    {
                        var objects = string.Join(", ", dataPlan.MissingObjects);
                        var errMsg = CoreStrings.Format("Restore_Error_DataTargetsMissing", objects);
                        onLogLine?.Invoke($"[ERROR] {errMsg}");
                        return RestoreResult.Failure(errMsg, -1, stopwatch.Elapsed, listArgs);
                    }

                    if (dataPlan.CyclicTables.Count > 0)
                    {
                        var tables = string.Join(", ", dataPlan.CyclicTables);
                        var errMsg = CoreStrings.Format("Restore_Error_DataDependencyCycle", tables);
                        onLogLine?.Invoke($"[ERROR] {errMsg}");
                        return RestoreResult.Failure(errMsg, -1, stopwatch.Elapsed, listArgs);
                    }

                    onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Format(
                        "Restore_Log_DataPlanSummary",
                        dataPlan.Tables.Count,
                        dataPlan.DataEntryCount)}");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var errMsg = CoreStrings.Format("Restore_Error_PlanFailed", ex.Message);
                onLogLine?.Invoke($"[ERROR] {errMsg}");
                return RestoreResult.Failure(errMsg, -1, stopwatch.Elapsed, listArgs);
            }
        }

        string? snapshotFilePath = null;

        // 3. 還原前安全快照 (Pre-Restore Snapshot) — 透過 BackupService 作業模組安全委任
        var plannedChangeCount = restorePlan?.ActionableCount ?? dataPlan?.DataEntryCount ?? 1;
        if (options.CreatePreRestoreSnapshot && plannedChangeCount > 0)
        {
            onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SAFETY] {CoreStrings.Format("Restore_Log_SnapshotStart", targetDb)}");

            var snapshotDir = options.SnapshotDirectory;
            if (string.IsNullOrWhiteSpace(snapshotDir))
            {
                var baseDir = Path.GetDirectoryName(options.SourceFilePath);
                if (string.IsNullOrEmpty(baseDir))
                {
                    baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PostgresBackups");
                }
                snapshotDir = Path.Combine(baseDir, "snapshots");
            }

            if (!Directory.Exists(snapshotDir))
            {
                Directory.CreateDirectory(snapshotDir);
            }

            var snapshotFileName = $"{targetDb}_snapshot_{DateTime.Now:yyyyMMddHHmmss}.dump";

            var snapshotOptions = new BackupOptions
            {
                Connection = options.Connection,
                Format = BackupFormat.Custom,
                Mode = BackupMode.SchemaAndData,
                Scope = BackupScope.FullDatabase,
                OutputDirectory = snapshotDir,
                CustomFileName = snapshotFileName,
                ClientToolDirectory = options.ClientToolDirectory,
                OperationType = BackupOperationType.PreRestoreSnapshot
            };

            if (_backupService == null)
            {
                var errMsg = CoreStrings.Get("Restore_Error_SnapshotServiceMissing");
                onLogLine?.Invoke($"[CRITICAL ABORT] {errMsg}");
                return RestoreResult.Failure(errMsg, -1, stopwatch.Elapsed, string.Empty);
            }

            var snapshotResult = await _backupService.BackupAsync(
                snapshotOptions,
                onLogLine: line => onLogLine?.Invoke($"[snapshot] {line}"),
                ct: ct);

            if (!snapshotResult.IsSuccess)
            {
                var errMsg = CoreStrings.Format(
                    "Restore_Error_SnapshotFailed",
                    snapshotResult.ExitCode,
                    snapshotResult.ErrorMessage ?? CoreStrings.Get("Restore_Error_SnapshotFileMissing"));
                onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [CRITICAL ABORT] {errMsg}");
                return RestoreResult.Failure(errMsg, snapshotResult.ExitCode, stopwatch.Elapsed, snapshotResult.Arguments);
            }

            snapshotFilePath = snapshotResult.OutputFilePath;
            onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SAFETY SUCCESS] {CoreStrings.Format("Restore_Log_SnapshotSuccess", snapshotResult.FileSizeBytes)}");
        }

        string? restoreListFilePath = null;
        var restoreListContent = restorePlan?.Content ?? dataPlan?.Content;
        if (restoreListContent is not null)
        {
            try
            {
                restoreListFilePath = Path.Combine(
                    Path.GetTempPath(), $"PostgresBackup-restore-{Guid.NewGuid():N}.list");
                await File.WriteAllTextAsync(restoreListFilePath, restoreListContent, ct);
            }
            catch (OperationCanceledException)
            {
                DeleteRestoreListFile(restoreListFilePath);
                throw;
            }
            catch (Exception ex)
            {
                DeleteRestoreListFile(restoreListFilePath);
                var errMsg = CoreStrings.Format("Restore_Error_PlanFailed", ex.Message);
                onLogLine?.Invoke($"[ERROR] {errMsg}");
                return RestoreResult.Failure(errMsg, -1, stopwatch.Elapsed, string.Empty, snapshotFilePath);
            }
        }

        if (dataPlan is { Tables.Count: > 0 })
        {
            onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SAFETY] {CoreStrings.Format(
                "Restore_Log_DataClearStart", dataPlan.Tables.Count)}");
            try
            {
                await _dataPreparationService.ClearTablesAsync(
                    options.Connection, targetDb, dataPlan.Tables, ct);
            }
            catch (OperationCanceledException)
            {
                DeleteRestoreListFile(restoreListFilePath);
                throw;
            }
            catch (Exception ex)
            {
                DeleteRestoreListFile(restoreListFilePath);
                var errMsg = CoreStrings.Format("Restore_Error_DataClearFailed", ex.Message);
                onLogLine?.Invoke($"[ERROR] {errMsg}");
                return RestoreResult.Failure(errMsg, -1, stopwatch.Elapsed, string.Empty, snapshotFilePath);
            }
        }

        // 4. 執行還原作業
        var restoreArgs = RestoreArgumentsBuilder.Build(options, restoreListFilePath);
        var toolNameDisplay = Path.GetFileNameWithoutExtension(toolExecutablePath);

        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Format("Restore_Log_Start", toolNameDisplay, targetDb)}");
        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Format("Restore_Log_SourceFile", options.SourceFilePath)}");
        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Format("Restore_Log_Mode", options.Mode)}");

        ProcessResult restoreProc;
        try
        {
            restoreProc = await _processRunner.RunAsync(
                toolExecutablePath,
                restoreArgs,
                envVars,
                onOutputLine: line => onLogLine?.Invoke($"[{toolNameDisplay}] {line}"),
                onErrorLine: line => onLogLine?.Invoke($"[{toolNameDisplay}] {line}"),
                ct: ct);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(restoreListFilePath))
            {
                DeleteRestoreListFile(restoreListFilePath);
            }
        }

        stopwatch.Stop();

        if (restoreProc.ExitCode == 0)
        {
            onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SUCCESS] {CoreStrings.Format("Restore_Log_Success", CoreStrings.Format("Format_DurationSeconds", stopwatch.Elapsed.TotalSeconds.ToString("F2")))}");

            if (_historyRepo != null)
            {
                try
                {
                    await _historyRepo.AddRecordAsync(new BackupRecord
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        OperationType = BackupOperationType.Restore,
                        DatabaseName = targetDb,
                        TargetDatabase = targetDb,
                        FilePath = options.SourceFilePath,
                        Format = options.Format,
                        FileSizeBytes = new FileInfo(options.SourceFilePath).Length,
                        DurationMs = (long)stopwatch.Elapsed.TotalMilliseconds,
                        Status = BackupStatus.Success,
                        Arguments = restoreArgs
                    }, ct);
                }
                catch { }
            }

            return RestoreResult.Success(stopwatch.Elapsed, restoreArgs, snapshotFilePath);
        }
        else
        {
            var err = !string.IsNullOrWhiteSpace(restoreProc.StandardError)
                ? restoreProc.StandardError
                : restoreProc.ErrorMessage ?? CoreStrings.Get("Restore_Error_NonZeroExit");

            onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [ERROR] {CoreStrings.Format("Restore_Log_Failed", restoreProc.ExitCode, err)}");

            if (_historyRepo != null)
            {
                try
                {
                    await _historyRepo.AddRecordAsync(new BackupRecord
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        OperationType = BackupOperationType.Restore,
                        DatabaseName = targetDb,
                        TargetDatabase = targetDb,
                        FilePath = options.SourceFilePath,
                        Format = options.Format,
                        FileSizeBytes = new FileInfo(options.SourceFilePath).Length,
                        DurationMs = (long)stopwatch.Elapsed.TotalMilliseconds,
                        Status = BackupStatus.Failed,
                        ErrorMessage = err,
                        Arguments = restoreArgs
                    }, ct);
                }
                catch { }
            }

            return RestoreResult.Failure(err, restoreProc.ExitCode, stopwatch.Elapsed, restoreArgs, snapshotFilePath);
        }
    }

    private static Dictionary<string, string?> BuildEnvironmentVariables(string? password)
    {
        var envVars = new Dictionary<string, string?>();
        if (!string.IsNullOrEmpty(password)) envVars["PGPASSWORD"] = password;

        // Keep client data encoding explicit and avoid localized Windows messages
        // being emitted in a code page that the redirected process cannot decode.
        envVars["PGCLIENTENCODING"] = "UTF8";
        envVars["LC_ALL"] = "C";
        envVars["LC_MESSAGES"] = "C";
        envVars["LANG"] = "C";
        envVars["LANGUAGE"] = null;
        return envVars;
    }

    private static string EscapeArgument(string value) => value.Replace("\"", "\\\"");

    private static void DeleteRestoreListFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { File.Delete(path); } catch { }
    }
}
