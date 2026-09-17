namespace PostgresBackup.Core.Models;

/// <summary>
/// 還原作業執行結果
/// </summary>
public record RestoreResult
{
    public bool IsSuccess { get; init; }
    public string? SnapshotFilePath { get; init; }
    public bool SnapshotCreated { get; init; }
    public TimeSpan Duration { get; init; }
    public int ExitCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string Arguments { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public static RestoreResult Success(
        TimeSpan duration,
        string args,
        string? snapshotPath = null) =>
        new()
        {
            IsSuccess = true,
            Duration = duration,
            ExitCode = 0,
            Arguments = args,
            SnapshotFilePath = snapshotPath,
            SnapshotCreated = !string.IsNullOrEmpty(snapshotPath)
        };

    public static RestoreResult Failure(
        string? errorMessage,
        int exitCode,
        TimeSpan duration,
        string args,
        string? snapshotPath = null) =>
        new()
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ExitCode = exitCode,
            Duration = duration,
            Arguments = args,
            SnapshotFilePath = snapshotPath,
            SnapshotCreated = !string.IsNullOrEmpty(snapshotPath)
        };
}
