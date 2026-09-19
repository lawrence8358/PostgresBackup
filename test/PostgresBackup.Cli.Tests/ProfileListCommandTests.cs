using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PostgresBackup.Cli.Commands;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;
using Xunit;

namespace PostgresBackup.Cli.Tests;

public class ProfileListCommandTests : IDisposable
{
    private const string Password = "SuperSecret!42";
    private readonly List<string> _tempDirectories = [];

    public void Dispose()
    {
        foreach (var directory in _tempDirectories)
        {
            try { Directory.Delete(directory, recursive: true); } catch { }
        }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 測試專屬的存放區位置。位置與權限政策成對傳入，
    /// 且一律不要求限制性權限：套用後非系統管理員的測試行程會失去對自身暫存目錄的存取權。
    /// </summary>
    private MachineScopedStoreLocation NewTempStoreLocation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"pg_test_cli_store_list_{Guid.NewGuid():N}");
        _tempDirectories.Add(directory);
        return MachineScopedStoreLocation.CreateUnrestrictedForTesting(directory);
    }

    private static Mock<IEnvironmentProbe> EnvironmentProbeReportingNoDirectory()
    {
        var probe = new Mock<IEnvironmentProbe>();
        probe.Setup(p => p.GetDirectoryAccessInfo(It.IsAny<string>())).Returns((DirectoryAccessInfo?)null);
        return probe;
    }

    /// <summary>針對特定存放區目錄回報權限狀態的替身：確保檢查的目錄與注入的位置一致。</summary>
    private static Mock<IEnvironmentProbe> EnvironmentProbeReporting(string directory, DirectoryAccessInfo info)
    {
        var probe = new Mock<IEnvironmentProbe>();
        probe.Setup(p => p.GetDirectoryAccessInfo(directory)).Returns(info);
        return probe;
    }

    /// <summary>
    /// 測試專屬的存放區目錄路徑。<c>profile list</c> 只讀取權限狀態（由替身提供），
    /// 不會實際建立或收緊此目錄；重點是不得回退到真實的 ProgramData 存放區。
    /// </summary>
    private static string NewStoreDirectoryPath()
        => Path.Combine(Path.GetTempPath(), $"pg_test_cli_store_list_{Guid.NewGuid():N}");

    private static IServiceProvider BuildServices(
        IConnectionProfileRepository repo,
        IEnvironmentProbe? environmentProbe = null,
        MachineScopedStoreLocation? storeLocation = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repo);
        services.AddSingleton(environmentProbe ?? EnvironmentProbeReportingNoDirectory().Object);
        services.AddSingleton(storeLocation ?? new MachineScopedStoreLocation(
            NewStoreDirectoryPath(), EnforceRestrictivePermissions: false));
        return services.BuildServiceProvider();
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(IServiceProvider sp, params string[] args)
    {
        var root = new RootCommand { ProfileCommand.Create(sp) };
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

    private async Task<MachineScopedConnectionProfileRepository> CreateRepoWithProfileAsync(
        string name, string? password, string? host = null)
    {
        var location = NewTempStoreLocation();
        var repo = new MachineScopedConnectionProfileRepository(
            new MachineScopedCredentialStorage(location, forceMemoryStorage: true), location);

        var profile = new ConnectionProfile
        {
            Id = name,
            Name = name,
            Host = host ?? "db.internal",
            Port = 5433,
            Database = "billing",
            Username = "svc_backup",
            CreatedAt = DateTimeOffset.UtcNow
        };

        await repo.SaveProfileAsync(profile, password);
        return repo;
    }

    [Fact]
    public async Task ProfileList_WhenNoProfiles_ReturnsZeroAndFriendlyMessage()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var sp = BuildServices(repo.Object);

        var (exitCode, output) = await RunAsync(sp, "profile", "list");

        Assert.Equal(0, exitCode);
        Assert.Contains("沒有任何", output);
    }

    [Fact]
    public async Task ProfileList_ShowsNameHostPortDatabaseUsernameSourceAndCreatedAt()
    {
        var repo = await CreateRepoWithProfileAsync("nightly", Password);

        var sp = BuildServices(repo);

        var (exitCode, output) = await RunAsync(sp, "profile", "list");

        Assert.Equal(0, exitCode);
        Assert.Contains("nightly", output);
        Assert.Contains("db.internal:5433", output);
        Assert.Contains("billing", output);
        Assert.Contains("svc_backup", output);
        Assert.Contains("命令列連線設定", output);
        Assert.Contains("建立時間", output);
    }

    [Fact]
    public async Task ProfileList_WhenPasswordSet_ShowsSetStatus_AndNeverContainsPasswordString()
    {
        var repo = await CreateRepoWithProfileAsync("nightly", Password);

        var sp = BuildServices(repo);

        var (exitCode, output) = await RunAsync(sp, "profile", "list");

        Assert.Equal(0, exitCode);
        Assert.Contains("已設定", output);
        Assert.DoesNotContain(Password, output);
    }

    [Fact]
    public async Task ProfileList_WhenPasswordMissing_ShowsMissingStatus()
    {
        var repo = await CreateRepoWithProfileAsync("nightly", null);

        var sp = BuildServices(repo);

        var (exitCode, output) = await RunAsync(sp, "profile", "list");

        Assert.Equal(0, exitCode);
        Assert.Contains("遺失", output);
    }

    [Fact]
    public async Task ProfileList_WhenDirectoryPermissionsAreExpected_DoesNotWarn()
    {
        var repo = await CreateRepoWithProfileAsync("nightly", Password);

        var storeDirectory = NewStoreDirectoryPath();
        var probe = EnvironmentProbeReporting(storeDirectory, new DirectoryAccessInfo
        {
            Path = storeDirectory,
            Exists = true,
            InheritanceEnabled = false,
            AllowedIdentities = [MachineScopedStore.AdministratorsSid, MachineScopedStore.LocalSystemSid]
        });

        var sp = BuildServices(repo, probe.Object,
            new MachineScopedStoreLocation(storeDirectory, EnforceRestrictivePermissions: true));

        var (exitCode, output) = await RunAsync(sp, "profile", "list");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("[WARNING]", output);
    }

    [Fact]
    public async Task ProfileList_WhenInheritanceStillEnabled_Warns()
    {
        var repo = await CreateRepoWithProfileAsync("nightly", Password);

        var storeDirectory = NewStoreDirectoryPath();
        var probe = EnvironmentProbeReporting(storeDirectory, new DirectoryAccessInfo
        {
            Path = storeDirectory,
            Exists = true,
            InheritanceEnabled = true,
            AllowedIdentities = [MachineScopedStore.AdministratorsSid, MachineScopedStore.LocalSystemSid]
        });

        var sp = BuildServices(repo, probe.Object,
            new MachineScopedStoreLocation(storeDirectory, EnforceRestrictivePermissions: true));

        var (exitCode, output) = await RunAsync(sp, "profile", "list");

        Assert.Equal(0, exitCode);
        Assert.Contains("[WARNING]", output);
    }

    [Fact]
    public async Task ProfileList_WhenExtraIdentityHasAccess_Warns()
    {
        var repo = await CreateRepoWithProfileAsync("nightly", Password);

        var storeDirectory = NewStoreDirectoryPath();
        var probe = EnvironmentProbeReporting(storeDirectory, new DirectoryAccessInfo
        {
            Path = storeDirectory,
            Exists = true,
            InheritanceEnabled = false,
            AllowedIdentities =
            [
                MachineScopedStore.AdministratorsSid,
                MachineScopedStore.LocalSystemSid,
                "S-1-1-0" // Everyone：不應具有存取權
            ]
        });

        var sp = BuildServices(repo, probe.Object,
            new MachineScopedStoreLocation(storeDirectory, EnforceRestrictivePermissions: true));

        var (exitCode, output) = await RunAsync(sp, "profile", "list");

        Assert.Equal(0, exitCode);
        Assert.Contains("[WARNING]", output);
    }

    [Fact]
    public async Task ProfileList_WhenDirectoryDoesNotExistYet_DoesNotWarn()
    {
        var repo = await CreateRepoWithProfileAsync("nightly", Password);

        var storeDirectory = NewStoreDirectoryPath();
        var probe = EnvironmentProbeReporting(storeDirectory, new DirectoryAccessInfo
        {
            Path = storeDirectory,
            Exists = false,
            InheritanceEnabled = true,
            AllowedIdentities = []
        });

        var sp = BuildServices(repo, probe.Object,
            new MachineScopedStoreLocation(storeDirectory, EnforceRestrictivePermissions: true));

        var (exitCode, output) = await RunAsync(sp, "profile", "list");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("[WARNING]", output);
    }
}
