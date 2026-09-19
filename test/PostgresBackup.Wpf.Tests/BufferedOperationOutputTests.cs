using System.ComponentModel;
using System.IO;
using Moq;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Wpf.Services;
using PostgresBackup.Wpf.ViewModels;

namespace PostgresBackup.Wpf.Tests;

public class BufferedOperationOutputTests
{
    private const string FailureDetail = "pg_restore: error: schema \"hangfire\" already exists\n"
        + "pg_restore: error: relation \"aggregatedcounter\" already exists\n"
        + "Command was: CREATE TABLE hangfire.aggregatedcounter (...);";

    [Fact]
    public async Task Backup_batches_synchronous_terminal_output_and_flushes_every_line_before_completion()
    {
        var repository = PasswordRepository();
        var service = new Mock<IBackupService>();
        service.Setup(s => s.BackupAsync(It.IsAny<BackupOptions>(), It.IsAny<Action<string>?>(), It.IsAny<CancellationToken>()))
            .Returns<BackupOptions, Action<string>?, CancellationToken>((_, onLogLine, _) =>
            {
                EmitLines(onLogLine);
                return Task.FromResult(BackupResult.Success("backup.dump", 1, TimeSpan.Zero, string.Empty, BackupFormat.Custom));
            });

        var vm = new BackupViewModel(service.Object, repository.Object)
        {
            SelectedProfile = Profile()
        };
        var terminalNotifications = CountTerminalNotifications(vm);

        await vm.StartBackupAsync();

        Assert.InRange(terminalNotifications(), 1, 2);
        Assert.Contains("line-0000", vm.TerminalOutput);
        Assert.Contains("line-0999", vm.TerminalOutput);
        Assert.Equal(OperationStatusKind.Completed, vm.StatusKind);
    }

    [Fact]
    public async Task Restore_batches_synchronous_terminal_output_and_flushes_every_line_before_completion()
    {
        var sourceFilePath = await CreateSourceFileAsync();
        try
        {
            var repository = PasswordRepository();
            var service = new Mock<IRestoreService>();
            service.Setup(s => s.RestoreAsync(It.IsAny<RestoreOptions>(), It.IsAny<Action<string>?>(), It.IsAny<CancellationToken>()))
                .Returns<RestoreOptions, Action<string>?, CancellationToken>((_, onLogLine, _) =>
                {
                    EmitLines(onLogLine);
                    return Task.FromResult(RestoreResult.Success(TimeSpan.Zero, string.Empty));
                });

            var vm = new RestoreViewModel(service.Object, repository.Object)
            {
                SourceFilePath = sourceFilePath,
                SelectedProfile = Profile(),
                IsConfirmed = true,
                CreatePreRestoreSnapshot = false
            };
            var terminalNotifications = CountTerminalNotifications(vm);

            await vm.StartRestoreAsync();

            Assert.InRange(terminalNotifications(), 1, 2);
            Assert.Contains("line-0000", vm.TerminalOutput);
            Assert.Contains("line-0999", vm.TerminalOutput);
            Assert.Equal(OperationStatusKind.Completed, vm.StatusKind);
        }
        finally
        {
            File.Delete(sourceFilePath);
        }
    }

    [Fact]
    public async Task Backup_failure_keeps_full_detail_out_of_status_and_copies_it_through_boundary()
    {
        var clipboard = new RecordingClipboardService();
        var service = new Mock<IBackupService>();
        service.Setup(s => s.BackupAsync(It.IsAny<BackupOptions>(), It.IsAny<Action<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BackupResult.Failure(FailureDetail, 1, TimeSpan.Zero, string.Empty));

        var vm = new BackupViewModel(service.Object, PasswordRepository().Object, clipboardService: clipboard)
        {
            SelectedProfile = Profile()
        };

        await vm.StartBackupAsync();
        vm.CopyErrorDetailsCommand.Execute(null);

        Assert.Equal(OperationStatusKind.Failed, vm.StatusKind);
        Assert.True(vm.HasErrorDetails);
        Assert.Equal(FailureDetail, vm.ErrorDetails);
        Assert.DoesNotContain("aggregatedcounter", vm.StatusMessage);
        Assert.Equal(FailureDetail, clipboard.Text);
    }

    [Fact]
    public async Task Restore_failure_keeps_full_detail_out_of_status_and_copies_it_through_boundary()
    {
        var sourceFilePath = await CreateSourceFileAsync();
        try
        {
            var clipboard = new RecordingClipboardService();
            var service = new Mock<IRestoreService>();
            service.Setup(s => s.RestoreAsync(It.IsAny<RestoreOptions>(), It.IsAny<Action<string>?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(RestoreResult.Failure(FailureDetail, 1, TimeSpan.Zero, string.Empty));

            var vm = new RestoreViewModel(service.Object, PasswordRepository().Object, clipboardService: clipboard)
            {
                SourceFilePath = sourceFilePath,
                SelectedProfile = Profile(),
                IsConfirmed = true,
                CreatePreRestoreSnapshot = false
            };

            await vm.StartRestoreAsync();
            vm.CopyErrorDetailsCommand.Execute(null);

            Assert.Equal(OperationStatusKind.Failed, vm.StatusKind);
            Assert.True(vm.HasErrorDetails);
            Assert.Equal(FailureDetail, vm.ErrorDetails);
            Assert.DoesNotContain("aggregatedcounter", vm.StatusMessage);
            Assert.Equal(FailureDetail, clipboard.Text);
        }
        finally
        {
            File.Delete(sourceFilePath);
        }
    }

    [Fact]
    public async Task Backup_exception_keeps_exception_detail_out_of_status()
    {
        var service = new Mock<IBackupService>();
        service.Setup(s => s.BackupAsync(It.IsAny<BackupOptions>(), It.IsAny<Action<string>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(FailureDetail));

        var vm = new BackupViewModel(service.Object, PasswordRepository().Object)
        {
            SelectedProfile = Profile()
        };

        await vm.StartBackupAsync();

        Assert.Equal(OperationStatusKind.Error, vm.StatusKind);
        Assert.True(vm.HasErrorDetails);
        Assert.Equal(FailureDetail, vm.ErrorDetails);
        Assert.DoesNotContain("aggregatedcounter", vm.StatusMessage);
    }

    [Fact]
    public async Task Restore_exception_keeps_exception_detail_out_of_status()
    {
        var sourceFilePath = await CreateSourceFileAsync();
        try
        {
            var service = new Mock<IRestoreService>();
            service.Setup(s => s.RestoreAsync(It.IsAny<RestoreOptions>(), It.IsAny<Action<string>?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException(FailureDetail));

            var vm = new RestoreViewModel(service.Object, PasswordRepository().Object)
            {
                SourceFilePath = sourceFilePath,
                SelectedProfile = Profile(),
                IsConfirmed = true,
                CreatePreRestoreSnapshot = false
            };

            await vm.StartRestoreAsync();

            Assert.Equal(OperationStatusKind.Error, vm.StatusKind);
            Assert.True(vm.HasErrorDetails);
            Assert.Equal(FailureDetail, vm.ErrorDetails);
            Assert.DoesNotContain("aggregatedcounter", vm.StatusMessage);
        }
        finally
        {
            File.Delete(sourceFilePath);
        }
    }

    private static Mock<IConnectionProfileRepository> PasswordRepository()
    {
        var repository = new Mock<IConnectionProfileRepository>();
        repository.Setup(r => r.GetPasswordAsync("profile-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("<REDACTED>");
        return repository;
    }

    private static ConnectionProfile Profile() => new() { Id = "profile-1", Database = "postgres" };

    private static void EmitLines(Action<string>? onLogLine)
    {
        for (var index = 0; index < 1_000; index++)
        {
            onLogLine?.Invoke($"line-{index:D4}");
        }
    }

    private static Func<int> CountTerminalNotifications(INotifyPropertyChanged viewModel)
    {
        var count = 0;
        viewModel.PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == "TerminalOutput") count++;
        };
        return () => count;
    }

    private static async Task<string> CreateSourceFileAsync()
    {
        var sourceFilePath = Path.Combine(Path.GetTempPath(), $"buffered-output-{Guid.NewGuid():N}.dump");
        await File.WriteAllTextAsync(sourceFilePath, "mock dump");
        return sourceFilePath;
    }

    private sealed class RecordingClipboardService : IClipboardService
    {
        public string? Text { get; private set; }

        public void SetText(string text) => Text = text;
    }
}
