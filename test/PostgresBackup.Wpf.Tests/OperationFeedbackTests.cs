using System.IO;
using Moq;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Wpf.ViewModels;

namespace PostgresBackup.Wpf.Tests;

public class OperationFeedbackTests
{
    [Fact]
    public async Task Restore_command_cancellation_is_not_reported_as_an_unexpected_error()
    {
        var source = Path.GetTempFileName();
        try
        {
            var repository = new Mock<IConnectionProfileRepository>();
            repository.Setup(r => r.GetPasswordAsync("profile-1", It.IsAny<CancellationToken>())).ReturnsAsync("test");
            var service = new Mock<IRestoreService>();
            service.Setup(s => s.RestoreAsync(It.IsAny<RestoreOptions>(), It.IsAny<Action<string>?>(), It.IsAny<CancellationToken>()))
                .Returns<RestoreOptions, Action<string>?, CancellationToken>(async (_, _, ct) =>
                {
                    try { await Task.Delay(Timeout.Infinite, ct); }
                    catch (OperationCanceledException ex)
                    {
                        throw new OperationCanceledException("Snapshot: D:\\snapshots\\safe.dump", ex, ct);
                    }
                    throw new InvalidOperationException("取消未生效");
                });
            var vm = new RestoreViewModel(service.Object, repository.Object)
            {
                SourceFilePath = source,
                SelectedProfile = new ConnectionProfile { Id = "profile-1", Database = "postgres" },
                IsConfirmed = true
            };

            var operation = vm.StartRestoreCommand.ExecuteAsync(null);
            try { Assert.True(vm.StartRestoreCommand.CanBeCanceled); }
            finally { vm.StartRestoreCommand.Cancel(); }
            await operation.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(vm.IsRestoring);
            Assert.Equal("Cancelled", vm.StatusKind.ToString());
            Assert.Contains(@"D:\snapshots\safe.dump", vm.ErrorDetails);
            Assert.False(vm.IsConfirmed);
        }
        finally { File.Delete(source); }
    }

    [Fact]
    public async Task Backup_command_can_be_cancelled_and_returns_to_an_idle_execution_state()
    {
        var repository = new Mock<IConnectionProfileRepository>();
        repository.Setup(r => r.GetPasswordAsync("profile-1", It.IsAny<CancellationToken>())).ReturnsAsync("test");
        var service = new Mock<IBackupService>();
        service.Setup(s => s.BackupAsync(It.IsAny<BackupOptions>(), It.IsAny<Action<string>?>(), It.IsAny<CancellationToken>()))
            .Returns<BackupOptions, Action<string>?, CancellationToken>(async (_, log, ct) =>
            {
                log?.Invoke("[pg_dump] running");
                await Task.Delay(Timeout.Infinite, ct);
                throw new InvalidOperationException("取消未生效");
            });
        var vm = new BackupViewModel(service.Object, repository.Object)
        {
            SelectedProfile = new ConnectionProfile { Id = "profile-1", Database = "postgres" }
        };

        var operation = vm.StartBackupCommand.ExecuteAsync(null);
        try
        {
            Assert.True(vm.StartBackupCommand.CanBeCanceled);
        }
        finally
        {
            vm.StartBackupCommand.Cancel();
        }
        await operation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(vm.IsBackingUp);
        Assert.Equal("Cancelled", vm.StatusKind.ToString());
        Assert.Contains("[pg_dump] running", vm.TerminalOutput);
    }

    [Fact]
    public async Task Backup_shows_busy_state_and_streams_live_terminal_output()
    {
        var repository = new Mock<IConnectionProfileRepository>();
        repository
            .Setup(r => r.GetPasswordAsync("profile-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("<REDACTED>");

        var completion = new TaskCompletionSource<BackupResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var backupService = new Mock<IBackupService>();
        backupService
            .Setup(s => s.BackupAsync(
                It.IsAny<BackupOptions>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<BackupOptions, Action<string>?, CancellationToken>((_, onLogLine, _) =>
                onLogLine?.Invoke("[pg_dump] still running"))
            .Returns(completion.Task);

        var vm = new BackupViewModel(backupService.Object, repository.Object)
        {
            SelectedProfile = new ConnectionProfile { Id = "profile-1", Database = "postgres" }
        };

        var operation = vm.StartBackupAsync();

        Assert.True(vm.IsBackingUp);
        Assert.Equal(OperationStatusKind.Running, vm.StatusKind);
        await WaitForTerminalLineAsync(vm, "[pg_dump] still running");

        completion.SetResult(BackupResult.Success(
            "backup.dump", 1, TimeSpan.Zero, string.Empty, BackupFormat.Custom));
        await operation;

        Assert.False(vm.IsBackingUp);
        Assert.Equal(OperationStatusKind.Completed, vm.StatusKind);
    }

    [Fact]
    public async Task Restore_shows_busy_state_and_streams_live_terminal_output()
    {
        var sourceFilePath = Path.Combine(
            Path.GetTempPath(), $"restore-feedback-{Guid.NewGuid():N}.dump");
        await File.WriteAllTextAsync(sourceFilePath, "mock dump");

        try
        {
            var repository = new Mock<IConnectionProfileRepository>();
            repository
                .Setup(r => r.GetPasswordAsync("profile-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync("<REDACTED>");

            var completion = new TaskCompletionSource<RestoreResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var restoreService = new Mock<IRestoreService>();
            restoreService
                .Setup(s => s.RestoreAsync(
                    It.IsAny<RestoreOptions>(),
                    It.IsAny<Action<string>?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RestoreOptions, Action<string>?, CancellationToken>((_, onLogLine, _) =>
                    onLogLine?.Invoke("[pg_restore] still running"))
                .Returns(completion.Task);

            var vm = new RestoreViewModel(restoreService.Object, repository.Object)
            {
                SourceFilePath = sourceFilePath,
                SelectedProfile = new ConnectionProfile { Id = "profile-1", Database = "postgres" },
                IsConfirmed = true,
                CreatePreRestoreSnapshot = false
            };

            var operation = vm.StartRestoreAsync();

            Assert.True(vm.IsRestoring);
            Assert.False(vm.CanStartRestore);
            Assert.Equal(OperationStatusKind.Running, vm.StatusKind);
            await WaitForTerminalLineAsync(vm, "[pg_restore] still running");

            completion.SetResult(RestoreResult.Success(TimeSpan.Zero, string.Empty));
            await operation;

            Assert.False(vm.IsRestoring);
            Assert.True(vm.CanStartRestore);
            Assert.Equal(OperationStatusKind.Completed, vm.StatusKind);
        }
        finally
        {
            File.Delete(sourceFilePath);
        }
    }

    private static async Task WaitForTerminalLineAsync(object viewModel, string expectedLine)
    {
        Func<string> terminalOutput = viewModel switch
        {
            BackupViewModel backup => () => backup.TerminalOutput,
            RestoreViewModel restore => () => restore.TerminalOutput,
            _ => throw new ArgumentOutOfRangeException(nameof(viewModel))
        };

        var deadline = DateTime.UtcNow.AddSeconds(1);
        while (!terminalOutput().Contains(expectedLine, StringComparison.Ordinal) && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Assert.Contains(expectedLine, terminalOutput());
    }
}
