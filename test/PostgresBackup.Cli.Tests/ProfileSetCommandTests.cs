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

public class ProfileSetCommandTests : IDisposable
{
    private const string Password = "SuperSecret!42";
    private readonly List<string> _tempDirectories = [];

    public void Dispose()
    {
        // 要求限制性權限的測試會讓本行程失去對該暫存目錄的存取權，刪除失敗屬預期。
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
        => MachineScopedStoreLocation.CreateUnrestrictedForTesting(NewTempStoreDirectory());

    /// <summary>
    /// 測試專屬的存放區目錄路徑。測試絕不得觸及真實的機器層級存放區
    /// （<c>C:\ProgramData\PostgresBackup</c>）：那會在開發者機器上實際建立並收緊系統目錄。
    /// </summary>
    private string NewTempStoreDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pg_test_cli_store_{Guid.NewGuid():N}");
        _tempDirectories.Add(path);
        return path;
    }

    private static Mock<IPasswordReader> PasswordReaderReturning(string? password)
    {
        var reader = new Mock<IPasswordReader>();
        reader.Setup(r => r.ReadPassword(It.IsAny<string>())).Returns(password);
        return reader;
    }

    private static Mock<IPasswordReader> StdinPasswordReaderReturning(string? password)
    {
        var reader = new Mock<IPasswordReader>();
        reader.Setup(r => r.ReadPasswordFromStandardInput()).Returns(password);
        return reader;
    }

    /// <summary>連線驗證成功的替身：不觸及真實資料庫。</summary>
    private static Mock<IToolDetectionService> VerifierAccepting()
    {
        var detector = new Mock<IToolDetectionService>();
        detector.Setup(d => d.VerifyConnectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConnectionCheckResult.Success("17.2", 17));
        return detector;
    }

    /// <summary>連線驗證失敗的替身（例如帳號密碼錯誤）。</summary>
    private static Mock<IToolDetectionService> VerifierRejecting(
        string message = "28P01: password authentication failed for user \"svc_backup\"")
    {
        var detector = new Mock<IToolDetectionService>();
        detector.Setup(d => d.VerifyConnectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConnectionCheckResult.Failure(message));
        return detector;
    }

    /// <summary>存放區權限一律回報「查無此目錄」，讓 set 指令的權限檢查保持沉默。</summary>
    private static Mock<IEnvironmentProbe> EnvironmentProbeReportingNoDirectory()
    {
        var probe = new Mock<IEnvironmentProbe>();
        probe.Setup(p => p.GetDirectoryAccessInfo(It.IsAny<string>())).Returns((DirectoryAccessInfo?)null);
        return probe;
    }

    private IServiceProvider BuildServices(
        IConnectionProfileRepository repo,
        IPasswordReader reader,
        IToolDetectionService? detector = null,
        IEnvironmentProbe? environmentProbe = null,
        MachineScopedStoreLocation? storeLocation = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(repo);
        services.AddSingleton(reader);
        services.AddSingleton(detector ?? VerifierAccepting().Object);
        services.AddSingleton(environmentProbe ?? EnvironmentProbeReportingNoDirectory().Object);

        // 存放區位置必須註冊，否則指令會回退到正式的 ProgramData 路徑，
        // 測試就會在開發者機器上真的建立並收緊該系統目錄。
        // 預設不要求限制性權限：套用後非系統管理員的測試行程會失去對自身暫存目錄的存取權。
        services.AddSingleton(storeLocation ?? new MachineScopedStoreLocation(
            NewTempStoreDirectory(), EnforceRestrictivePermissions: false));

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

    [Fact]
    public async Task ProfileSet_WhenSuccessful_ReturnsExitCodeZero()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sp = BuildServices(repo.Object, PasswordReaderReturning(Password).Object);

        var (exitCode, _) = await RunAsync(sp,
            "profile", "set", "--name", "nightly",
            "--host", "db.internal", "--port", "5433",
            "--database", "billing", "--username", "svc_backup");

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task ProfileSet_PersistsNonSecretFieldsAndNoPasswordInFile()
    {
        var location = NewTempStoreLocation();
        var repo = new MachineScopedConnectionProfileRepository(
            new MachineScopedCredentialStorage(location, forceMemoryStorage: true), location);

        var sp = BuildServices(repo, PasswordReaderReturning(Password).Object);

        var (exitCode, output) = await RunAsync(sp,
            "profile", "set", "--name", "nightly",
            "--host", "db.internal", "--port", "5433",
            "--database", "billing", "--username", "svc_backup");

        Assert.Equal(0, exitCode);

        var stored = Assert.Single(await repo.GetAllProfilesAsync());
        Assert.Equal("nightly", stored.Name);
        Assert.Equal("db.internal", stored.Host);
        Assert.Equal(5433, stored.Port);
        Assert.Equal("billing", stored.Database);
        Assert.Equal("svc_backup", stored.Username);
        Assert.Equal(Password, await repo.GetPasswordAsync(stored.Id));

        var fileContent = await File.ReadAllTextAsync(location.ProfilesFilePath);
        Assert.DoesNotContain(Password, fileContent);
        Assert.DoesNotContain(Password, output);
    }

    [Fact]
    public async Task ProfileSet_WithSameName_UpdatesExistingProfileInsteadOfAdding()
    {
        var location = NewTempStoreLocation();
        var repo = new MachineScopedConnectionProfileRepository(
            new MachineScopedCredentialStorage(location, forceMemoryStorage: true), location);

        var sp = BuildServices(repo, PasswordReaderReturning(Password).Object);

        await RunAsync(sp, "profile", "set", "--name", "nightly",
            "--host", "old-host", "--database", "billing", "--username", "old_user");

        var (exitCode, _) = await RunAsync(sp, "profile", "set", "--name", "nightly",
            "--host", "new-host", "--database", "billing", "--username", "new_user");

        Assert.Equal(0, exitCode);

        var stored = Assert.Single(await repo.GetAllProfilesAsync());
        Assert.Equal("new-host", stored.Host);
        Assert.Equal("new_user", stored.Username);
    }

    [Fact]
    public async Task ProfileSet_WhenPasswordEmpty_ReturnsNonZeroAndSavesNothing()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sp = BuildServices(repo.Object, PasswordReaderReturning(string.Empty).Object);

        var (exitCode, output) = await RunAsync(sp,
            "profile", "set", "--name", "nightly", "--database", "billing", "--username", "svc_backup");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("[ERROR]", output);
        repo.Verify(r => r.SaveProfileAsync(
            It.IsAny<ConnectionProfile>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProfileSet_WhenCredentialWriteFails_ReturnsNonZeroAndReportsError()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        repo.Setup(r => r.SaveProfileAsync(
                It.IsAny<ConnectionProfile>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CredentialStorageException("密碼並未儲存。"));

        var sp = BuildServices(repo.Object, PasswordReaderReturning(Password).Object);

        var (exitCode, output) = await RunAsync(sp,
            "profile", "set", "--name", "nightly", "--database", "billing", "--username", "svc_backup");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("[ERROR]", output);
        Assert.DoesNotContain(Password, output);
    }

    [Fact]
    public void ProfileSet_DoesNotExposeAnyPasswordOption()
    {
        var sp = BuildServices(new Mock<IConnectionProfileRepository>().Object,
            new Mock<IPasswordReader>().Object);

        var setCommand = Assert.Single(
            ProfileCommand.Create(sp).Subcommands, c => c.Name == "set");

        // 沒有任何選項叫做 --password/-p。
        Assert.DoesNotContain(setCommand.Options, o =>
            o.Name is "--password" or "-p" || o.Aliases.Any(a => a is "-p" or "--password"));

        // 與密碼有關的選項（--password-stdin）只能是不帶值的旗標：
        // 密碼在任何情況下都不得出現在命令列參數上。
        Assert.All(
            setCommand.Options.Where(o => o.Name.Contains("password", StringComparison.OrdinalIgnoreCase)),
            o => Assert.Equal(typeof(bool), o.ValueType));
    }

    // ---- 票 05：存檔前連線驗證、--force 與 --password-stdin ----

    [Fact]
    public async Task ProfileSet_WhenVerificationSucceeds_SavesProfileAndReturnsZero()
    {
        var location = NewTempStoreLocation();
        var repo = new MachineScopedConnectionProfileRepository(
            new MachineScopedCredentialStorage(location, forceMemoryStorage: true), location);

        var detector = VerifierAccepting();
        var sp = BuildServices(repo, PasswordReaderReturning(Password).Object, detector.Object);

        var (exitCode, output) = await RunAsync(sp,
            "profile", "set", "--name", "nightly",
            "--host", "db.internal", "--database", "billing", "--username", "svc_backup");

        Assert.Equal(0, exitCode);
        var stored = Assert.Single(await repo.GetAllProfilesAsync());
        Assert.Equal("nightly", stored.Name);
        Assert.DoesNotContain(Password, output);
        detector.Verify(d => d.VerifyConnectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProfileSet_WhenVerificationFails_SavesNothingAndReturnsNonZero()
    {
        var location = NewTempStoreLocation();
        var repo = new MachineScopedConnectionProfileRepository(
            new MachineScopedCredentialStorage(location, forceMemoryStorage: true), location);

        var sp = BuildServices(repo, PasswordReaderReturning(Password).Object, VerifierRejecting().Object);

        var (exitCode, output) = await RunAsync(sp,
            "profile", "set", "--name", "nightly",
            "--host", "db.internal", "--database", "billing", "--username", "svc_backup");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("[ERROR]", output);
        Assert.DoesNotContain(Password, output);
        // 拒絕儲存：不只退出碼，連存放區都必須維持空白且不留下檔案。
        Assert.Empty(await repo.GetAllProfilesAsync());
        Assert.False(File.Exists(location.ProfilesFilePath));
    }

    [Fact]
    public async Task ProfileSet_WhenVerificationFails_DoesNotCallSaveProfile()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var sp = BuildServices(repo.Object, PasswordReaderReturning(Password).Object, VerifierRejecting().Object);

        var (exitCode, _) = await RunAsync(sp,
            "profile", "set", "--name", "nightly", "--database", "billing", "--username", "svc_backup");

        Assert.NotEqual(0, exitCode);
        repo.Verify(r => r.SaveProfileAsync(
            It.IsAny<ConnectionProfile>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProfileSet_WithForce_SavesDespiteVerificationFailureAndWarns()
    {
        var location = NewTempStoreLocation();
        var repo = new MachineScopedConnectionProfileRepository(
            new MachineScopedCredentialStorage(location, forceMemoryStorage: true), location);

        var sp = BuildServices(repo, PasswordReaderReturning(Password).Object, VerifierRejecting().Object);

        var (exitCode, output) = await RunAsync(sp,
            "profile", "set", "--name", "nightly",
            "--host", "db.internal", "--database", "billing", "--username", "svc_backup", "--force");

        Assert.Equal(0, exitCode);
        Assert.Single(await repo.GetAllProfilesAsync());
        Assert.Contains("[WARNING]", output);
        Assert.Contains("尚未", output);
        Assert.DoesNotContain(Password, output);
    }

    [Fact]
    public async Task ProfileSet_WithForce_SkipsVerificationEntirely()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var detector = VerifierAccepting();
        var sp = BuildServices(repo.Object, PasswordReaderReturning(Password).Object, detector.Object);

        var (exitCode, _) = await RunAsync(sp,
            "profile", "set", "--name", "nightly", "--database", "billing", "--username", "svc_backup", "--force");

        Assert.Equal(0, exitCode);
        detector.Verify(d => d.VerifyConnectionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProfileSet_WithoutForce_DoesNotWarnAboutUnverifiedProfile()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var sp = BuildServices(repo.Object, PasswordReaderReturning(Password).Object);

        var (exitCode, output) = await RunAsync(sp,
            "profile", "set", "--name", "nightly", "--database", "billing", "--username", "svc_backup");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("[WARNING]", output);
    }

    // ---- 票 06：profile set 亦須在密碼落地「之前」確認存放區檔案權限 ----

    [Fact]
    public async Task ProfileSet_WhenStoreDirectoryPermissionsAreLoose_RefusesToSave()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        // 此測試要求真的走完「收緊權限後再驗證」的流程，因此使用一個
        // 之後不再讀寫的獨立暫存目錄：收緊後本行程可能已無權存取它。
        var storeDirectory = NewTempStoreDirectory();

        var probe = new Mock<IEnvironmentProbe>();
        probe.Setup(p => p.GetDirectoryAccessInfo(storeDirectory)).Returns(new DirectoryAccessInfo
        {
            Path = storeDirectory,
            Exists = true,
            InheritanceEnabled = true, // 仍繼承父目錄權限：同機一般使用者可能讀得到
            AllowedIdentities = [MachineScopedStore.AdministratorsSid, MachineScopedStore.LocalSystemSid]
        });

        var sp = BuildServices(repo.Object, PasswordReaderReturning(Password).Object,
            environmentProbe: probe.Object,
            storeLocation: new MachineScopedStoreLocation(storeDirectory, EnforceRestrictivePermissions: true));

        var (exitCode, output) = await RunAsync(sp,
            "profile", "set", "--name", "nightly", "--database", "billing", "--username", "svc_backup");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("[ERROR]", output);
        Assert.Contains("權限", output);
        Assert.DoesNotContain(Password, output);

        // 重點不在退出碼，而在密碼絕不能落地：權限可疑時連寫入都不得發生。
        repo.Verify(r => r.SaveProfileAsync(
            It.IsAny<ConnectionProfile>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProfileSet_WhenStoreDirectoryPermissionsAreExpected_SavesProfile()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var storeDirectory = NewTempStoreDirectory();

        var probe = new Mock<IEnvironmentProbe>();
        probe.Setup(p => p.GetDirectoryAccessInfo(storeDirectory)).Returns(new DirectoryAccessInfo
        {
            Path = storeDirectory,
            Exists = true,
            InheritanceEnabled = false,
            AllowedIdentities = [MachineScopedStore.AdministratorsSid, MachineScopedStore.LocalSystemSid]
        });

        var sp = BuildServices(repo.Object, PasswordReaderReturning(Password).Object,
            environmentProbe: probe.Object,
            storeLocation: new MachineScopedStoreLocation(storeDirectory, EnforceRestrictivePermissions: true));

        var (exitCode, output) = await RunAsync(sp,
            "profile", "set", "--name", "nightly", "--database", "billing", "--username", "svc_backup");

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("[ERROR]", output);
        Assert.DoesNotContain("[WARNING]", output);
        repo.Verify(r => r.SaveProfileAsync(
            It.IsAny<ConnectionProfile>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProfileSet_WithPasswordStdin_UsesStandardInputAndKeepsPasswordOutOfOutput()
    {
        var location = NewTempStoreLocation();
        var repo = new MachineScopedConnectionProfileRepository(
            new MachineScopedCredentialStorage(location, forceMemoryStorage: true), location);

        var reader = StdinPasswordReaderReturning(Password);
        var sp = BuildServices(repo, reader.Object);

        var (exitCode, output) = await RunAsync(sp,
            "profile", "set", "--name", "nightly",
            "--host", "db.internal", "--database", "billing", "--username", "svc_backup",
            "--password-stdin");

        Assert.Equal(0, exitCode);

        var stored = Assert.Single(await repo.GetAllProfilesAsync());
        Assert.Equal(Password, await repo.GetPasswordAsync(stored.Id));

        // 自動化情境下密碼不得出現在任何輸出或持久化的非機密內容中。
        Assert.DoesNotContain(Password, output);
        Assert.DoesNotContain(Password, await File.ReadAllTextAsync(location.ProfilesFilePath));

        reader.Verify(r => r.ReadPasswordFromStandardInput(), Times.Once);
        reader.Verify(r => r.ReadPassword(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ProfileSet_WithoutPasswordStdin_UsesMaskedInteractiveInput()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var reader = PasswordReaderReturning(Password);
        var sp = BuildServices(repo.Object, reader.Object);

        var (exitCode, _) = await RunAsync(sp,
            "profile", "set", "--name", "nightly", "--database", "billing", "--username", "svc_backup");

        Assert.Equal(0, exitCode);
        reader.Verify(r => r.ReadPasswordFromStandardInput(), Times.Never);
    }

    [Fact]
    public async Task ProfileSet_WithPasswordStdin_WhenNoPasswordProvided_ReturnsNonZeroAndSavesNothing()
    {
        var repo = new Mock<IConnectionProfileRepository>();
        repo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var sp = BuildServices(repo.Object, StdinPasswordReaderReturning(string.Empty).Object);

        var (exitCode, output) = await RunAsync(sp,
            "profile", "set", "--name", "nightly", "--database", "billing",
            "--username", "svc_backup", "--password-stdin");

        Assert.NotEqual(0, exitCode);
        Assert.Contains("[ERROR]", output);
        repo.Verify(r => r.SaveProfileAsync(
            It.IsAny<ConnectionProfile>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ProfileSetHelp_OffersNoOptionAcceptingAPasswordValue()
    {
        var sp = BuildServices(new Mock<IConnectionProfileRepository>().Object,
            new Mock<IPasswordReader>().Object);

        var (_, output) = await RunAsync(sp, "profile", "set", "--help");

        Assert.Contains("--password-stdin", output);
        Assert.Contains("--force", output);
        // --password-stdin 不接受值；說明中不得出現任何接受密碼值的選項。
        Assert.DoesNotContain("--password ", output);
        Assert.DoesNotContain("--password <", output);
        Assert.DoesNotContain("-p,", output);
        Assert.DoesNotContain("-p ", output);
    }
}
