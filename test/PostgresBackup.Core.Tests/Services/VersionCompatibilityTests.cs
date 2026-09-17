using Moq;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;
using Xunit;

namespace PostgresBackup.Core.Tests.Services;

public class VersionCompatibilityTests
{
    private readonly ToolDetectionService _service;

    public VersionCompatibilityTests()
    {
        var mockRunner = new Mock<IProcessRunner>();
        var mockProbe = new Mock<IEnvironmentProbe>();
        _service = new ToolDetectionService(mockRunner.Object, mockProbe.Object);
    }

    [Fact]
    public void CheckCompatibility_WhenClientMajorMatchesServerMajor_ReturnsCompatible()
    {
        var clientVersion = new ToolVersion(16, 4);
        int serverMajor = 16;

        var result = _service.CheckCompatibility(clientVersion, serverMajor, "16.2");

        Assert.True(result.IsCompatible);
        Assert.Equal(clientVersion, result.ClientVersion);
        Assert.Equal(16, result.ServerMajorVersion);
        Assert.Contains("相容", result.Message);
    }

    [Fact]
    public void CheckCompatibility_WhenClientMajorHigherThanServerMajor_ReturnsCompatible()
    {
        var clientVersion = new ToolVersion(17, 1);
        int serverMajor = 16;

        var result = _service.CheckCompatibility(clientVersion, serverMajor, "16.4");

        Assert.True(result.IsCompatible);
        Assert.Equal(clientVersion, result.ClientVersion);
        Assert.Equal(16, result.ServerMajorVersion);
        Assert.Contains("相容", result.Message);
    }

    [Fact]
    public void CheckCompatibility_WhenClientMajorLowerThanServerMajor_ReturnsIncompatible()
    {
        var clientVersion = new ToolVersion(15, 3);
        int serverMajor = 16;

        var result = _service.CheckCompatibility(clientVersion, serverMajor, "16.4");

        Assert.False(result.IsCompatible);
        Assert.Equal(clientVersion, result.ClientVersion);
        Assert.Equal(16, result.ServerMajorVersion);
        Assert.Contains("不相容", result.Message);
        Assert.Contains("低於", result.Message);
    }
}
