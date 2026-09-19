using Moq;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Wpf.ViewModels;

namespace PostgresBackup.Wpf.Tests;

/// <summary>
/// 釘住「測試連線失敗時不得把密碼顯示出來」此一行為：
/// 資料庫驅動程式的例外訊息在連線字串格式異常時有可能回吐整條連線字串，
/// 而連線字串含密碼。這條路徑上原本沒有任何遮蔽。
/// </summary>
public class SettingsViewModelTestConnectionRedactionTests
{
    private const string Password = "primeeagle20260912@";

    private static SettingsViewModel CreateViewModel(Mock<IToolDetectionService> toolDetector)
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ConnectionProfile>());

        var vm = new SettingsViewModel(toolDetector.Object, repo.Object)
        {
            Host = "localhost",
            Port = 5432,
            Database = "postgres",
            Username = "pex",
            Password = Password
        };

        return vm;
    }

    [Fact]
    public async Task 相容性檢查訊息夾帶連線字串時不得顯示密碼()
    {
        var toolDetector = new Mock<IToolDetectionService>();
        toolDetector.Setup(t => t.CheckCompatibilityAsync(
                It.IsAny<ToolDetectionResult>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VersionCheckResult.Failed(
                $"Format of the initialization string does not conform to specification: " +
                $"Host=localhost;Port=5432;Database=postgres;Username=pex;Password={Password}"));

        var vm = CreateViewModel(toolDetector);

        await vm.TestConnectionAsync();

        Assert.False(vm.IsConnectionSuccessful);
        Assert.DoesNotContain(Password, vm.ConnectionStatusMessage);
        Assert.Contains("***", vm.ConnectionStatusMessage);
    }

    [Fact]
    public async Task 相容性檢查拋出的例外訊息夾帶連線字串時不得顯示密碼()
    {
        var toolDetector = new Mock<IToolDetectionService>();
        toolDetector.Setup(t => t.CheckCompatibilityAsync(
                It.IsAny<ToolDetectionResult>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                $"Host=localhost;Port=5432;Database=postgres;Username=pex;Password={Password}"));

        var vm = CreateViewModel(toolDetector);

        await vm.TestConnectionAsync();

        Assert.False(vm.IsConnectionSuccessful);
        Assert.DoesNotContain(Password, vm.ConnectionStatusMessage);
    }

    [Fact]
    public async Task 連線成功時的判定與訊息不受遮蔽影響()
    {
        var toolDetector = new Mock<IToolDetectionService>();
        toolDetector.Setup(t => t.CheckCompatibilityAsync(
                It.IsAny<ToolDetectionResult>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VersionCheckResult.Compatible(new ToolVersion(18, 6), 18, "18.6"));

        var vm = CreateViewModel(toolDetector);

        await vm.TestConnectionAsync();

        Assert.True(vm.IsConnectionSuccessful);
        Assert.Contains("18.6", vm.ServerVersionDisplay);
    }
}
