using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

public class BackupServiceTests
{
    private class FakeProcessRunner : IProcessRunner
    {
        public ProcessResult ResultToReturn { get; set; } = new(0, "Export completed", string.Empty);
        public string? CapturedArguments { get; private set; }
        public string? CapturedPassword { get; private set; }
        public string? CapturedClientEncoding { get; private set; }
        public string? CapturedLocaleAll { get; private set; }
        public string? CapturedLocaleMessages { get; private set; }
        public string? CapturedLanguage { get; private set; }
        public bool LanguageVariableRemoved { get; private set; }
        public bool CreateFileOnRun { get; set; } = true;
        public string? FilePathToCreate { get; set; }

        public Task<ProcessResult> RunAsync(
            string executable,
            string arguments,
            IDictionary<string, string?>? environmentVariables = null,
            Action<string>? onOutputLine = null,
            Action<string>? onErrorLine = null,
            CancellationToken ct = default)
        {
            CapturedArguments = arguments;
            if (environmentVariables != null && environmentVariables.TryGetValue("PGPASSWORD", out var pass))
            {
                CapturedPassword = pass;
            }
            if (environmentVariables != null && environmentVariables.TryGetValue("PGCLIENTENCODING", out var encoding))
            {
                CapturedClientEncoding = encoding;
            }
            if (environmentVariables != null && environmentVariables.TryGetValue("LC_ALL", out var localeAll))
            {
                CapturedLocaleAll = localeAll;
            }
            if (environmentVariables != null && environmentVariables.TryGetValue("LC_MESSAGES", out var localeMessages))
            {
                CapturedLocaleMessages = localeMessages;
            }
            if (environmentVariables != null && environmentVariables.TryGetValue("LANG", out var language))
            {
                CapturedLanguage = language;
            }
            if (environmentVariables != null && environmentVariables.TryGetValue("LANGUAGE", out var removedLanguage))
            {
                LanguageVariableRemoved = removedLanguage is null;
            }

            onOutputLine?.Invoke("pg_dump: reading schemas");
            onErrorLine?.Invoke("pg_dump: dumping contents");

            if (CreateFileOnRun && !string.IsNullOrEmpty(FilePathToCreate))
            {
                File.WriteAllText(FilePathToCreate, "MOCK DUMP CONTENT");
            }

            return Task.FromResult(ResultToReturn);
        }
    }

    private class FakeToolDetector : IToolDetectionService
    {
        public ToolDetectionResult ResultToReturn { get; set; } = ToolDetectionResult.CreateFound(
            @"C:\Program Files\PostgreSQL\16\bin\pg_dump.exe",
            @"C:\Program Files\PostgreSQL\16\bin\pg_restore.exe",
            @"C:\Program Files\PostgreSQL\16\bin\psql.exe",
            ToolVersion.Parse("PostgreSQL 16.2"),
            DetectionSource.CommonDirectory);

        public Task<ToolDetectionResult> DetectAsync(string? customPath = null, CancellationToken ct = default) =>
            Task.FromResult(ResultToReturn);

        public Task<ConnectionCheckResult> VerifyConnectionAsync(string connectionString, CancellationToken ct = default) =>
            Task.FromResult(ConnectionCheckResult.Success("16.2", 16));

        public Task<VersionCheckResult> CheckCompatibilityAsync(ToolDetectionResult clientTools, string connectionString, CancellationToken ct = default) =>
            Task.FromResult(VersionCheckResult.Compatible(clientTools.Version!, 16));

        public VersionCheckResult CheckCompatibility(ToolVersion clientVersion, int serverMajorVersion, string? serverVersionString = null) =>
            VersionCheckResult.Compatible(clientVersion, serverMajorVersion, serverVersionString);
    }

    [Fact]
    public async Task BackupAsync_WhenSuccessful_ReturnsSuccessAndFileDetails()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"pg_backup_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var fakeRunner = new FakeProcessRunner();
            var fakeDetector = new FakeToolDetector();
            var service = new BackupService(new ClientToolRun(fakeRunner), fakeDetector);

            var options = new BackupOptions
            {
                Connection = new ConnectionSettings
                {
                    Host = "localhost",
                    Database = "testdb",
                    Password = "mypassword"
                },
                OutputDirectory = tempDir,
                CustomFileName = "test_backup.dump"
            };

            fakeRunner.FilePathToCreate = Path.Combine(tempDir, "test_backup.dump");

            var logs = new List<string>();
            var result = await service.BackupAsync(options, onLogLine: s => logs.Add(s));

            Assert.True(result.IsSuccess);
            Assert.Equal(fakeRunner.FilePathToCreate, result.OutputFilePath);
            Assert.True(result.FileSizeBytes > 0);
            Assert.Contains(logs, l => l.Contains("pg_dump: dumping contents"));
            Assert.Equal("mypassword", fakeRunner.CapturedPassword);
            Assert.Equal("UTF8", fakeRunner.CapturedClientEncoding);
            Assert.Equal("C", fakeRunner.CapturedLocaleAll);
            Assert.Equal("C", fakeRunner.CapturedLocaleMessages);
            Assert.Equal("C", fakeRunner.CapturedLanguage);
            Assert.True(fakeRunner.LanguageVariableRemoved);
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
    public async Task BackupAsync_WhenProcessFails_ReturnsFailure()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"pg_backup_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var fakeRunner = new FakeProcessRunner
            {
                ResultToReturn = new ProcessResult(1, string.Empty, "FATAL: database 'unknown_db' does not exist"),
                CreateFileOnRun = false
            };
            var fakeDetector = new FakeToolDetector();
            var service = new BackupService(new ClientToolRun(fakeRunner), fakeDetector);

            var options = new BackupOptions
            {
                Connection = new ConnectionSettings { Database = "unknown_db" },
                OutputDirectory = tempDir
            };

            var result = await service.BackupAsync(options);

            Assert.False(result.IsSuccess);
            Assert.Equal(1, result.ExitCode);
            Assert.Contains("FATAL", result.ErrorMessage);
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
    public async Task BackupAsync_WhenToolNotFound_ReturnsFailureWithoutRunning()
    {
        var fakeRunner = new FakeProcessRunner();
        var fakeDetector = new FakeToolDetector { ResultToReturn = ToolDetectionResult.CreateNotFound() };
        var service = new BackupService(new ClientToolRun(fakeRunner), fakeDetector);

        var options = new BackupOptions();
        var result = await service.BackupAsync(options);

        Assert.False(result.IsSuccess);
        Assert.Contains("were not detected", result.ErrorMessage);
    }
}
