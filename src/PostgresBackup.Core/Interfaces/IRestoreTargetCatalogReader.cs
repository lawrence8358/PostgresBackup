using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Interfaces;

public interface IRestoreTargetCatalogReader
{
    Task<RestoreTargetCatalog> ReadAsync(
        ConnectionSettings connection,
        string targetDatabase,
        CancellationToken ct = default);
}
