using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

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

        Assert.Contains("--use-list \"C:\\temp\\restore.list\"", args);
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
        Assert.Contains("--use-list \"C:\\temp\\data-restore.list\"", args);
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

        Assert.Contains("-h \"localhost\"", args);
        Assert.Contains("-p 5432", args);
        Assert.Contains("-U \"postgres\"", args);
        Assert.Contains("-d \"target_db\"", args);
        Assert.Contains("--clean --if-exists", args);
        Assert.Contains("--exit-on-error", args);
        Assert.DoesNotContain("--create", args);
        Assert.Contains("-v", args);
        Assert.Contains(@"""C:\backups\mybackup.dump""", args);
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

        Assert.Contains("-h \"10.0.0.1\"", args);
        Assert.Contains("-d \"target_db\"", args);
        Assert.Contains(@"-f ""C:\backups\script.sql""", args);
        Assert.DoesNotContain("--clean", args);
    }
}
