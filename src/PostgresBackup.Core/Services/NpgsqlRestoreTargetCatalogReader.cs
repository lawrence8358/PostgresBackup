using Npgsql;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Services;

/// <summary>Reads target object identities from PostgreSQL system catalogs in one round trip.</summary>
public sealed class NpgsqlRestoreTargetCatalogReader : IRestoreTargetCatalogReader
{
    private const string CatalogSql = """
        SELECT 'schema', n.nspname, '', ''
        FROM pg_catalog.pg_namespace n
        UNION ALL
        SELECT 'relation', n.nspname, c.relname, ''
        FROM pg_catalog.pg_class c
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        UNION ALL
        SELECT 'constraint', n.nspname, c.relname, con.conname
        FROM pg_catalog.pg_constraint con
        JOIN pg_catalog.pg_class c ON c.oid = con.conrelid
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        UNION ALL
        SELECT 'default', n.nspname, c.relname, a.attname
        FROM pg_catalog.pg_attrdef d
        JOIN pg_catalog.pg_class c ON c.oid = d.adrelid
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_catalog.pg_attribute a ON a.attrelid = c.oid AND a.attnum = d.adnum
        UNION ALL
        SELECT 'trigger', n.nspname, c.relname, t.tgname
        FROM pg_catalog.pg_trigger t
        JOIN pg_catalog.pg_class c ON c.oid = t.tgrelid
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        WHERE NOT t.tgisinternal
        UNION ALL
        SELECT 'rule', n.nspname, c.relname, r.rulename
        FROM pg_catalog.pg_rewrite r
        JOIN pg_catalog.pg_class c ON c.oid = r.ev_class
        JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
        WHERE r.rulename <> '_RETURN'
        UNION ALL
        SELECT 'routine', n.nspname,
               p.proname || '(' || pg_catalog.pg_get_function_identity_arguments(p.oid) || ')', ''
        FROM pg_catalog.pg_proc p
        JOIN pg_catalog.pg_namespace n ON n.oid = p.pronamespace
        UNION ALL
        SELECT 'type', n.nspname, t.typname, ''
        FROM pg_catalog.pg_type t
        JOIN pg_catalog.pg_namespace n ON n.oid = t.typnamespace
        UNION ALL
        SELECT 'extension', '', e.extname, ''
        FROM pg_catalog.pg_extension e
        UNION ALL
        SELECT 'collation', n.nspname, c.collname, ''
        FROM pg_catalog.pg_collation c
        JOIN pg_catalog.pg_namespace n ON n.oid = c.collnamespace;

        SELECT child_namespace.nspname, child.relname,
               parent_namespace.nspname, parent.relname
        FROM pg_catalog.pg_constraint fk
        JOIN pg_catalog.pg_class child ON child.oid = fk.conrelid
        JOIN pg_catalog.pg_namespace child_namespace ON child_namespace.oid = child.relnamespace
        JOIN pg_catalog.pg_class parent ON parent.oid = fk.confrelid
        JOIN pg_catalog.pg_namespace parent_namespace ON parent_namespace.oid = parent.relnamespace
        WHERE fk.contype = 'f';
        """;

    public async Task<RestoreTargetCatalog> ReadAsync(
        ConnectionSettings connection,
        string targetDatabase,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDatabase);

        var connectionBuilder = new NpgsqlConnectionStringBuilder(connection.ToConnectionString())
        {
            Database = targetDatabase
        };

        await using var db = new NpgsqlConnection(connectionBuilder.ConnectionString);
        await db.OpenAsync(ct);
        await using var command = new NpgsqlCommand(CatalogSql, db);
        await using var reader = await command.ExecuteReaderAsync(ct);

        var catalog = new RestoreTargetCatalog();
        while (await reader.ReadAsync(ct))
        {
            var kind = reader.GetString(0);
            var schema = reader.GetString(1);
            var name = reader.GetString(2);
            var child = reader.GetString(3);

            switch (kind)
            {
                case "schema": catalog.Schemas.Add(schema); break;
                case "relation": catalog.Relations.Add(RestoreTargetCatalog.Key(schema, name)); break;
                case "constraint": catalog.Constraints.Add(RestoreTargetCatalog.Key(schema, name, child)); break;
                case "default": catalog.Defaults.Add(RestoreTargetCatalog.Key(schema, name, child)); break;
                case "trigger": catalog.Triggers.Add(RestoreTargetCatalog.Key(schema, name, child)); break;
                case "rule": catalog.Rules.Add(RestoreTargetCatalog.Key(schema, name, child)); break;
                case "routine": catalog.Routines.Add(RestoreTargetCatalog.Key(schema, name)); break;
                case "type": catalog.Types.Add(RestoreTargetCatalog.Key(schema, name)); break;
                case "extension": catalog.Extensions.Add(name); break;
                case "collation": catalog.Collations.Add(RestoreTargetCatalog.Key(schema, name)); break;
            }
        }

        if (await reader.NextResultAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                catalog.ForeignKeyDependencies.Add(new RestoreForeignKeyDependency(
                    new RestoreTableIdentity(reader.GetString(0), reader.GetString(1)),
                    new RestoreTableIdentity(reader.GetString(2), reader.GetString(3))));
            }
        }

        return catalog;
    }
}
