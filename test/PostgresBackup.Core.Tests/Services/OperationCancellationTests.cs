using Moq;
using System.Text.RegularExpressions;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

public class OperationCancellationTests
{
    private static IToolDetectionService Tools()
    {
        var tools = new Mock<IToolDetectionService>();
        tools.Setup(t => t.DetectAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolDetectionResult.CreateFound("pg_dump.exe", "pg_restore.exe", "psql.exe",
                new ToolVersion(18, 0), DetectionSource.CustomPath));
        return tools.Object;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cancelled_restore_reports_the_target_and_available_safety_snapshot(bool createSnapshot)
    {
        var directory = Path.Combine(Environment.CurrentDirectory, "TestResults", $"restore-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "source.dump");
        await File.WriteAllTextAsync(file, "source");
        string? snapshot = null;
        using var cancellation = new CancellationTokenSource();
        await using var history = new SqliteBackupHistoryRepository(":memory:");
        var runner = new Mock<IProcessRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IDictionary<string, string?>?>(), It.IsAny<Action<string>?>(),
                It.IsAny<Action<string>?>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, IDictionary<string, string?>?, Action<string>?, Action<string>?, CancellationToken>(
                async (executable, arguments, _, _, _, ct) =>
                {
                    if (executable == "pg_dump.exe")
                    {
                        var match = Regex.Match(arguments, "-f (?:\"([^\"]+)\"|(\\S+))");
                        snapshot = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                        await File.WriteAllTextAsync(snapshot, "snapshot");
                        return new ProcessResult(0, "", "");
                    }
                    cancellation.Cancel();
                    ct.ThrowIfCancellationRequested();
                    return new ProcessResult(0, "", "");
                });
        var run = new ClientToolRun(runner.Object, history);
        var tools = Tools();
        var restore = new RestoreService(run, tools, new BackupService(run, tools));
        try
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restore.RestoreAsync(new RestoreOptions
            {
                Connection = new ConnectionSettings { Database = "target_test" },
                TargetDatabase = "target_test",
                SourceFilePath = file,
                SnapshotDirectory = Path.GetRelativePath(Environment.CurrentDirectory, directory),
                Mode = RestoreMode.CleanAndRecreate,
                CreatePreRestoreSnapshot = createSnapshot
            }, ct: cancellation.Token));

            Assert.Contains("target_test", error.Message);
            if (createSnapshot)
            {
                Assert.NotNull(snapshot);
                Assert.Contains(Path.GetFullPath(snapshot), error.Message);
                Assert.True(File.Exists(snapshot));
            }
            var records = await history.GetRecordsAsync();
            Assert.Single(records, r => r.OperationType == BackupOperationType.Restore && r.Status == BackupStatus.Cancelled);
            Assert.Equal(createSnapshot ? 2 : 1, records.Count);
            Assert.Equal("source", await File.ReadAllTextAsync(file));
        }
        finally
        {
            if (snapshot is not null) File.Delete(snapshot);
            File.Delete(file);
            Directory.Delete(directory);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelled_backup_retains_partial_file_and_persists_cancelled_record(bool relativeOutput)
    {
        var directory = Path.Combine(relativeOutput ? Path.Combine(Environment.CurrentDirectory, "TestResults") : Path.GetTempPath(),
            $"backup-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "partial.dump");
        using var cancellation = new CancellationTokenSource();
        await using var history = new SqliteBackupHistoryRepository(":memory:");
        var runner = new Mock<IProcessRunner>();
        runner.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<IDictionary<string, string?>?>(), It.IsAny<Action<string>?>(),
                It.IsAny<Action<string>?>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, IDictionary<string, string?>?, Action<string>?, Action<string>?, CancellationToken>(
                async (_, _, _, _, _, ct) =>
                {
                    await File.WriteAllTextAsync(file, "incomplete");
                    cancellation.Cancel();
                    ct.ThrowIfCancellationRequested();
                    return new ProcessResult(0, "", "");
                });
        var backup = new BackupService(new ClientToolRun(runner.Object, history), Tools());
        try
        {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => backup.BackupAsync(new BackupOptions
            {
                Connection = new ConnectionSettings { Database = "test" },
                OutputDirectory = relativeOutput ? Path.GetRelativePath(Environment.CurrentDirectory, directory) : directory,
                CustomFileName = "partial.dump"
            }, ct: cancellation.Token));

            Assert.Contains(file, error.Message);
            Assert.Equal("incomplete", await File.ReadAllTextAsync(file));
            var record = Assert.Single(await history.GetRecordsAsync());
            Assert.Equal(BackupStatus.Cancelled, record.Status);
            Assert.Equal(file, record.FilePath);
        }
        finally
        {
            File.Delete(file);
            Directory.Delete(directory);
        }
    }
}
