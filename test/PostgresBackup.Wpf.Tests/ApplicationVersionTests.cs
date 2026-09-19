using PostgresBackup.Wpf.Services;

namespace PostgresBackup.Wpf.Tests;

public class ApplicationVersionTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta.1", "1.2.3-beta.1")]
    [InlineData("1.2.3+abc123", "1.2.3")]
    [InlineData("1.2.3-beta.1+abc123", "1.2.3-beta.1")]
    [InlineData("v1.2.3", "1.2.3")]
    public void Normalize_preserves_release_identity_and_removes_build_metadata(
        string informationalVersion,
        string expected)
    {
        Assert.Equal(expected, ApplicationVersion.Normalize(informationalVersion));
    }
}
