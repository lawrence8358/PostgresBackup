using System.Runtime.InteropServices;
using Moq;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

/// <summary>
/// 檔案權限查詢接縫的測試。
/// 真實的限制性權限套用與放寬後的警告屬整合測試範圍，不簽入版控。
/// </summary>
public class EnvironmentProbeDirectoryAccessTests
{
    [Fact]
    public void GetDirectoryAccessInfo_WhenDirectoryMissing_ReportsNotExists()
    {
        var probe = new WindowsEnvironmentProbe();
        var missing = Path.Combine(Path.GetTempPath(), $"pg_test_missing_{Guid.NewGuid():N}");

        var info = probe.GetDirectoryAccessInfo(missing);

        Assert.NotNull(info);
        Assert.False(info.Exists);
    }

    [Fact]
    public void GetDirectoryAccessInfo_WhenDirectoryExists_ReportsExists()
    {
        var probe = new WindowsEnvironmentProbe();
        var dir = Path.Combine(Path.GetTempPath(), $"pg_test_acl_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var info = probe.GetDirectoryAccessInfo(dir);

            // 權限不足以讀取 ACL 時介面允許回傳 null；其餘情況應回報目錄存在。
            if (info != null)
            {
                Assert.True(info.Exists);
                Assert.Equal(dir, info.Path);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Assert.NotEmpty(info.AllowedIdentities);
                }
            }
        }
        finally
        {
            try { Directory.Delete(dir); } catch { }
        }
    }

    [Fact]
    public void GetDirectoryAccessInfo_IsMockable()
    {
        // 票 06 的權限警告測試需要能以替身控制此查詢結果。
        var probe = new Mock<IEnvironmentProbe>();
        probe.Setup(p => p.GetDirectoryAccessInfo(It.IsAny<string>()))
            .Returns(new DirectoryAccessInfo
            {
                Path = @"C:\ProgramData\PostgresBackup",
                Exists = true,
                InheritanceEnabled = true,
                AllowedIdentities = ["S-1-1-0"]
            });

        var info = probe.Object.GetDirectoryAccessInfo(@"C:\ProgramData\PostgresBackup");

        Assert.NotNull(info);
        Assert.True(info.InheritanceEnabled);
        Assert.Contains("S-1-1-0", info.AllowedIdentities);
    }
}
