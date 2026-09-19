using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Interfaces;

public interface IRestoreDataPreparationService
{
    Task ClearTablesAsync(
        ConnectionSettings connection,
        string targetDatabase,
        IReadOnlyList<RestoreTableIdentity> tables,
        CancellationToken ct = default);
}
