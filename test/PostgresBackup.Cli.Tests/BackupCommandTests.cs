using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PostgresBackup.Cli.Commands;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using Xunit;

namespace PostgresBackup.Cli.Tests;

public class BackupCommandTests
{
    [Fact]
    public async Task BackupCommand_WhenSuccessful_ReturnsExitCodeZero()
    {
        var mockRepo = new Mock<IConnectionProfileRepository>();
        var mockBackupService = new Mock<IBackupService>();

        mockBackupService.Setup(b => b.BackupAsync(
                It.IsAny<BackupOptions>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BackupResult.Success(
                @"C:\backups\test.dump",
                1024,
                TimeSpan.FromSeconds(1),
                "-Fc",
                BackupFormat.Custom));

        var services = new ServiceCollection();
        services.AddSingleton(mockRepo.Object);
        services.AddSingleton(mockBackupService.Object);
        var sp = services.BuildServiceProvider();

        var root = new RootCommand { BackupCommand.Create(sp) };

        var exitCode = await root.Parse(["backup", "-d", "mydb", "-H", "localhost"]).InvokeAsync(new InvocationConfiguration(), CancellationToken.None);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task BackupCommand_WhenServiceFails_ReturnsExitCodeOne()
    {
        var mockRepo = new Mock<IConnectionProfileRepository>();
        var mockBackupService = new Mock<IBackupService>();

        mockBackupService.Setup(b => b.BackupAsync(
                It.IsAny<BackupOptions>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BackupResult.Failure(
                "Backup process error",
                1,
                TimeSpan.FromSeconds(1),
                string.Empty));

        var services = new ServiceCollection();
        services.AddSingleton(mockRepo.Object);
        services.AddSingleton(mockBackupService.Object);
        var sp = services.BuildServiceProvider();

        var root = new RootCommand { BackupCommand.Create(sp) };

        var exitCode = await root.Parse(["backup", "-d", "mydb"]).InvokeAsync(new InvocationConfiguration(), CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task BackupCommand_WhenProfileNotFound_ReturnsNonZeroExitCode()
    {
        var mockRepo = new Mock<IConnectionProfileRepository>();
        mockRepo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConnectionProfile>());
        var mockBackupService = new Mock<IBackupService>();

        var services = new ServiceCollection();
        services.AddSingleton(mockRepo.Object);
        services.AddSingleton(mockBackupService.Object);
        var sp = services.BuildServiceProvider();

        var root = new RootCommand { BackupCommand.Create(sp) };

        var exitCode = await root.Parse(["backup", "--profile", "not-exist"]).InvokeAsync(new InvocationConfiguration(), CancellationToken.None);

        Assert.NotEqual(0, exitCode);
        mockBackupService.Verify(b => b.BackupAsync(
            It.IsAny<BackupOptions>(),
            It.IsAny<Action<string>?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BackupCommand_WhenPasswordOptionUsed_WritesExposureWarning()
    {
        var mockRepo = new Mock<IConnectionProfileRepository>();
        var mockBackupService = new Mock<IBackupService>();

        mockBackupService.Setup(b => b.BackupAsync(
                It.IsAny<BackupOptions>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(BackupResult.Success(
                @"C:\backups\test.dump",
                1024,
                TimeSpan.FromSeconds(1),
                "-Fc",
                BackupFormat.Custom));

        var services = new ServiceCollection();
        services.AddSingleton(mockRepo.Object);
        services.AddSingleton(mockBackupService.Object);
        var sp = services.BuildServiceProvider();

        var root = new RootCommand { BackupCommand.Create(sp) };

        var originalOut = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            await root.Parse(["backup", "-d", "mydb", "-H", "localhost", "-p", "secret"]).InvokeAsync(new InvocationConfiguration(), CancellationToken.None);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Contains("WARNING", writer.ToString());
        Assert.Contains("暴露", writer.ToString());
    }
}
