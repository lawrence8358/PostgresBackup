using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

public class RestoreTargetDatabaseTests
{
    [Fact]
    public void CleanAndRecreate_restores_into_the_selected_existing_database()
    {
        var options = new RestoreOptions
        {
            Connection = new ConnectionSettings
            {
                Host = "localhost",
                Port = 5432,
                Username = "postgres",
                Database = "stock_analysis2"
            },
            TargetDatabase = "stock_analysis2",
            SourceFilePath = @"C:\backups\stock_analysis.dump",
            Format = BackupFormat.Custom,
            Mode = RestoreMode.CleanAndRecreate
        };

        var args = RestoreArgumentsBuilder.Build(options);

        Assert.Contains("-d \"stock_analysis2\"", args);
        Assert.Contains("--clean --if-exists", args);
        Assert.DoesNotContain("--create", args);
    }
}
