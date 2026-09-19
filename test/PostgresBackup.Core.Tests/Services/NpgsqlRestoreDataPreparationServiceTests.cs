using PostgresBackup.Core.Models;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

public class NpgsqlRestoreDataPreparationServiceTests
{
    [Fact]
    public void BuildCommandText_QuotesIdentifiersAndNeverCascadesOutsideArchive()
    {
        RestoreTableIdentity[] tables =
        [
            new("Odd Schema", "Order"),
            new("public", "Quote")
        ];

        var sql = NpgsqlRestoreDataPreparationService.BuildCommandText(tables);

        Assert.Equal(
            "TRUNCATE TABLE \"Odd Schema\".\"Order\", \"public\".\"Quote\" RESTART IDENTITY;",
            sql);
        Assert.DoesNotContain("CASCADE", sql, StringComparison.OrdinalIgnoreCase);
    }
}
