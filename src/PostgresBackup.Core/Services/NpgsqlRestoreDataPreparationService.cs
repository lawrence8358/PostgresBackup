using System.Text;
using Npgsql;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Services;

/// <summary>
/// Prepares existing tables for a deterministic data-only restore. All tables are
/// truncated in one statement so foreign keys within the archive are handled as a set.
/// CASCADE is intentionally not used because it could clear tables outside the archive.
/// </summary>
public sealed class NpgsqlRestoreDataPreparationService : IRestoreDataPreparationService
{
    public async Task ClearTablesAsync(
        ConnectionSettings connection,
        string targetDatabase,
        IReadOnlyList<RestoreTableIdentity> tables,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDatabase);
        ArgumentNullException.ThrowIfNull(tables);
        if (tables.Count == 0) return;

        var connectionBuilder = new NpgsqlConnectionStringBuilder(connection.ToConnectionString())
        {
            Database = targetDatabase
        };

        var sql = BuildCommandText(tables);

        await using var db = new NpgsqlConnection(connectionBuilder.ConnectionString);
        await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand(sql, db);
        await command.ExecuteNonQueryAsync(ct);
    }

    internal static string BuildCommandText(IReadOnlyList<RestoreTableIdentity> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);
        if (tables.Count == 0) throw new ArgumentException("At least one table is required.", nameof(tables));

        var sql = new StringBuilder("TRUNCATE TABLE ");
        for (var i = 0; i < tables.Count; i++)
        {
            if (i > 0) sql.Append(", ");
            sql.Append(QuoteIdentifier(tables[i].Schema))
                .Append('.')
                .Append(QuoteIdentifier(tables[i].Name));
        }
        sql.Append(" RESTART IDENTITY;");
        return sql.ToString();
    }

    private static string QuoteIdentifier(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
