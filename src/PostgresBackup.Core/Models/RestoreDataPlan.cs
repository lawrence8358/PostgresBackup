namespace PostgresBackup.Core.Models;

public readonly record struct RestoreTableIdentity(string Schema, string Name)
{
    public override string ToString() => $"{Schema}.{Name}";
}

public sealed record RestoreDataPlan(
    string Content,
    IReadOnlyList<RestoreTableIdentity> Tables,
    int DataEntryCount,
    IReadOnlyList<string> MissingObjects,
    IReadOnlyList<string> UnsupportedDescriptions,
    IReadOnlyList<string> CyclicTables)
{
    public bool HasChanges => DataEntryCount > 0;
}
