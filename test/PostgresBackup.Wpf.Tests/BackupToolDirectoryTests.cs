using Moq;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Wpf.ViewModels;

namespace PostgresBackup.Wpf.Tests;

public class BackupToolDirectoryTests
{
    [Fact]
    public async Task Backup_uses_custom_tool_directory_selected_in_settings()
    {
        const string customToolsDirectory = @"D:\Project\PostgreTools\PostgresBackup\dist\pgsql";
        var repository = new Mock<IConnectionProfileRepository>();
        repository.Setup(r => r.GetPasswordAsync("profile-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("<REDACTED>");

        var backupService = new Mock<IBackupService>();
        BackupOptions? capturedOptions = null;
        backupService
            .Setup(s => s.BackupAsync(
                It.IsAny<BackupOptions>(),
                It.IsAny<Action<string>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<BackupOptions, Action<string>?, CancellationToken>((options, _, _) => capturedOptions = options)
            .ReturnsAsync(BackupResult.Success("backup.dump", 1, TimeSpan.Zero, string.Empty, BackupFormat.Custom));

        var settings = new SettingsViewModel(new Mock<IToolDetectionService>().Object, repository.Object)
        {
            CustomPath = customToolsDirectory
        };
        var vm = new BackupViewModel(backupService.Object, repository.Object, settings)
        {
            SelectedProfile = new ConnectionProfile { Id = "profile-1", Database = "postgres" }
        };

        await vm.StartBackupAsync();

        Assert.NotNull(capturedOptions);
        Assert.Equal(customToolsDirectory, capturedOptions!.ClientToolDirectory);
    }
}
