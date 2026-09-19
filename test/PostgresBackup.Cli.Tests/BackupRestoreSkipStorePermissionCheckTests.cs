using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PostgresBackup.Cli.Commands;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;
using Xunit;

namespace PostgresBackup.Cli.Tests;

/// <summary>
/// 釘住一個刻意的不對稱：<c>profile set</c> 與 <c>profile list</c> 檢查存放區權限，
/// 備份與還原則<b>不檢查</b>。
/// <para>
/// 理由是那個警告對備份作業沒有可採取的行動——備份只讀不寫，不會讓任何密碼落地，
/// 而每晚跑一次的排程若每次都印一行無人閱讀的警告，只會訓練使用者忽略警告。
/// 此行為目前僅靠「備份／還原的相依注入根本沒註冊 <c>IEnvironmentProbe</c>」隱含成立，
/// 那是巧合而非防線：本測試把權限狀態刻意設成不合格，明示地釘住它。
/// </para>
/// </summary>
public class BackupRestoreSkipStorePermissionCheckTests
{
    private const string ProfileName = "nightly";

    /// <summary>回報「權限已被放寬」的探測替身：若備份／還原真的檢查了，就一定會印出警告。</summary>
    private static Mock<IEnvironmentProbe> EnvironmentProbeReportingLoosePermissions(string directory)
    {
        var probe = new Mock<IEnvironmentProbe>();
        probe.Setup(p => p.GetDirectoryAccessInfo(It.IsAny<string>()))
            .Returns(new DirectoryAccessInfo
            {
                Path = directory,
                Exists = true,
                InheritanceEnabled = true,
                AllowedIdentities = ["S-1-5-32-545"] // BUILTIN\Users：任何一般使用者都讀得到
            });
        return probe;
    }

    private static Mock<IConnectionProfileRepository> RepositoryWithProfile()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConnectionProfile>
            {
                new()
                {
                    Id = ProfileName, Name = ProfileName, Host = "localhost",
                    Port = 5432, Database = "billing", Username = "svc_backup"
                }
            });
        repo.Setup(r => r.GetPasswordAsync(ProfileName, It.IsAny<CancellationToken>()))
            .ReturnsAsync("SuperSecret!42");
        return repo;
    }

    private static ServiceCollection BaseServices(out string storeDirectory)
    {
        storeDirectory = Path.Combine(Path.GetTempPath(), $"pg_test_cli_store_noncheck_{Guid.NewGuid():N}");

        var services = new ServiceCollection();
        services.AddSingleton(RepositoryWithProfile().Object);
        services.AddSingleton(EnvironmentProbeReportingLoosePermissions(storeDirectory).Object);
        services.AddSingleton(new MachineScopedStoreLocation(storeDirectory, EnforceRestrictivePermissions: true));
        return services;
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

    [Fact]
    public async Task Backup_WithProfile_DoesNotWarnAboutStoreDirectoryPermissions()
    {
        var services = BaseServices(out _);
        var backupService = new Mock<IBackupService>();
        backupService.Setup(b => b.BackupAsync(
                It.IsAny<BackupOptions>(), It.IsAny<Action<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BackupResult.Success(
                @"C:\backups\test.dump", 1024, TimeSpan.FromSeconds(1), "-Fc", BackupFormat.Custom));
        services.AddSingleton(backupService.Object);

        var (exitCode, output) = await RunAsync(
            BackupCommand.Create(services.BuildServiceProvider()),
            "backup", "--profile", ProfileName, "-d", "billing");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("存放區", output);
    }

    [Fact]
    public async Task Restore_WithProfile_DoesNotWarnAboutStoreDirectoryPermissions()
    {
        var services = BaseServices(out _);
        var restoreService = new Mock<IRestoreService>();
        restoreService.Setup(r => r.RestoreAsync(
                It.IsAny<RestoreOptions>(), It.IsAny<Action<string>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RestoreResult.Success(TimeSpan.FromSeconds(1), string.Empty, null));
        services.AddSingleton(restoreService.Object);

        var dumpFile = Path.Combine(Path.GetTempPath(), $"pg_test_restore_{Guid.NewGuid():N}.dump");
        await File.WriteAllTextAsync(dumpFile, "dummy");

        try
        {
            var (_, output) = await RunAsync(
                RestoreCommand.Create(services.BuildServiceProvider()),
                "restore", "--profile", ProfileName, "-f", dumpFile, "--yes");

            Assert.DoesNotContain("存放區", output);
        }
        finally
        {
            try { File.Delete(dumpFile); } catch { }
        }
    }
}
