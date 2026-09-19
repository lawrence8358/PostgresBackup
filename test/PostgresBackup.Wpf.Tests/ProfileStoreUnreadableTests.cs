using Moq;
using PostgresBackup.Core.Exceptions;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Wpf.ViewModels;

namespace PostgresBackup.Wpf.Tests;

/// <summary>
/// 連線設定存放區讀不到時，圖形介面必須說出原因而不是當掉。
/// <para>
/// 這幾條路徑都由啟動流程觸發：逸出的例外會變成整個視窗開不起來，
/// 使用者得到的訊息會是「應用程式當掉了」，而不是「這份設定讀不到，原因是……」。
/// 存放區改為讓存取被拒與檔案損毀穿透之後，這裡就是那些例外的落點。
/// </para>
/// </summary>
public class ProfileStoreUnreadableTests
{
    private const string FailureDetail = "權限不足";

    private static Mock<IConnectionProfileRepository> RepositoryThatCannotBeRead()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProfileStoreAccessDeniedException(FailureDetail));
        return repo;
    }

    [Fact]
    public async Task 設定頁載入連線設定失敗時應顯示原因而非拋出()
    {
        var vm = new SettingsViewModel(new Mock<IToolDetectionService>().Object, RepositoryThatCannotBeRead().Object);

        await vm.LoadProfilesAsync();

        Assert.Empty(vm.Profiles);
        Assert.Contains(FailureDetail, vm.ConnectionStatusMessage);
        Assert.False(vm.IsConnectionSuccessful);
    }

    [Fact]
    public async Task 設定頁讀取密碼失敗時應顯示原因而非留下空白密碼欄()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConnectionProfile>
            {
                new() { Id = "nightly", Name = "nightly", Host = "localhost", Database = "postgres", Username = "pex" }
            });
        repo.Setup(r => r.GetPasswordAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CredentialStoreCorruptedException(FailureDetail));

        var vm = new SettingsViewModel(new Mock<IToolDetectionService>().Object, repo.Object);

        await vm.LoadProfilesAsync();

        Assert.Equal(string.Empty, vm.Password);
        Assert.Contains(FailureDetail, vm.ConnectionStatusMessage);
    }

    [Fact]
    public async Task 備份頁載入連線設定失敗時應顯示原因而非拋出()
    {
        var vm = new BackupViewModel(
            new Mock<IBackupService>().Object,
            RepositoryThatCannotBeRead().Object);

        await vm.RefreshProfilesAsync();

        Assert.Empty(vm.Profiles);
        Assert.Contains(FailureDetail, vm.StatusMessage);
    }

    [Fact]
    public async Task 還原頁載入連線設定失敗時應顯示原因而非拋出()
    {
        var vm = new RestoreViewModel(
            new Mock<IRestoreService>().Object,
            RepositoryThatCannotBeRead().Object);

        await vm.RefreshProfilesAsync();

        Assert.Empty(vm.Profiles);
        Assert.Contains(FailureDetail, vm.StatusMessage);
    }
}
