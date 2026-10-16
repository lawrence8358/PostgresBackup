using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PostgresBackup.Cli.Commands;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using Xunit;

namespace PostgresBackup.Cli.Tests;

public class OperationCancellationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_reaches_the_service_and_returns_130_with_a_closed_log(bool restore)
    {
        using var cancellation = new CancellationTokenSource();
        var observedToken = CancellationToken.None;
        var backup = new Mock<IBackupService>();
        backup.Setup(s => s.BackupAsync(It.IsAny<BackupOptions>(), It.IsAny<Action<string>?>(), It.IsAny<CancellationToken>()))
            .Returns<BackupOptions, Action<string>?, CancellationToken>((_, _, ct) =>
            {
                observedToken = ct;
                cancellation.Cancel();
                return Task.FromException<BackupResult>(new OperationCanceledException("cancelled test backup", ct));
            });
        var restoreService = new Mock<IRestoreService>();
        restoreService.Setup(s => s.RestoreAsync(It.IsAny<RestoreOptions>(), It.IsAny<Action<string>?>(), It.IsAny<CancellationToken>()))
            .Returns<RestoreOptions, Action<string>?, CancellationToken>((_, _, ct) =>
            {
                observedToken = ct;
                cancellation.Cancel();
                return Task.FromException<RestoreResult>(new OperationCanceledException("snapshot: safe.dump", ct));
            });
        using var services = new ServiceCollection()
            .AddSingleton(new Mock<IConnectionProfileRepository>().Object)
            .AddSingleton(backup.Object)
            .AddSingleton(restoreService.Object)
            .BuildServiceProvider();
        var root = new RootCommand { BackupCommand.Create(services), RestoreCommand.Create(services) };
        var log = Path.Combine(Path.GetTempPath(), $"cancel-log-{Guid.NewGuid():N}.txt");
        var originalOutput = Console.Out;
        using var output = new StringWriter();
        Console.SetOut(output);
        try
        {
            string[] arguments = restore
                ? ["restore", "--file", "source.dump", "--yes", "--log", log]
                : ["backup", "--log", log];
            var exitCode = await root.Parse(arguments).InvokeAsync(new InvocationConfiguration(), cancellation.Token);

            Assert.True(observedToken.CanBeCanceled);
            Assert.True(observedToken.IsCancellationRequested);
            Assert.Equal(130, exitCode);
            Assert.Contains("CANCELLED", output.ToString());
            using var file = File.Open(log, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally
        {
            Console.SetOut(originalOutput);
            // 測試失敗時舊實作可能仍持有檔案，保留原始斷言而不以清理例外蓋過它。
            try { File.Delete(log); } catch (IOException) { }
        }
    }
}
