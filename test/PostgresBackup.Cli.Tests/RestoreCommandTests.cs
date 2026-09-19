using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PostgresBackup.Cli.Commands;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using Xunit;

namespace PostgresBackup.Cli.Tests;

public class RestoreCommandTests
{
    [Fact]
    public async Task RestoreCommand_WhenSnapshotAndRestoreSucceed_ReturnsExitCodeZero()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"restore_test_{Guid.NewGuid():N}.dump");
        File.WriteAllText(tempFile, "DUMP DATA");

        try
        {
            var mockRepo = new Mock<IConnectionProfileRepository>();
            var mockRestoreService = new Mock<IRestoreService>();

            mockRestoreService.Setup(r => r.RestoreAsync(
                    It.IsAny<RestoreOptions>(),
                    It.IsAny<Action<string>?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(RestoreResult.Success(TimeSpan.FromSeconds(2), "-d testdb"));

            var services = new ServiceCollection();
            services.AddSingleton(mockRepo.Object);
            services.AddSingleton(mockRestoreService.Object);
            var sp = services.BuildServiceProvider();

            var root = new RootCommand { RestoreCommand.Create(sp) };

            var exitCode = await root.Parse(["restore", "-f", tempFile, "-d", "testdb", "--yes"]).InvokeAsync(new InvocationConfiguration(), CancellationToken.None);

            Assert.Equal(0, exitCode);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    [Fact]
    public async Task RestoreCommand_WhenRestoreServiceFails_ReturnsExitCodeOne()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"restore_test_{Guid.NewGuid():N}.dump");
        File.WriteAllText(tempFile, "DUMP DATA");

        try
        {
            var mockRepo = new Mock<IConnectionProfileRepository>();
            var mockRestoreService = new Mock<IRestoreService>();

            mockRestoreService.Setup(r => r.RestoreAsync(
                    It.IsAny<RestoreOptions>(),
                    It.IsAny<Action<string>?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(RestoreResult.Failure("Snapshot failed, aborted", 1, TimeSpan.FromSeconds(1), ""));

            var services = new ServiceCollection();
            services.AddSingleton(mockRepo.Object);
            services.AddSingleton(mockRestoreService.Object);
            var sp = services.BuildServiceProvider();

            var root = new RootCommand { RestoreCommand.Create(sp) };

            var exitCode = await root.Parse(["restore", "-f", tempFile, "-d", "testdb", "--yes"]).InvokeAsync(new InvocationConfiguration(), CancellationToken.None);

            Assert.Equal(1, exitCode);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    [Fact]
    public async Task RestoreCommand_WhenProfileNotFound_ReturnsNonZeroExitCode()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"restore_test_{Guid.NewGuid():N}.dump");
        File.WriteAllText(tempFile, "DUMP DATA");

        try
        {
            var mockRepo = new Mock<IConnectionProfileRepository>();
            mockRepo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ConnectionProfile>());
            var mockRestoreService = new Mock<IRestoreService>();

            var services = new ServiceCollection();
            services.AddSingleton(mockRepo.Object);
            services.AddSingleton(mockRestoreService.Object);
            var sp = services.BuildServiceProvider();

            var root = new RootCommand { RestoreCommand.Create(sp) };

            var exitCode = await root.Parse(["restore", "-f", tempFile, "--profile", "not-exist", "--yes"]).InvokeAsync(new InvocationConfiguration(), CancellationToken.None);

            Assert.NotEqual(0, exitCode);
            mockRestoreService.Verify(r => r.RestoreAsync(
                It.IsAny<RestoreOptions>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<CancellationToken>()), Times.Never);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    [Fact]
    public async Task RestoreCommand_WhenPasswordOptionUsed_WritesExposureWarning()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"restore_test_{Guid.NewGuid():N}.dump");
        File.WriteAllText(tempFile, "DUMP DATA");

        try
        {
            var mockRepo = new Mock<IConnectionProfileRepository>();
            var mockRestoreService = new Mock<IRestoreService>();

            mockRestoreService.Setup(r => r.RestoreAsync(
                    It.IsAny<RestoreOptions>(),
                    It.IsAny<Action<string>?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(RestoreResult.Success(TimeSpan.FromSeconds(2), "-d testdb"));

            var services = new ServiceCollection();
            services.AddSingleton(mockRepo.Object);
            services.AddSingleton(mockRestoreService.Object);
            var sp = services.BuildServiceProvider();

            var root = new RootCommand { RestoreCommand.Create(sp) };

            var originalOut = Console.Out;
            var writer = new StringWriter();
            Console.SetOut(writer);
            try
            {
                await root.Parse(["restore", "-f", tempFile, "-d", "testdb", "--yes", "-p", "secret"]).InvokeAsync(new InvocationConfiguration(), CancellationToken.None);
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            Assert.Contains("WARNING", writer.ToString());
            Assert.Contains("暴露", writer.ToString());
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }
}
