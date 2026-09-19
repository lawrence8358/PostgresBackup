using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PostgresBackup.Cli.Commands;
using PostgresBackup.Core.Exceptions;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;
using Xunit;

namespace PostgresBackup.Cli.Tests;

/// <summary>
/// 命令列連線設定存放區刻意只有 Administrators 與 SYSTEM 可讀，
/// 因此「一般使用者執行」是常態情境而非異常。此時各指令必須說出真正的原因，
/// 不得把存取被拒折疊成「目前沒有任何連線設定」或「找不到名為 X 的連線設定」——
/// 那會把人引去重建一份其實已經存在的設定。
/// </summary>
public class ProfileStoreAccessDiagnosticsTests
{
    private const string AccessDeniedMessage =
        "無法讀取命令列連線設定存放區：權限不足。請以系統管理員身分重新執行此指令。";

    private static Mock<IConnectionProfileRepository> RepositoryDenyingAccess()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ProfileStoreAccessDeniedException(AccessDeniedMessage));
        return repo;
    }

    private static Mock<IConnectionProfileRepository> RepositoryWithProfile(string name, string? password)
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConnectionProfile>
            {
                new() { Id = name, Name = name, Host = "localhost", Port = 5432, Database = "postgres", Username = "pex" }
            });
        repo.Setup(r => r.GetPasswordAsync(name, It.IsAny<CancellationToken>()))
            .ReturnsAsync(password);
        return repo;
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(Command command, params string[] args)
    {
        var root = new RootCommand { command };
        var originalOut = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            var exitCode = await root.Parse(args).InvokeAsync(new InvocationConfiguration(), CancellationToken.None);
            return (exitCode, writer.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    private static IServiceProvider ProfileServices(IConnectionProfileRepository repo)
    {
        var probe = new Mock<IEnvironmentProbe>();
        probe.Setup(p => p.GetDirectoryAccessInfo(It.IsAny<string>())).Returns((DirectoryAccessInfo?)null);

        var services = new ServiceCollection();
        services.AddSingleton(repo);
        services.AddSingleton(probe.Object);
        services.AddSingleton(new MachineScopedStoreLocation(
            Path.Combine(Path.GetTempPath(), $"pg_test_cli_store_diag_{Guid.NewGuid():N}"),
            EnforceRestrictivePermissions: false));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ProfileList_WhenStoreAccessIsDenied_ReportsPermissionsInsteadOfAnEmptyStore()
    {
        var sp = ProfileServices(RepositoryDenyingAccess().Object);

        var (exitCode, output) = await RunAsync(ProfileCommand.Create(sp), "profile", "list");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("權限不足", output);
        Assert.DoesNotContain("目前沒有任何命令列連線設定", output);
    }

    [Fact]
    public async Task ProfileRemove_WhenStoreAccessIsDenied_ReportsPermissionsInsteadOfMissingProfile()
    {
        var sp = ProfileServices(RepositoryDenyingAccess().Object);

        var (exitCode, output) = await RunAsync(ProfileCommand.Create(sp), "profile", "remove", "--name", "nightly");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("權限不足", output);
        Assert.DoesNotContain("找不到名為", output);
    }

    [Fact]
    public async Task Backup_WhenStoreAccessIsDenied_ReportsPermissionsInsteadOfMissingProfile()
    {
        var services = new ServiceCollection();
        services.AddSingleton(RepositoryDenyingAccess().Object);
        services.AddSingleton(new Mock<IBackupService>().Object);
        var sp = services.BuildServiceProvider();

        var (exitCode, output) = await RunAsync(
            BackupCommand.Create(sp), "backup", "--profile", "nightly", "-d", "mydb");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("權限不足", output);
        Assert.DoesNotContain("找不到名為", output);
    }

    [Fact]
    public async Task Restore_WhenStoreAccessIsDenied_ReportsPermissionsInsteadOfMissingProfile()
    {
        var services = new ServiceCollection();
        services.AddSingleton(RepositoryDenyingAccess().Object);
        services.AddSingleton(new Mock<IRestoreService>().Object);
        var sp = services.BuildServiceProvider();

        var (exitCode, output) = await RunAsync(
            RestoreCommand.Create(sp), "restore", "--profile", "nightly", "-f", "dummy.dump");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("權限不足", output);
        Assert.DoesNotContain("找不到名為", output);
    }

    [Fact]
    public async Task CheckTools_WhenStoreAccessIsDenied_ReportsPermissionsInsteadOfMissingProfile()
    {
        var services = new ServiceCollection();
        services.AddSingleton(RepositoryDenyingAccess().Object);
        services.AddSingleton(new Mock<IToolDetectionService>().Object);
        var sp = services.BuildServiceProvider();

        var (exitCode, output) = await RunAsync(
            CheckToolsCommand.Create(sp), "check-tools", "--profile", "nightly");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("權限不足", output);
        Assert.DoesNotContain("找不到名為", output);
    }

    [Fact]
    public async Task CheckTools_WhenProfilePasswordIsMissing_WarnsBeforeAttemptingToConnect()
    {
        var detector = new Mock<IToolDetectionService>();
        detector.Setup(d => d.DetectAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolDetectionResult.CreateNotFound("not installed"));

        var services = new ServiceCollection();
        services.AddSingleton(RepositoryWithProfile("nightly", password: null).Object);
        services.AddSingleton(detector.Object);
        var sp = services.BuildServiceProvider();

        var (_, output) = await RunAsync(
            CheckToolsCommand.Create(sp), "check-tools", "--profile", "nightly");

        Assert.Contains("遺失", output);
        Assert.Contains("profile set --name nightly", output);
    }

    [Fact]
    public async Task CheckTools_WhenProfilePasswordIsPresent_DoesNotWarn()
    {
        var detector = new Mock<IToolDetectionService>();
        detector.Setup(d => d.DetectAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolDetectionResult.CreateNotFound("not installed"));

        var services = new ServiceCollection();
        services.AddSingleton(RepositoryWithProfile("nightly", "SuperSecret!42").Object);
        services.AddSingleton(detector.Object);
        var sp = services.BuildServiceProvider();

        var (_, output) = await RunAsync(
            CheckToolsCommand.Create(sp), "check-tools", "--profile", "nightly");

        Assert.DoesNotContain("遺失", output);
    }
}
