using Moq;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;
using Xunit;

namespace PostgresBackup.Core.Tests.Services;

/// <summary>
/// 備份與還原作業必須拒絕以完整連線字串描述的連線。
///
/// 原因不是「不支援」這麼單純：<see cref="ConnectionSettings.ToConnectionString"/> 會優先
/// 採用連線字串，但兩個參數構建器只讀主機／連接埠／使用者／資料庫欄位。還原路徑同時
/// 走這兩條 —— 目標資料庫的檢查與清空經由前者、pg_restore 經由後者 —— 因此兩者若指向
/// 不同伺服器，一次還原會檢查並清空某一台，卻把資料寫進另一台。
///
/// 目前沒有任何進入點會在備份或還原的連線設定上填入連線字串（僅 check-tools 的
/// -s 會填，且不流向此處），這些測試因此是防止該入口被打開時悄悄造成上述後果。
/// </summary>
public class ConnectionStringRejectionTests
{
    private static ConnectionSettings WithConnectionString() => new()
    {
        Host = "db-a",
        Port = 5432,
        Database = "mydb",
        Username = "postgres",
        ConnectionString = "Host=db-b;Port=5432;Database=mydb;Username=postgres"
    };

    [Fact]
    public async Task BackupAsync_WhenConnectionCarriesConnectionString_FailsWithoutDetectingTools()
    {
        var runner = new Mock<IProcessRunner>(MockBehavior.Strict);
        var detector = new Mock<IToolDetectionService>(MockBehavior.Strict);

        var service = new BackupService(new ClientToolRun(runner.Object), detector.Object);

        var result = await service.BackupAsync(new BackupOptions
        {
            Connection = WithConnectionString(),
            OutputDirectory = Path.GetTempPath()
        });

        Assert.False(result.IsSuccess);
        Assert.Equal(-1, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));

        // 嚴格模式的替身會在任何未設定的呼叫上失敗，因此這同時證明了守門
        // 發生在工具偵測與處理序執行之前。
        detector.VerifyNoOtherCalls();
        runner.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task RestoreAsync_WhenConnectionCarriesConnectionString_FailsWithoutTouchingTargetDatabase()
    {
        var runner = new Mock<IProcessRunner>(MockBehavior.Strict);
        var detector = new Mock<IToolDetectionService>(MockBehavior.Strict);
        var backupService = new Mock<IBackupService>(MockBehavior.Strict);
        var catalogReader = new Mock<IRestoreTargetCatalogReader>(MockBehavior.Strict);
        var dataPreparation = new Mock<IRestoreDataPreparationService>(MockBehavior.Strict);

        var sourceFile = Path.Combine(Path.GetTempPath(), $"cs-reject-{Guid.NewGuid():N}.dump");
        await File.WriteAllTextAsync(sourceFile, "not a real archive");

        try
        {
            var service = new RestoreService(
                new ClientToolRun(runner.Object),
                detector.Object,
                backupService.Object,
                catalogReader: catalogReader.Object,
                dataPreparationService: dataPreparation.Object);

            var result = await service.RestoreAsync(new RestoreOptions
            {
                Connection = WithConnectionString(),
                SourceFilePath = sourceFile,
                TargetDatabase = "mydb",
                Mode = RestoreMode.DataOnly
            });

            Assert.False(result.IsSuccess);
            Assert.Equal(-1, result.ExitCode);
            Assert.False(string.IsNullOrWhiteSpace(result.ErrorMessage));

            // 最重要的一項：清空資料表的那條路從未被走到。
            dataPreparation.VerifyNoOtherCalls();
            catalogReader.VerifyNoOtherCalls();
            backupService.VerifyNoOtherCalls();
            detector.VerifyNoOtherCalls();
            runner.VerifyNoOtherCalls();
        }
        finally
        {
            File.Delete(sourceFile);
        }
    }

    [Fact]
    public async Task BackupAsync_WhenConnectionStringIsBlank_IsNotRejected()
    {
        var detector = new Mock<IToolDetectionService>();
        detector
            .Setup(d => d.DetectAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ToolDetectionResult { Status = ToolStatus.NotFound });

        var service = new BackupService(new ClientToolRun(new Mock<IProcessRunner>().Object), detector.Object);

        var result = await service.BackupAsync(new BackupOptions
        {
            Connection = new ConnectionSettings { ConnectionString = "   " },
            OutputDirectory = Path.GetTempPath()
        });

        // 仍然失敗，但原因必須是找不到客戶端工具而非連線字串守門 ——
        // 空白字串不算「帶有連線字串」。
        Assert.False(result.IsSuccess);
        detector.Verify(
            d => d.DetectAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
