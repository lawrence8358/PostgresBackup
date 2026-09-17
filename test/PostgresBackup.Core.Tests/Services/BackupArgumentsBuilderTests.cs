using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

public class BackupArgumentsBuilderTests
{
    [Fact]
    public void Build_CustomFormatFullDatabase_ContainsExpectedFlags()
    {
        var options = new BackupOptions
        {
            Connection = new ConnectionSettings
            {
                Host = "localhost",
                Port = 5432,
                Username = "postgres",
                Database = "testdb"
            },
            Format = BackupFormat.Custom,
            Mode = BackupMode.SchemaAndData,
            Scope = BackupScope.FullDatabase
        };

        var args = BackupArgumentsBuilder.Build(options, @"C:\backups\test.dump");

        Assert.Contains("-h \"localhost\"", args);
        Assert.Contains("-p 5432", args);
        Assert.Contains("-U \"postgres\"", args);
        Assert.Contains("-d \"testdb\"", args);
        Assert.Contains("-Fc", args);
        Assert.Contains("-v", args);
        Assert.Contains(@"-f ""C:\backups\test.dump""", args);
        Assert.DoesNotContain("--schema-only", args);
        Assert.DoesNotContain("--data-only", args);
    }

    [Fact]
    public void Build_PlainFormatSchemaOnly_ContainsSchemaOnlyAndFp()
    {
        var options = new BackupOptions
        {
            Connection = new ConnectionSettings { Database = "testdb" },
            Format = BackupFormat.Plain,
            Mode = BackupMode.SchemaOnly,
            Scope = BackupScope.FullDatabase
        };

        var args = BackupArgumentsBuilder.Build(options, @"C:\backups\schema.sql");

        Assert.Contains("-Fp", args);
        Assert.Contains("--schema-only", args);
        Assert.DoesNotContain("-Fc", args);
    }

    [Fact]
    public void Build_SpecificSchemas_AppendsSchemaFlags()
    {
        var options = new BackupOptions
        {
            Connection = new ConnectionSettings { Database = "testdb" },
            Scope = BackupScope.SpecificSchemas,
            Schemas = ["public", "audit_log"]
        };

        var args = BackupArgumentsBuilder.Build(options, @"C:\backups\schemas.dump");

        Assert.Contains("-n \"public\"", args);
        Assert.Contains("-n \"audit_log\"", args);
    }

    [Fact]
    public void Build_SpecificTables_AppendsTableFlags()
    {
        var options = new BackupOptions
        {
            Connection = new ConnectionSettings { Database = "testdb" },
            Scope = BackupScope.SpecificTables,
            Tables = ["users", "orders"]
        };

        var args = BackupArgumentsBuilder.Build(options, @"C:\backups\tables.dump");

        Assert.Contains("-t \"users\"", args);
        Assert.Contains("-t \"orders\"", args);
    }
}
