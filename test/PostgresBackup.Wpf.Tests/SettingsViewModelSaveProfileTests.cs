using Moq;
using PostgresBackup.Core.Exceptions;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Wpf.ViewModels;

namespace PostgresBackup.Wpf.Tests;

/// <summary>
/// 釘住「儲存介面連線設定失敗時不得回報成功」此一行為：
/// 密碼寫入憑證存儲失敗時，使用者必須立即看到明確錯誤。
/// </summary>
public class SettingsViewModelSaveProfileTests
{
    private static SettingsViewModel CreateViewModel(Mock<IConnectionProfileRepository> repo)
    {
        var toolDetector = new Mock<IToolDetectionService>();
        return new SettingsViewModel(toolDetector.Object, repo.Object);
    }

    [Fact]
    public async Task 儲存連線設定失敗時應顯示錯誤且不回報成功()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.SaveProfileAsync(
                It.IsAny<ConnectionProfile>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CredentialStorageException("寫入認證管理員失敗（Win32 錯誤碼：87）。", 87));

        var vm = CreateViewModel(repo);
        vm.ProfileName = "測試連線";
        vm.Password = "SecretPassword123";

        await vm.SaveProfileAsync();

        Assert.False(vm.IsConnectionSuccessful);
        Assert.NotEqual(string.Empty, vm.ConnectionStatusMessage);
        Assert.Contains("87", vm.ConnectionStatusMessage);
        Assert.DoesNotContain("SecretPassword123", vm.ConnectionStatusMessage);
    }

    [Fact]
    public async Task 儲存連線設定成功時應回報成功()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.SaveProfileAsync(
                It.IsAny<ConnectionProfile>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ConnectionProfile>());

        var vm = CreateViewModel(repo);
        vm.ProfileName = "測試連線";
        vm.Password = "SecretPassword123";

        await vm.SaveProfileAsync();

        Assert.True(vm.IsConnectionSuccessful);
        Assert.NotEqual(string.Empty, vm.ConnectionStatusMessage);
    }
}
