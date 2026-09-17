namespace PostgresBackup.Core.Models;

/// <summary>
/// 備份作業執行結果
/// </summary>
public record BackupResult
{
    public bool IsSuccess { get; init; }
    public string OutputFilePath { get; init; } = string.Empty;
    public long FileSizeBytes { get; init; }
    public TimeSpan Duration { get; init; }
    public int ExitCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string Arguments { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public BackupFormat Format { get; init; } = BackupFormat.Custom;

    public static BackupResult Success(
        string filePath,
        long size,
        TimeSpan duration,
        string args,
        BackupFormat format) =>
        new()
        {
            IsSuccess = true,
            OutputFilePath = filePath,
            FileSizeBytes = size,
            Duration = duration,
            ExitCode = 0,
            Arguments = args,
            Format = format
        };

    public static BackupResult Failure(
        string? errorMessage,
        int exitCode,
        TimeSpan duration,
        string args,
        string filePath = "",
        BackupFormat format = BackupFormat.Custom) =>
        new()
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ExitCode = exitCode,
            Duration = duration,
            Arguments = args,
            OutputFilePath = filePath,
            Format = format
        };
}
