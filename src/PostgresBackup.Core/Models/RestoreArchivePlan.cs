namespace PostgresBackup.Core.Models;

public sealed record RestoreArchivePlan(
    string Content,
    int IncludedCount,
    int ActionableCount,
    int SkippedExistingCount,
    int SkippedMetadataCount,
    IReadOnlyList<string> UnsupportedDescriptions);
