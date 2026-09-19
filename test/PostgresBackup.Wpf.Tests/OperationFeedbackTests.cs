using System.IO;
using Moq;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Wpf.ViewModels;

namespace PostgresBackup.Wpf.Tests;

public class OperationFeedbackTests
{
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
