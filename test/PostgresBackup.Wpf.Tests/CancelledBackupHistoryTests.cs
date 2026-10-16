using Moq;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Wpf.ViewModels;

namespace PostgresBackup.Wpf.Tests;

public class CancelledBackupHistoryTests
{
    [Theory]
    [InlineData(BackupOperationType.Backup, false)]
    [InlineData(BackupOperationType.PreRestoreSnapshot, false)]
    [InlineData(BackupOperationType.Restore, true)]
    public void Partial_backups_cannot_be_selected_for_restore(BackupOperationType operation, bool canRestore)
    {
        var vm = new HistoryViewModel(new Mock<IBackupHistoryRepository>().Object);
        var record = new BackupRecord { Status = BackupStatus.Cancelled, OperationType = operation };
        BackupRecord? requested = null;
        vm.RequestRestore += value => requested = value;

        Assert.Equal(canRestore, vm.InitiateRestoreCommand.CanExecute(record));
        vm.InitiateRestore(record);
        Assert.Equal(canRestore, requested is not null);
    }
}
