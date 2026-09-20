using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

/// <summary>
/// 構建器回傳未跳脫的 argv 元素清單，因此斷言比對的是元素本身而非命令列外觀。
/// 連線參數與引號規則改由客戶端工具作業負責，其行為由 <see cref="ClientToolRunTests"/> 守住。
/// </summary>
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

        Assert.Contains("-Fc", args);
        Assert.Contains("-v", args);
        Assert.Equal(["-f", @"C:\backups\test.dump"], args.TakeLast(2));
        Assert.DoesNotContain("--schema-only", args);
        Assert.DoesNotContain("--data-only", args);

        // 連線參數不由構建器產生，由客戶端工具作業補在前方。
        Assert.DoesNotContain("-h", args);
        Assert.DoesNotContain("-p", args);
        Assert.DoesNotContain("-U", args);
        Assert.DoesNotContain("-d", args);
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

        Assert.Equal(["-n", "public", "-n", "audit_log"], ArgumentPairs(args, "-n"));
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

        Assert.Equal(["-t", "users", "-t", "orders"], ArgumentPairs(args, "-t"));
    }

    /// <summary>取出所有 <paramref name="flag"/> 旗標與其緊接之值，保持原有順序。</summary>
    private static List<string> ArgumentPairs(IReadOnlyList<string> args, string flag)
    {
        var pairs = new List<string>();
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] != flag) continue;
            pairs.Add(args[i]);
            pairs.Add(args[i + 1]);
        }
        return pairs;
    }
}
