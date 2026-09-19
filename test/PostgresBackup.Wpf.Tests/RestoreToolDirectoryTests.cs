using System.IO;
using Moq;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Wpf.ViewModels;

namespace PostgresBackup.Wpf.Tests;

public class RestoreToolDirectoryTests
{
    [Fact]
    public async Task Restore_uses_custom_tool_directory_selected_in_settings()
    {
        const string customToolsDirectory = @"D:\Project\PostgreTools\PostgresBackup\dist\pgsql";
        var sourceFilePath = Path.Combine(Path.GetTempPath(), $"restore-test-{Guid.NewGuid():N}.dump");
        await File.WriteAllTextAsync(sourceFilePath, "mock dump");

        try
        {
            var repository = new Mock<IConnectionProfileRepository>();
            repository.Setup(r => r.GetPasswordAsync("profile-1", It.IsAny<CancellationToken>()))
                .ReturnsAsync("<REDACTED>");

            var restoreService = new Mock<IRestoreService>();
            RestoreOptions? capturedOptions = null;
            restoreService
                .Setup(s => s.RestoreAsync(
                    It.IsAny<RestoreOptions>(),
                    It.IsAny<Action<string>?>(),
                    It.IsAny<CancellationToken>()))
                .Callback<RestoreOptions, Action<string>?, CancellationToken>((options, _, _) => capturedOptions = options)
                .ReturnsAsync(RestoreResult.Success(TimeSpan.Zero, string.Empty));

            var settings = new SettingsViewModel(new Mock<IToolDetectionService>().Object, repository.Object)
            {
                CustomPath = customToolsDirectory
            };
            var vm = new RestoreViewModel(restoreService.Object, repository.Object, settings)
            {
                SourceFilePath = sourceFilePath,
                SelectedProfile = new ConnectionProfile { Id = "profile-1", Database = "postgres" },
                IsConfirmed = true,
                CreatePreRestoreSnapshot = false
            };

            await vm.StartRestoreAsync();

            Assert.NotNull(capturedOptions);
            Assert.Equal(customToolsDirectory, capturedOptions!.ClientToolDirectory);
        }
        finally
        {
            File.Delete(sourceFilePath);
        }
    }
}
