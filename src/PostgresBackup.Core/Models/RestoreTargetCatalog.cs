namespace PostgresBackup.Core.Models;

/// <summary>
/// Names of objects that already exist in the restore target.
/// PostgreSQL quoted identifiers are case-sensitive, so all sets use ordinal comparison.
/// </summary>
public sealed class RestoreTargetCatalog
{
    public HashSet<string> Schemas { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Relations { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Constraints { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Defaults { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Triggers { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Rules { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Routines { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Types { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Extensions { get; } = new(StringComparer.Ordinal);
    public HashSet<string> Collations { get; } = new(StringComparer.Ordinal);
    public HashSet<RestoreForeignKeyDependency> ForeignKeyDependencies { get; } = [];

    public static string Key(params string[] parts) => string.Join('\0', parts);
}

public readonly record struct RestoreForeignKeyDependency(
    RestoreTableIdentity Child,
    RestoreTableIdentity Parent);
