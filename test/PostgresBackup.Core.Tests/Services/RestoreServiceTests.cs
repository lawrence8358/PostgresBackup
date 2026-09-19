using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

public class RestoreServiceTests
{
    private class TestProcessRunner : IProcessRunner
    {
        public bool FailSnapshot { get; set; }
        public bool FailRestore { get; set; }
        public string ArchiveListOutput { get; set; } = string.Empty;
        public string? CapturedRestoreListContent { get; private set; }
        public string? CapturedRestoreListPath { get; private set; }
        public int CallCount { get; private set; }
        public List<string> ExecutablesCalled { get; } = [];
        public List<string> ArgumentsCalled { get; } = [];
        public List<IReadOnlyDictionary<string, string?>> CapturedEnvironmentVariables { get; } = [];

        public Task<ProcessResult> RunAsync(
            string executable,
            string arguments,
            IDictionary<string, string?>? environmentVariables = null,
            Action<string>? onOutputLine = null,
            Action<string>? onErrorLine = null,
            CancellationToken ct = default)
        {
            CallCount++;
            ExecutablesCalled.Add(executable);
            ArgumentsCalled.Add(arguments);
            CapturedEnvironmentVariables.Add(
                environmentVariables?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                ?? new Dictionary<string, string?>());

            // 判斷是否為快照 (pg_dump)
            if (executable.Contains("pg_dump", StringComparison.OrdinalIgnoreCase))
            {
                if (FailSnapshot)
                {
                    return Task.FromResult(new ProcessResult(1, string.Empty, "Snapshot dump crashed"));
                }
                // 模擬建立快照檔案
                var fileArgIndex = arguments.IndexOf("-f \"", StringComparison.Ordinal);
                if (fileArgIndex >= 0)
                {
                    var pathStart = fileArgIndex + 4;
                    var pathEnd = arguments.IndexOf("\"", pathStart, StringComparison.Ordinal);
                    if (pathEnd > pathStart)
                    {
                        var path = arguments.Substring(pathStart, pathEnd - pathStart);
                        File.WriteAllText(path, "MOCK SNAPSHOT");
                    }
                }
                return Task.FromResult(new ProcessResult(0, "Snapshot ok", string.Empty));
            }

            if (executable.Contains("pg_restore", StringComparison.OrdinalIgnoreCase)
                && arguments.StartsWith("--list ", StringComparison.Ordinal))
            {
                return Task.FromResult(new ProcessResult(0, ArchiveListOutput, string.Empty));
            }

            if (executable.Contains("pg_restore", StringComparison.OrdinalIgnoreCase))
            {
                const string marker = "--use-list \"";
                var listStart = arguments.IndexOf(marker, StringComparison.Ordinal);
                if (listStart >= 0)
                {
                    listStart += marker.Length;
                    var listEnd = arguments.IndexOf('"', listStart);
                    CapturedRestoreListPath = arguments[listStart..listEnd];
                    CapturedRestoreListContent = File.ReadAllText(CapturedRestoreListPath);
                }
            }

            // 還原作業 (pg_restore or psql)
            if (FailRestore)
            {
                return Task.FromResult(new ProcessResult(1, string.Empty, "Restore execution failed"));
            }

            return Task.FromResult(new ProcessResult(0, "Restore completed successfully", string.Empty));
        }
    }

    private sealed class TestCatalogReader(RestoreTargetCatalog catalog) : IRestoreTargetCatalogReader
    {
        public Task<RestoreTargetCatalog> ReadAsync(
            ConnectionSettings connection,
            string targetDatabase,
            CancellationToken ct = default) => Task.FromResult(catalog);
    }

    private sealed class TestDataPreparationService(TestProcessRunner runner) : IRestoreDataPreparationService
    {
        public IReadOnlyList<RestoreTableIdentity>? ClearedTables { get; private set; }
        public int ProcessCallCountWhenInvoked { get; private set; }

        public Task ClearTablesAsync(
            ConnectionSettings connection,
            string targetDatabase,
            IReadOnlyList<RestoreTableIdentity> tables,
            CancellationToken ct = default)
        {
            ProcessCallCountWhenInvoked = runner.CallCount;
            ClearedTables = tables.ToArray();
            return Task.CompletedTask;
        }
    }

    private class TestToolDetector : IToolDetectionService
    {
        public Task<ToolDetectionResult> DetectAsync(string? customPath = null, CancellationToken ct = default) =>
            Task.FromResult(ToolDetectionResult.CreateFound(
                @"C:\pg\bin\pg_dump.exe",
                @"C:\pg\bin\pg_restore.exe",
                @"C:\pg\bin\psql.exe",
                new ToolVersion(16, 0),
                DetectionSource.CommonDirectory));

        public Task<ConnectionCheckResult> VerifyConnectionAsync(string connectionString, CancellationToken ct = default) =>
            Task.FromResult(ConnectionCheckResult.Success("16.2", 16));

        public Task<VersionCheckResult> CheckCompatibilityAsync(ToolDetectionResult clientTools, string connectionString, CancellationToken ct = default) =>
            Task.FromResult(VersionCheckResult.Compatible(clientTools.Version!, 16));

        public VersionCheckResult CheckCompatibility(ToolVersion clientVersion, int serverMajorVersion, string? serverVersionString = null) =>
            VersionCheckResult.Compatible(clientVersion, serverMajorVersion, serverVersionString);
    }

    [Fact]
    public async Task RestoreAsync_WhenSnapshotFails_AbortsRestoreImmediately()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"pg_restore_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var sourceDumpFile = Path.Combine(tempDir, "source.dump");
        File.WriteAllText(sourceDumpFile, "MOCK DUMP");

        try
        {
            var runner = new TestProcessRunner { FailSnapshot = true };
            var detector = new TestToolDetector();
            var historyRepo = new SqliteBackupHistoryRepository(":memory:");
            var backupService = new BackupService(runner, detector, historyRepo);
            var service = new RestoreService(runner, detector, backupService, historyRepo);

            var options = new RestoreOptions
            {
                Connection = new ConnectionSettings { Database = "target_db" },
                SourceFilePath = sourceDumpFile,
                TargetDatabase = "target_db",
                Mode = RestoreMode.CleanAndRecreate,
                CreatePreRestoreSnapshot = true,
                SnapshotDirectory = tempDir
            };

            var logs = new List<string>();
            var result = await service.RestoreAsync(options, onLogLine: s => logs.Add(s));

            // 驗證快照失敗時還原作業被終止
            Assert.False(result.IsSuccess);
            Assert.Contains("aborted to protect", result.ErrorMessage);
            Assert.Equal(1, runner.CallCount); // 僅呼叫了 pg_dump，絕無呼叫 pg_restore
            Assert.DoesNotContain(runner.ExecutablesCalled, e => e.Contains("pg_restore"));

            // 驗證紀錄了失敗的 PreRestoreSnapshot
            var records = await historyRepo.GetRecordsAsync();
            Assert.Single(records);
            Assert.Equal(BackupOperationType.PreRestoreSnapshot, records[0].OperationType);
            Assert.Equal(BackupStatus.Failed, records[0].Status);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task RestoreAsync_WhenSnapshotSucceeds_ExecutesRestoreAndRecordsBoth()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"pg_restore_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var sourceDumpFile = Path.Combine(tempDir, "source.dump");
        File.WriteAllText(sourceDumpFile, "MOCK DUMP");

        try
        {
            var runner = new TestProcessRunner { FailSnapshot = false, FailRestore = false };
            var detector = new TestToolDetector();
            var historyRepo = new SqliteBackupHistoryRepository(":memory:");
            var backupService = new BackupService(runner, detector, historyRepo);
            var service = new RestoreService(runner, detector, backupService, historyRepo);

            var options = new RestoreOptions
            {
                Connection = new ConnectionSettings { Database = "target_db" },
                SourceFilePath = sourceDumpFile,
                TargetDatabase = "target_db",
                Mode = RestoreMode.CleanAndRecreate,
                CreatePreRestoreSnapshot = true,
                SnapshotDirectory = tempDir
            };

            var result = await service.RestoreAsync(options);

            Assert.True(result.IsSuccess);
            Assert.True(result.SnapshotCreated);
            Assert.Equal(2, runner.CapturedEnvironmentVariables.Count);
            Assert.All(runner.CapturedEnvironmentVariables, environment =>
            {
                Assert.Equal("UTF8", environment["PGCLIENTENCODING"]);
                Assert.Equal("C", environment["LC_ALL"]);
                Assert.Equal("C", environment["LC_MESSAGES"]);
                Assert.Equal("C", environment["LANG"]);
                Assert.Null(environment["LANGUAGE"]);
            });
            Assert.Equal(2, runner.CallCount); // 呼叫了 pg_dump，接著呼叫 pg_restore

            var records = await historyRepo.GetRecordsAsync();
            Assert.Equal(2, records.Count);
            Assert.Contains(records, r => r.OperationType == BackupOperationType.PreRestoreSnapshot && r.Status == BackupStatus.Success);
            Assert.Contains(records, r => r.OperationType == BackupOperationType.Restore && r.Status == BackupStatus.Success);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task RestoreAsync_NormalModeBuildsAndUsesFilteredArchiveList()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"pg_restore_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var sourceDumpFile = Path.Combine(tempDir, "source.dump");
        File.WriteAllText(sourceDumpFile, "MOCK DUMP");

        try
        {
            var runner = new TestProcessRunner
            {
                ArchiveListOutput = """
                    1; 2615 100 SCHEMA - public owner
                    2; 1259 101 TABLE public ExistingTable owner
                    3; 0 101 TABLE DATA public ExistingTable owner
                    4; 1259 102 TABLE public MissingTable owner
                    5; 0 102 TABLE DATA public MissingTable owner
                    """
            };
            var catalog = new RestoreTargetCatalog();
            catalog.Schemas.Add("public");
            catalog.Relations.Add(RestoreTargetCatalog.Key("public", "ExistingTable"));
            var service = new RestoreService(
                runner,
                new TestToolDetector(),
                catalogReader: new TestCatalogReader(catalog));
            var options = new RestoreOptions
            {
                Connection = new ConnectionSettings { Database = "target_db" },
                SourceFilePath = sourceDumpFile,
                TargetDatabase = "target_db",
                Mode = RestoreMode.Normal,
                CreatePreRestoreSnapshot = false
            };

            var result = await service.RestoreAsync(options);

            Assert.True(result.IsSuccess);
            Assert.Equal(2, runner.CallCount);
            Assert.StartsWith("--list ", runner.ArgumentsCalled[0]);
            Assert.Contains("--use-list", runner.ArgumentsCalled[1]);
            Assert.Contains("--no-data-for-failed-tables", runner.ArgumentsCalled[1]);
            Assert.Contains("; skipped-existing: 2; 1259 101 TABLE public ExistingTable owner", runner.CapturedRestoreListContent);
            Assert.Contains("; skipped-existing: 3; 0 101 TABLE DATA public ExistingTable owner", runner.CapturedRestoreListContent);
            Assert.Contains("4; 1259 102 TABLE public MissingTable owner", runner.CapturedRestoreListContent);
            Assert.Contains("5; 0 102 TABLE DATA public MissingTable owner", runner.CapturedRestoreListContent);
            Assert.NotNull(runner.CapturedRestoreListPath);
            Assert.False(File.Exists(runner.CapturedRestoreListPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task RestoreAsync_NormalModeWithNothingMissing_SkipsSafetySnapshot()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"pg_restore_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var sourceDumpFile = Path.Combine(tempDir, "source.dump");
        File.WriteAllText(sourceDumpFile, "MOCK DUMP");

        try
        {
            var runner = new TestProcessRunner
            {
                ArchiveListOutput = """
                    1; 2615 100 SCHEMA - public owner
                    2; 1259 101 TABLE public ExistingTable owner
                    3; 0 101 TABLE DATA public ExistingTable owner
                    """
            };
            var catalog = new RestoreTargetCatalog();
            catalog.Schemas.Add("public");
            catalog.Relations.Add(RestoreTargetCatalog.Key("public", "ExistingTable"));
            var service = new RestoreService(
                runner,
                new TestToolDetector(),
                catalogReader: new TestCatalogReader(catalog));
            var options = new RestoreOptions
            {
                Connection = new ConnectionSettings { Database = "target_db" },
                SourceFilePath = sourceDumpFile,
                TargetDatabase = "target_db",
                Mode = RestoreMode.Normal,
                CreatePreRestoreSnapshot = true,
                SnapshotDirectory = tempDir
            };

            var result = await service.RestoreAsync(options);

            Assert.True(result.IsSuccess);
            Assert.DoesNotContain(runner.ExecutablesCalled, executable =>
                executable.Contains("pg_dump", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(2, runner.CallCount);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task RestoreAsync_NormalModeWithUnsupportedEntry_StopsBeforeSnapshotOrRestore()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"pg_restore_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var sourceDumpFile = Path.Combine(tempDir, "source.dump");
        File.WriteAllText(sourceDumpFile, "MOCK DUMP");

        try
        {
            var runner = new TestProcessRunner
            {
                ArchiveListOutput = "10; 0 100 POLICY public ExistingTable tenant_policy owner"
            };
            var service = new RestoreService(
                runner,
                new TestToolDetector(),
                catalogReader: new TestCatalogReader(new RestoreTargetCatalog()));
            var options = new RestoreOptions
            {
                Connection = new ConnectionSettings { Database = "target_db" },
                SourceFilePath = sourceDumpFile,
                TargetDatabase = "target_db",
                Mode = RestoreMode.Normal,
                CreatePreRestoreSnapshot = true,
                SnapshotDirectory = tempDir
            };

            var result = await service.RestoreAsync(options);

            Assert.False(result.IsSuccess);
            Assert.Contains("POLICY", result.ErrorMessage);
            Assert.Single(runner.ExecutablesCalled);
            Assert.StartsWith("--list ", runner.ArgumentsCalled[0]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task RestoreAsync_DataOnlyInspectsArchiveBeforeWritingData()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"pg_restore_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var sourceDumpFile = Path.Combine(tempDir, "source.dump");
        File.WriteAllText(sourceDumpFile, "MOCK DUMP");

        try
        {
            var runner = new TestProcessRunner
            {
                ArchiveListOutput = """
                    10; 0 100 TABLE DATA public ChildTable owner
                    11; 0 101 TABLE DATA public ParentTable owner
                    """
            };
            var catalog = new RestoreTargetCatalog();
            var child = new RestoreTableIdentity("public", "ChildTable");
            var parent = new RestoreTableIdentity("public", "ParentTable");
            catalog.Relations.Add(RestoreTargetCatalog.Key(child.Schema, child.Name));
            catalog.Relations.Add(RestoreTargetCatalog.Key(parent.Schema, parent.Name));
            catalog.ForeignKeyDependencies.Add(new RestoreForeignKeyDependency(child, parent));
            var dataPreparation = new TestDataPreparationService(runner);
            var detector = new TestToolDetector();
            var backupService = new BackupService(runner, detector);
            var service = new RestoreService(
                runner,
                detector,
                backupService,
                catalogReader: new TestCatalogReader(catalog),
                dataPreparationService: dataPreparation);
            var options = new RestoreOptions
            {
                Connection = new ConnectionSettings { Database = "target_db" },
                SourceFilePath = sourceDumpFile,
                TargetDatabase = "target_db",
                Mode = RestoreMode.DataOnly,
                CreatePreRestoreSnapshot = true,
                SnapshotDirectory = tempDir
            };

            var result = await service.RestoreAsync(options);

            Assert.True(result.IsSuccess);
            Assert.Equal(3, runner.CallCount);
            Assert.StartsWith("--list ", runner.ArgumentsCalled[0]);
            Assert.Contains("pg_dump", runner.ExecutablesCalled[1], StringComparison.OrdinalIgnoreCase);
            Assert.Equal(2, dataPreparation.ProcessCallCountWhenInvoked);
            Assert.Equal([parent, child], dataPreparation.ClearedTables);
            Assert.Contains("--data-only", runner.ArgumentsCalled[2]);
            Assert.Contains("--use-list", runner.ArgumentsCalled[2]);
            Assert.Contains("--exit-on-error", runner.ArgumentsCalled[2]);
            Assert.True(
                runner.CapturedRestoreListContent!.IndexOf("TABLE DATA public ParentTable", StringComparison.Ordinal)
                < runner.CapturedRestoreListContent.IndexOf("TABLE DATA public ChildTable", StringComparison.Ordinal));
            Assert.NotNull(runner.CapturedRestoreListPath);
            Assert.False(File.Exists(runner.CapturedRestoreListPath));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task RestoreAsync_DataOnlyWithMissingTargetTable_StopsBeforeSnapshotOrDataChanges()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"pg_restore_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var sourceDumpFile = Path.Combine(tempDir, "source.dump");
        File.WriteAllText(sourceDumpFile, "MOCK DUMP");

        try
        {
            var runner = new TestProcessRunner
            {
                ArchiveListOutput = "10; 0 100 TABLE DATA public MissingTable owner"
            };
            var dataPreparation = new TestDataPreparationService(runner);
            var service = new RestoreService(
                runner,
                new TestToolDetector(),
                catalogReader: new TestCatalogReader(new RestoreTargetCatalog()),
                dataPreparationService: dataPreparation);
            var options = new RestoreOptions
            {
                Connection = new ConnectionSettings { Database = "target_db" },
                SourceFilePath = sourceDumpFile,
                TargetDatabase = "target_db",
                Mode = RestoreMode.DataOnly,
                CreatePreRestoreSnapshot = true,
                SnapshotDirectory = tempDir
            };

            var result = await service.RestoreAsync(options);

            Assert.False(result.IsSuccess);
            Assert.Contains("public.MissingTable", result.ErrorMessage);
            Assert.Single(runner.ExecutablesCalled);
            Assert.Null(dataPreparation.ClearedTables);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    [Fact]
    public async Task RestoreAsync_DataOnlyWithForeignKeyCycle_StopsBeforeSnapshotOrDataChanges()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"pg_restore_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var sourceDumpFile = Path.Combine(tempDir, "source.dump");
        File.WriteAllText(sourceDumpFile, "MOCK DUMP");

        try
        {
            var runner = new TestProcessRunner
            {
                ArchiveListOutput = """
                    10; 0 100 TABLE DATA public Alpha owner
                    11; 0 101 TABLE DATA public Beta owner
                    """
            };
            var alpha = new RestoreTableIdentity("public", "Alpha");
            var beta = new RestoreTableIdentity("public", "Beta");
            var catalog = new RestoreTargetCatalog();
            catalog.Relations.Add(RestoreTargetCatalog.Key(alpha.Schema, alpha.Name));
            catalog.Relations.Add(RestoreTargetCatalog.Key(beta.Schema, beta.Name));
            catalog.ForeignKeyDependencies.Add(new RestoreForeignKeyDependency(alpha, beta));
            catalog.ForeignKeyDependencies.Add(new RestoreForeignKeyDependency(beta, alpha));
            var dataPreparation = new TestDataPreparationService(runner);
            var service = new RestoreService(
                runner,
                new TestToolDetector(),
                catalogReader: new TestCatalogReader(catalog),
                dataPreparationService: dataPreparation);
            var options = new RestoreOptions
            {
                Connection = new ConnectionSettings { Database = "target_db" },
                SourceFilePath = sourceDumpFile,
                TargetDatabase = "target_db",
                Mode = RestoreMode.DataOnly,
                CreatePreRestoreSnapshot = true,
                SnapshotDirectory = tempDir
            };

            var result = await service.RestoreAsync(options);

            Assert.False(result.IsSuccess);
            Assert.Contains("public.Alpha", result.ErrorMessage);
            Assert.Contains("public.Beta", result.ErrorMessage);
            Assert.Single(runner.ExecutablesCalled);
            Assert.Null(dataPreparation.ClearedTables);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
