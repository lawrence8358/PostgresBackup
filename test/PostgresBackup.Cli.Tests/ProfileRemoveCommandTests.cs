using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PostgresBackup.Cli.Commands;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;
using Xunit;

namespace PostgresBackup.Cli.Tests;

public class ProfileRemoveCommandTests : IDisposable
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
        var directory = Path.Combine(Path.GetTempPath(), $"pg_test_cli_store_remove_{Guid.NewGuid():N}");
        _tempDirectories.Add(directory);
        return MachineScopedStoreLocation.CreateUnrestrictedForTesting(directory);
    }

    private static Mock<IEnvironmentProbe> EnvironmentProbeReportingNoDirectory()
    {
        var probe = new Mock<IEnvironmentProbe>();
        probe.Setup(p => p.GetDirectoryAccessInfo(It.IsAny<string>())).Returns((DirectoryAccessInfo?)null);
        return probe;
    }

    private static IServiceProvider BuildServices(IConnectionProfileRepository repo)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repo);
        services.AddSingleton(EnvironmentProbeReportingNoDirectory().Object);

        // 存放區位置必須註冊，否則指令會回退到正式的 ProgramData 路徑。
        services.AddSingleton(new MachineScopedStoreLocation(
            Path.Combine(Path.GetTempPath(), $"pg_test_cli_store_remove_{Guid.NewGuid():N}"),
            EnforceRestrictivePermissions: false));

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

    private async Task<(MachineScopedConnectionProfileRepository Repo, string FilePath)> CreateRepoWithProfileAsync(
        string name, string? password)
    {
        var location = NewTempStoreLocation();
        var repo = new MachineScopedConnectionProfileRepository(
            new MachineScopedCredentialStorage(location, forceMemoryStorage: true), location);

        var profile = new ConnectionProfile
        {
            Id = name,
            Name = name,
            Host = "db.internal",
            Port = 5433,
            Database = "billing",
            Username = "svc_backup",
            CreatedAt = DateTimeOffset.UtcNow
        };

        await repo.SaveProfileAsync(profile, password);
        return (repo, location.ProfilesFilePath);
    }

    [Fact]
    public async Task ProfileRemove_WhenExists_RemovesProfileAndClearsPassword()
    {
        var (repo, _) = await CreateRepoWithProfileAsync("nightly", Password);
        var sp = BuildServices(repo);

        var (exitCode, output) = await RunAsync(sp, "profile", "remove", "--name", "nightly");

        Assert.Equal(0, exitCode);
        Assert.Contains("已刪除", output);
        Assert.Empty(await repo.GetAllProfilesAsync());
        Assert.Null(await repo.GetPasswordAsync("nightly"));
    }

    [Fact]
    public async Task ProfileRemove_AfterRemoval_ProfileListNoLongerShowsIt()
    {
        var (repo, _) = await CreateRepoWithProfileAsync("nightly", Password);
        var sp = BuildServices(repo);

        var (removeExitCode, _) = await RunAsync(sp, "profile", "remove", "--name", "nightly");
        Assert.Equal(0, removeExitCode);

        var (listExitCode, listOutput) = await RunAsync(sp, "profile", "list");

        Assert.Equal(0, listExitCode);
        Assert.DoesNotContain("nightly", listOutput);
    }

    [Fact]
    public async Task ProfileRemove_WhenNameDoesNotExist_ReturnsNonZeroAndReportsError()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var sp = BuildServices(repo.Object);

        var (exitCode, output) = await RunAsync(sp, "profile", "remove", "--name", "does-not-exist");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("[ERROR]", output);
        repo.Verify(r => r.DeleteProfileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
