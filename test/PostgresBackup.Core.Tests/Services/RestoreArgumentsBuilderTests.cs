using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

/// <summary>
/// 構建器回傳未跳脫的 argv 元素清單，因此斷言比對的是元素本身而非命令列外觀。
/// 連線參數與引號規則改由客戶端工具作業負責，其行為由 <see cref="ClientToolRunTests"/> 守住。
/// </summary>
public class RestoreArgumentsBuilderTests
{
    [Fact]
    public void Build_CustomFormatNormal_RequiresFilteredArchiveList()
    {
        var options = new RestoreOptions
        {
            Connection = new ConnectionSettings { Database = "target_db" },
            SourceFilePath = @"C:\backups\mybackup.dump",
            Format = BackupFormat.Custom,
            Mode = RestoreMode.Normal
        };

        Assert.Throws<InvalidOperationException>(() => RestoreArgumentsBuilder.Build(options));
    }

    [Fact]
    public void Build_CustomFormatNormal_UsesFilteredArchiveListAndProtectsExistingTableData()
    {
        var options = new RestoreOptions
        {
            Connection = new ConnectionSettings { Database = "target_db" },
            SourceFilePath = @"C:\backups\mybackup.dump",
            Format = BackupFormat.Custom,
            Mode = RestoreMode.Normal
        };

        var args = RestoreArgumentsBuilder.Build(options, @"C:\temp\restore.list");

        Assert.Equal(["--use-list", @"C:\temp\restore.list"], args.Take(2));
        Assert.Contains("--no-data-for-failed-tables", args);
        Assert.Contains("--exit-on-error", args);
        Assert.DoesNotContain("--clean", args);
    }

    [Fact]
    public void Build_CustomFormatDataOnly_RequiresOrderedArchiveList()
    {
        var options = new RestoreOptions
        {
            Connection = new ConnectionSettings { Database = "target_db" },
            SourceFilePath = @"C:\backups\mybackup.dump",
            Format = BackupFormat.Custom,
            Mode = RestoreMode.DataOnly
        };

        Assert.Throws<InvalidOperationException>(() => RestoreArgumentsBuilder.Build(options));

        var args = RestoreArgumentsBuilder.Build(options, @"C:\temp\data-restore.list");

        Assert.Contains("--data-only", args);
        Assert.Equal(["--use-list", @"C:\temp\data-restore.list"], args.SkipWhile(a => a != "--use-list").Take(2));
        Assert.Contains("--exit-on-error", args);
    }

    [Fact]
    public void Build_CustomFormatCleanAndRecreate_ContainsExpectedFlags()
    {
        var options = new RestoreOptions
        {
            Connection = new ConnectionSettings
            {
                Host = "localhost",
                Port = 5432,
                Username = "postgres",
                Database = "target_db"
            },
            SourceFilePath = @"C:\backups\mybackup.dump",
            Format = BackupFormat.Custom,
            Mode = RestoreMode.CleanAndRecreate
        };

        var args = RestoreArgumentsBuilder.Build(options);

        Assert.Equal(["--clean", "--if-exists"], args.Take(2));
        Assert.Contains("--exit-on-error", args);
        Assert.DoesNotContain("--create", args);
        Assert.Contains("-v", args);
        Assert.Equal(@"C:\backups\mybackup.dump", args[^1]);

        // 目標資料庫由客戶端工具作業以 -d 補上，構建器不產生連線參數。
        Assert.DoesNotContain("-d", args);
        Assert.DoesNotContain("target_db", args);
    }

    [Fact]
    public void Build_PlainFormatSql_ContainsPsqlFileFlag()
    {
        var options = new RestoreOptions
        {
            Connection = new ConnectionSettings
            {
                Host = "10.0.0.1",
                Port = 5432,
                Username = "dbadmin",
                Database = "target_db"
            },
            SourceFilePath = @"C:\backups\script.sql",
            Format = BackupFormat.Plain
        };

        var args = RestoreArgumentsBuilder.Build(options);

        Assert.Equal(["-f", @"C:\backups\script.sql"], args);
        Assert.DoesNotContain("--clean", args);
    }
}
