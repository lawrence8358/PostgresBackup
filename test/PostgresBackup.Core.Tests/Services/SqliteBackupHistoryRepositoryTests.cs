using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

public class SqliteBackupHistoryRepositoryTests : IAsyncDisposable
{
    private readonly SqliteBackupHistoryRepository _repo;

    public SqliteBackupHistoryRepositoryTests()
    {
        // 使用 SQLite 記憶體資料庫
        _repo = new SqliteBackupHistoryRepository(":memory:");
    }

    public async ValueTask DisposeAsync()
    {
        await _repo.DisposeAsync();
    }

    [Fact]
    public async Task AddRecordAsync_And_GetRecordsAsync_ReturnsSavedRecordsOrdered()
    {
        var record1 = new BackupRecord
        {
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10),
            OperationType = BackupOperationType.Backup,
            DatabaseName = "app_db",
            FilePath = @"C:\backups\app_db_1.dump",
            Format = BackupFormat.Custom,
            FileSizeBytes = 1048576,
            DurationMs = 2500,
            Status = BackupStatus.Success,
            Arguments = "-Fc -d app_db"
        };

        var record2 = new BackupRecord
        {
            Timestamp = DateTimeOffset.UtcNow,
            OperationType = BackupOperationType.Restore,
            DatabaseName = "app_db",
            TargetDatabase = "app_db_restore",
            FilePath = @"C:\backups\app_db_1.dump",
            Format = BackupFormat.Custom,
            FileSizeBytes = 1048576,
            DurationMs = 3100,
            Status = BackupStatus.Success,
            Arguments = "-d app_db_restore"
        };

        var added1 = await _repo.AddRecordAsync(record1);
        var added2 = await _repo.AddRecordAsync(record2);

        Assert.True(added1.Id > 0);
        Assert.True(added2.Id > added1.Id);

        var all = await _repo.GetRecordsAsync();
        Assert.Equal(2, all.Count);
        // 驗證最新紀錄排在最前面 (timestamp DESC)
        Assert.Equal(added2.Id, all[0].Id);
        Assert.Equal(added1.Id, all[1].Id);
    }

    [Fact]
    public async Task GetRecordsAsync_WithFilter_FiltersCorrectly()
    {
        await _repo.AddRecordAsync(new BackupRecord
        {
            DatabaseName = "crm",
            OperationType = BackupOperationType.Backup,
            Status = BackupStatus.Success,
            FilePath = "crm.dump"
        });

        await _repo.AddRecordAsync(new BackupRecord
        {
            DatabaseName = "erp",
            OperationType = BackupOperationType.Backup,
            Status = BackupStatus.Failed,
            FilePath = "erp.dump"
        });

        await _repo.AddRecordAsync(new BackupRecord
        {
            DatabaseName = "crm",
            OperationType = BackupOperationType.PreRestoreSnapshot,
            Status = BackupStatus.Success,
            FilePath = "crm_snap.dump"
        });

        // 篩選 DatabaseName == "crm"
        var crmRecords = await _repo.GetRecordsAsync(new BackupHistoryFilter { DatabaseName = "crm" });
        Assert.Equal(2, crmRecords.Count);

        // 篩選 OperationType == PreRestoreSnapshot
        var snapRecords = await _repo.GetRecordsAsync(new BackupHistoryFilter { OperationType = BackupOperationType.PreRestoreSnapshot });
        Assert.Single(snapRecords);
        Assert.Equal("crm_snap.dump", snapRecords[0].FilePath);

        // 篩選 Status == Failed
        var failedRecords = await _repo.GetRecordsAsync(new BackupHistoryFilter { Status = BackupStatus.Failed });
        Assert.Single(failedRecords);
        Assert.Equal("erp", failedRecords[0].DatabaseName);
    }

    [Fact]
    public async Task DeleteRecordAsync_RemovesRecordSuccessfully()
    {
        var record = await _repo.AddRecordAsync(new BackupRecord
        {
            DatabaseName = "temp_db",
            FilePath = "temp.dump"
        });

        var deleted = await _repo.DeleteRecordAsync(record.Id);
        Assert.True(deleted);

        var retrieved = await _repo.GetRecordByIdAsync(record.Id);
        Assert.Null(retrieved);
    }
}
