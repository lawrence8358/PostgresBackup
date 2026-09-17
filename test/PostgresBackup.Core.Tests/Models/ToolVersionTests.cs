using PostgresBackup.Core.Models;
using Xunit;

namespace PostgresBackup.Core.Tests.Models;

public class ToolVersionTests
{
    [Theory]
    [InlineData("pg_dump (PostgreSQL) 16.4", 16, 4, null)]
    [InlineData("pg_dump (PostgreSQL) 17.2", 17, 2, null)]
    [InlineData("pg_dump (PostgreSQL) 15.3 (Debian 15.3-0+deb12u1)", 15, 3, null)]
    [InlineData("pg_restore (PostgreSQL) 16.1", 16, 1, null)]
    [InlineData("psql (PostgreSQL) 14.5.1", 14, 5, 1)]
    [InlineData("16.4", 16, 4, null)]
    [InlineData("17.0", 17, 0, null)]
    [InlineData("PostgreSQL 16.4 on x86_64-pc-linux-gnu", 16, 4, null)]
    public void TryParse_ValidInputs_ParsesExpectedVersions(string raw, int expectedMajor, int expectedMinor, int? expectedPatch)
    {
        bool success = ToolVersion.TryParse(raw, out var version);

        Assert.True(success);
        Assert.NotNull(version);
        Assert.Equal(expectedMajor, version.Major);
        Assert.Equal(expectedMinor, version.Minor);
        Assert.Equal(expectedPatch, version.Patch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("random string without numbers")]
    public void TryParse_InvalidInputs_ReturnsFalse(string? raw)
    {
        bool success = ToolVersion.TryParse(raw, out var version);

        Assert.False(success);
        Assert.Null(version);
    }

    [Fact]
    public void CompareTo_ComparesVersionsCorrectly()
    {
        var v15 = new ToolVersion(15, 4);
        var v16 = new ToolVersion(16, 0);
        var v16_4 = new ToolVersion(16, 4);
        var v16_4_1 = new ToolVersion(16, 4, 1);

        Assert.True(v15 < v16);
        Assert.True(v16 < v16_4);
        Assert.True(v16_4 < v16_4_1);
        Assert.True(v16_4_1 >= v16_4);
        Assert.True(v16 == new ToolVersion(16, 0));
    }
}
