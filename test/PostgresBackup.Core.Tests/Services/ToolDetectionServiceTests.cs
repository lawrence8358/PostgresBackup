using Moq;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;
using Xunit;

namespace PostgresBackup.Core.Tests.Services;

public class ToolDetectionServiceTests
{
    private readonly Mock<IProcessRunner> _mockRunner;
    private readonly Mock<IEnvironmentProbe> _mockProbe;
    private readonly ToolDetectionService _service;

    public ToolDetectionServiceTests()
    {
        _mockRunner = new Mock<IProcessRunner>();
        _mockProbe = new Mock<IEnvironmentProbe>();
        _service = new ToolDetectionService(_mockRunner.Object, _mockProbe.Object);
    }

    [Fact]
    public async Task DetectAsync_WhenCustomPathIsValid_UsesCustomPath()
    {
        string customDir = @"C:\CustomTools\PostgreSQL\bin";
        string dumpPath = Path.Combine(customDir, "pg_dump.exe");
        string restorePath = Path.Combine(customDir, "pg_restore.exe");
        string psqlPath = Path.Combine(customDir, "psql.exe");

        _mockProbe.Setup(p => p.DirectoryExists(customDir)).Returns(true);
        _mockProbe.Setup(p => p.FileExists(dumpPath)).Returns(true);
        _mockProbe.Setup(p => p.FileExists(restorePath)).Returns(true);
        _mockProbe.Setup(p => p.FileExists(psqlPath)).Returns(true);

        _mockRunner.Setup(r => r.RunAsync(dumpPath, "--version", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(0, "pg_dump (PostgreSQL) 16.4", ""));

        var result = await _service.DetectAsync(customDir);

        Assert.Equal(ToolStatus.Ready, result.Status);
        Assert.Equal(DetectionSource.CustomPath, result.Source);
        Assert.Equal(dumpPath, result.PgDumpPath);
        Assert.Equal(restorePath, result.PgRestorePath);
        Assert.Equal(psqlPath, result.PsqlPath);
        Assert.NotNull(result.Version);
        Assert.Equal(16, result.Version.Major);
        Assert.Equal(4, result.Version.Minor);
    }

    [Fact]
    public async Task DetectAsync_WhenCustomPathInvalid_FallsBackToPath()
    {
        string customDir = @"C:\NonExistent\bin";
        _mockProbe.Setup(p => p.DirectoryExists(customDir)).Returns(false);

        string pathDir = @"C:\PostgreSQL\bin";
        string dumpPath = Path.Combine(pathDir, "pg_dump.exe");
        string restorePath = Path.Combine(pathDir, "pg_restore.exe");

        _mockProbe.Setup(p => p.GetPathEntries()).Returns([pathDir]);
        _mockProbe.Setup(p => p.FileExists(dumpPath)).Returns(true);
        _mockProbe.Setup(p => p.FileExists(restorePath)).Returns(true);

        _mockRunner.Setup(r => r.RunAsync(dumpPath, "--version", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(0, "pg_dump (PostgreSQL) 15.2", ""));

        var result = await _service.DetectAsync(customDir);

        Assert.Equal(ToolStatus.Ready, result.Status);
        Assert.Equal(DetectionSource.Path, result.Source);
        Assert.Equal(dumpPath, result.PgDumpPath);
        Assert.NotNull(result.Version);
        Assert.Equal(15, result.Version.Major);
    }

    [Fact]
    public async Task DetectAsync_WhenNotInPath_ChecksCommonDirectories()
    {
        _mockProbe.Setup(p => p.GetPathEntries()).Returns(Array.Empty<string>());

        string commonDir16 = @"C:\Program Files\PostgreSQL\16\bin";
        string dumpPath16 = Path.Combine(commonDir16, "pg_dump.exe");
        string restorePath16 = Path.Combine(commonDir16, "pg_restore.exe");

        _mockProbe.Setup(p => p.GetCommonPostgreSqlDirectories()).Returns([commonDir16]);
        _mockProbe.Setup(p => p.FileExists(dumpPath16)).Returns(true);
        _mockProbe.Setup(p => p.FileExists(restorePath16)).Returns(true);

        _mockRunner.Setup(r => r.RunAsync(dumpPath16, "--version", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(0, "pg_dump (PostgreSQL) 16.1", ""));

        var result = await _service.DetectAsync(null);

        Assert.Equal(ToolStatus.Ready, result.Status);
        Assert.Equal(DetectionSource.CommonDirectory, result.Source);
        Assert.Equal(dumpPath16, result.PgDumpPath);
    }

    [Fact]
    public async Task DetectAsync_WhenNotInCommonDirectories_ChecksRegistry()
    {
        _mockProbe.Setup(p => p.GetPathEntries()).Returns(Array.Empty<string>());
        _mockProbe.Setup(p => p.GetCommonPostgreSqlDirectories()).Returns(Array.Empty<string>());

        string regDir = @"D:\Tools\PostgreSQL\bin";
        string dumpPath = Path.Combine(regDir, "pg_dump.exe");

        _mockProbe.Setup(p => p.GetRegistryInstallations()).Returns([regDir]);
        _mockProbe.Setup(p => p.FileExists(dumpPath)).Returns(true);

        _mockRunner.Setup(r => r.RunAsync(dumpPath, "--version", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessResult(0, "pg_dump (PostgreSQL) 14.8", ""));

        var result = await _service.DetectAsync(null);

        Assert.Equal(ToolStatus.Ready, result.Status);
        Assert.Equal(DetectionSource.Registry, result.Source);
        Assert.Equal(dumpPath, result.PgDumpPath);
    }

    [Fact]
    public async Task DetectAsync_WhenNoToolsFoundAnywhere_ReturnsNotFound()
    {
        _mockProbe.Setup(p => p.GetPathEntries()).Returns(Array.Empty<string>());
        _mockProbe.Setup(p => p.GetCommonPostgreSqlDirectories()).Returns(Array.Empty<string>());
        _mockProbe.Setup(p => p.GetRegistryInstallations()).Returns(Array.Empty<string>());

        var result = await _service.DetectAsync(null);

        Assert.Equal(ToolStatus.NotFound, result.Status);
        Assert.Equal(DetectionSource.None, result.Source);
        Assert.Null(result.PgDumpPath);
        Assert.NotNull(result.ErrorMessage);
    }
}
