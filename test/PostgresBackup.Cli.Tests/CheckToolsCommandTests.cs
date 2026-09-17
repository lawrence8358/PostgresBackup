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
}
