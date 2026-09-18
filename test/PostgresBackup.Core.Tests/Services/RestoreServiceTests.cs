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
        public int CallCount { get; private set; }
        public List<string> ExecutablesCalled { get; } = [];

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

            // 還原作業 (pg_restore or psql)
            if (FailRestore)
            {
                return Task.FromResult(new ProcessResult(1, string.Empty, "Restore execution failed"));
            }

            return Task.FromResult(new ProcessResult(0, "Restore completed successfully", string.Empty));
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
                CreatePreRestoreSnapshot = true,
                SnapshotDirectory = tempDir
            };

            var logs = new List<string>();
            var result = await service.RestoreAsync(options, onLogLine: s => logs.Add(s));

            // 驗證快照失敗時還原作業被終止
            Assert.False(result.IsSuccess);
            Assert.Contains("強制終止", result.ErrorMessage);
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
                CreatePreRestoreSnapshot = true,
                SnapshotDirectory = tempDir
            };

            var result = await service.RestoreAsync(options);

            Assert.True(result.IsSuccess);
            Assert.True(result.SnapshotCreated);
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
}
