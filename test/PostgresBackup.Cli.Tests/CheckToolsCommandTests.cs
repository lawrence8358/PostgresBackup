using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PostgresBackup.Cli.Commands;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using Xunit;

namespace PostgresBackup.Cli.Tests;

public class CheckToolsCommandTests
{
    [Fact]
    public async Task CheckToolsCommand_WhenToolsReady_ExitsWithCodeZero()
    {
        var mockDetector = new Mock<IToolDetectionService>();
        mockDetector.Setup(d => d.DetectAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolDetectionResult.CreateFound(
                @"C:\Program Files\PostgreSQL\16\bin\pg_dump.exe",
                @"C:\Program Files\PostgreSQL\16\bin\pg_restore.exe",
                @"C:\Program Files\PostgreSQL\16\bin\psql.exe",
                new ToolVersion(16, 4),
                DetectionSource.CommonDirectory));

        var services = new ServiceCollection();
        services.AddSingleton(mockDetector.Object);
        var sp = services.BuildServiceProvider();

        var root = new RootCommand { CheckToolsCommand.Create(sp) };

        int exitCode = await root.Parse(new[] { "check-tools" }).InvokeAsync(new InvocationConfiguration(), CancellationToken.None);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public async Task CheckToolsCommand_WhenToolsNotFound_ExitsWithCodeOne()
    {
        var mockDetector = new Mock<IToolDetectionService>();
        mockDetector.Setup(d => d.DetectAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolDetectionResult.CreateNotFound("Not found"));

        var services = new ServiceCollection();
        services.AddSingleton(mockDetector.Object);
        var sp = services.BuildServiceProvider();

        var root = new RootCommand { CheckToolsCommand.Create(sp) };

        int exitCode = await root.Parse(new[] { "check-tools" }).InvokeAsync(new InvocationConfiguration(), CancellationToken.None);

        Assert.Equal(1, exitCode);
    }

    [Fact]
    public async Task CheckToolsCommand_WhenVersionIncompatible_ExitsWithCodeTwo()
    {
        var mockDetector = new Mock<IToolDetectionService>();
        var foundTools = ToolDetectionResult.CreateFound(
            @"C:\Program Files\PostgreSQL\15\bin\pg_dump.exe",
            @"C:\Program Files\PostgreSQL\15\bin\pg_restore.exe",
            @"C:\Program Files\PostgreSQL\15\bin\psql.exe",
            new ToolVersion(15, 2),
            DetectionSource.CommonDirectory);

        mockDetector.Setup(d => d.DetectAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(foundTools);

        mockDetector.Setup(d => d.CheckCompatibilityAsync(foundTools, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(VersionCheckResult.Incompatible(new ToolVersion(15, 2), 16, "16.4"));

        var services = new ServiceCollection();
        services.AddSingleton(mockDetector.Object);
        var sp = services.BuildServiceProvider();

        var root = new RootCommand { CheckToolsCommand.Create(sp) };

        int exitCode = await root.Parse(new[] { "check-tools", "--database", "testdb" }).InvokeAsync(new InvocationConfiguration(), CancellationToken.None);

        Assert.Equal(2, exitCode);
    }

    [Fact]
    public async Task CheckToolsCommand_WhenPasswordOptionUsed_WritesExposureWarning()
    {
        var mockDetector = new Mock<IToolDetectionService>();
        mockDetector.Setup(d => d.DetectAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolDetectionResult.CreateFound(
                @"C:\Program Files\PostgreSQL\16\bin\pg_dump.exe",
                @"C:\Program Files\PostgreSQL\16\bin\pg_restore.exe",
                @"C:\Program Files\PostgreSQL\16\bin\psql.exe",
                new ToolVersion(16, 4),
                DetectionSource.CommonDirectory));

        var services = new ServiceCollection();
        services.AddSingleton(mockDetector.Object);
        var sp = services.BuildServiceProvider();

        var root = new RootCommand { CheckToolsCommand.Create(sp) };

        var originalOut = Console.Out;
        var writer = new StringWriter();
        Console.SetOut(writer);
        try
        {
            await root.Parse(new[] { "check-tools", "-p", "secret" }).InvokeAsync(new InvocationConfiguration(), CancellationToken.None);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        Assert.Contains("WARNING", writer.ToString());
        Assert.Contains("暴露", writer.ToString());
    }

    [Fact]
    public async Task CheckToolsCommand_WhenProfileNotFound_ReturnsNonZeroExitCode_WithoutDetectingTools()
    {
        var mockDetector = new Mock<IToolDetectionService>();
        var mockRepo = new Mock<IConnectionProfileRepository>();
        mockRepo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConnectionProfile>());

        var services = new ServiceCollection();
        services.AddSingleton(mockDetector.Object);
        services.AddSingleton(mockRepo.Object);
        var sp = services.BuildServiceProvider();

        var root = new RootCommand { CheckToolsCommand.Create(sp) };

        int exitCode = await root.Parse(new[] { "check-tools", "--profile", "not-exist" }).InvokeAsync(new InvocationConfiguration(), CancellationToken.None);

        Assert.NotEqual(0, exitCode);
        mockDetector.Verify(d => d.DetectAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CheckToolsCommand_WhenProfileFound_UsesStoredConnectionSettings()
    {
        var mockDetector = new Mock<IToolDetectionService>();
        var foundTools = ToolDetectionResult.CreateFound(
            @"C:\Program Files\PostgreSQL\16\bin\pg_dump.exe",
            @"C:\Program Files\PostgreSQL\16\bin\pg_restore.exe",
            @"C:\Program Files\PostgreSQL\16\bin\psql.exe",
            new ToolVersion(16, 4),
            DetectionSource.CommonDirectory);

        mockDetector.Setup(d => d.DetectAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(foundTools);

        string? capturedConnString = null;
        mockDetector.Setup(d => d.CheckCompatibilityAsync(foundTools, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<ToolDetectionResult, string, CancellationToken>((_, connStr, _) => capturedConnString = connStr)
            .ReturnsAsync(VersionCheckResult.Compatible(new ToolVersion(16, 4), 16, "16.4"));

        var profile = new ConnectionProfile
        {
            Id = "profile-1",
            Name = "my-profile",
            Host = "db.internal",
            Port = 5433,
            Database = "profiledb",
            Username = "profileuser"
        };

        var mockRepo = new Mock<IConnectionProfileRepository>();
        mockRepo.Setup(r => r.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConnectionProfile> { profile });
        mockRepo.Setup(r => r.GetPasswordAsync("profile-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync("stored-secret");

        var services = new ServiceCollection();
        services.AddSingleton(mockDetector.Object);
        services.AddSingleton(mockRepo.Object);
        var sp = services.BuildServiceProvider();

        var root = new RootCommand { CheckToolsCommand.Create(sp) };

        int exitCode = await root.Parse(new[] { "check-tools", "--profile", "my-profile" }).InvokeAsync(new InvocationConfiguration(), CancellationToken.None);

        Assert.Equal(0, exitCode);
        mockDetector.Verify(d => d.CheckCompatibilityAsync(foundTools, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(capturedConnString);
        Assert.Contains("db.internal", capturedConnString);
        Assert.Contains("profiledb", capturedConnString);
        Assert.Contains("profileuser", capturedConnString);
    }
}
