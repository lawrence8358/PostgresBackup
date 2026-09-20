using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

using PostgresBackup.Core.Resources;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 實作安全還原作業服務，具備還原前強制安全快照防護與阻斷機制。
///
/// 工具執行、環境變數、計時、成敗判定與紀錄寫入全數委由客戶端工具作業，
/// 此處只保留還原專屬的責任：工具挑選、格式推斷、計畫產出與驗證、暫存清單檔的
/// 生命週期、安全快照協調與結果轉換。
/// </summary>
public class RestoreService : IRestoreService
{
    private readonly ClientToolRun _clientToolRun;
    private readonly IToolDetectionService _toolDetector;
    private readonly IBackupService? _backupService;
    private readonly IRestoreTargetCatalogReader _catalogReader;
    private readonly IRestoreDataPreparationService _dataPreparationService;

    public RestoreService(
        ClientToolRun clientToolRun,
        IToolDetectionService toolDetector,
        IBackupService? backupService = null,
        IRestoreTargetCatalogReader? catalogReader = null,
        IRestoreDataPreparationService? dataPreparationService = null)
    {
        _clientToolRun = clientToolRun;
        _toolDetector = toolDetector;
        _backupService = backupService;
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
            // 封存目錄的讀取是唯讀探查：沿用同一套環境變數與拼接規則，但不留下紀錄。
            var listResult = await _clientToolRun.ProbeAsync(
                toolExecutablePath,
                ["--list", options.SourceFilePath],
                options.Connection,
                "Restore_Error_NonZeroExit",
                ct);

            if (!listResult.IsSuccess)
            {
                // 失敗結果的 ErrorMessage 恆為非 null（ExtractErrorMessage 至少會回傳資源字串），
                // 但該不變式只由 IsSuccess 承載，編譯器看不到。
                var errMsg = CoreStrings.Format(
                    "Restore_Error_ArchiveListFailed", listResult.ErrorMessage ?? string.Empty);
                onLogLine?.Invoke($"[ERROR] {errMsg}");
                return RestoreResult.Failure(
                    errMsg, listResult.ExitCode, listResult.Elapsed, listResult.CommandLine);
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
                        return RestoreResult.Failure(errMsg, -1, listResult.Elapsed, listResult.CommandLine);
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
                        return RestoreResult.Failure(errMsg, -1, listResult.Elapsed, listResult.CommandLine);
                    }

                    if (dataPlan.MissingObjects.Count > 0)
                    {
                        var objects = string.Join(", ", dataPlan.MissingObjects);
                        var errMsg = CoreStrings.Format("Restore_Error_DataTargetsMissing", objects);
                        onLogLine?.Invoke($"[ERROR] {errMsg}");
                        return RestoreResult.Failure(errMsg, -1, listResult.Elapsed, listResult.CommandLine);
                    }

                    if (dataPlan.CyclicTables.Count > 0)
                    {
                        var tables = string.Join(", ", dataPlan.CyclicTables);
                        var errMsg = CoreStrings.Format("Restore_Error_DataDependencyCycle", tables);
                        onLogLine?.Invoke($"[ERROR] {errMsg}");
                        return RestoreResult.Failure(errMsg, -1, listResult.Elapsed, listResult.CommandLine);
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
                return RestoreResult.Failure(errMsg, -1, listResult.Elapsed, listResult.CommandLine);
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
                return RestoreResult.Failure(errMsg, -1, TimeSpan.Zero, string.Empty);
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
                return RestoreResult.Failure(errMsg, snapshotResult.ExitCode, snapshotResult.Duration, snapshotResult.Arguments);
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
                return RestoreResult.Failure(errMsg, -1, TimeSpan.Zero, string.Empty, snapshotFilePath);
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
                return RestoreResult.Failure(errMsg, -1, TimeSpan.Zero, string.Empty, snapshotFilePath);
            }
        }

        // 4. 執行還原作業
        var restoreArgs = RestoreArgumentsBuilder.Build(options, restoreListFilePath);
        var toolNameDisplay = Path.GetFileNameWithoutExtension(toolExecutablePath);

        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Format("Restore_Log_Start", toolNameDisplay, targetDb)}");
        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Format("Restore_Log_SourceFile", options.SourceFilePath)}");
        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] {CoreStrings.Format("Restore_Log_Mode", options.Mode)}");

        ClientToolRunResult run;
        try
        {
            run = await _clientToolRun.RunAsync(
                new ClientToolRunRequest
                {
                    ExecutablePath = toolExecutablePath,
                    Arguments = restoreArgs,
                    Connection = options.Connection,
                    TargetDatabase = targetDb,
                    LogPrefix = toolNameDisplay,
                    NonZeroExitErrorKey = "Restore_Error_NonZeroExit",
                    OperationType = BackupOperationType.Restore,
                    RecordedFilePath = options.SourceFilePath,
                    RecordedFormat = options.Format,
                    // 還原紀錄的檔案大小是來源檔，不是產出檔，成敗兩種情形皆然。
                    MeasureRecordedFileSize = _ => new FileInfo(options.SourceFilePath).Length
                },
                onLogLine,
                ct);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(restoreListFilePath))
            {
                DeleteRestoreListFile(restoreListFilePath);
            }
        }

        // 5. 轉換為還原作業結果
        if (run.IsSuccess)
        {
            onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [SUCCESS] {CoreStrings.Format("Restore_Log_Success", CoreStrings.Format("Format_DurationSeconds", run.Elapsed.TotalSeconds.ToString("F2")))}");
            return RestoreResult.Success(run.Elapsed, run.CommandLine, snapshotFilePath);
        }

        onLogLine?.Invoke($"[{DateTime.Now:HH:mm:ss}] [ERROR] {CoreStrings.Format("Restore_Log_Failed", run.ExitCode, run.ErrorMessage ?? string.Empty)}");
        return RestoreResult.Failure(
            run.ErrorMessage, run.ExitCode, run.Elapsed, run.CommandLine, snapshotFilePath);
    }

    private static void DeleteRestoreListFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        try { File.Delete(path); } catch { }
    }
}
