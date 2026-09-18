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
}
