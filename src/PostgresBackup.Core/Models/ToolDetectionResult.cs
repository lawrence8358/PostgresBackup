namespace PostgresBackup.Core.Models;

/// <summary>
/// 客戶端工具偵測管線執行結果
/// </summary>
public record ToolDetectionResult
{
    public ToolStatus Status { get; init; } = ToolStatus.NotFound;
    public string? PgDumpPath { get; init; }
    public string? PgRestorePath { get; init; }
    public string? PsqlPath { get; init; }
    public ToolVersion? Version { get; init; }
    public DetectionSource Source { get; init; } = DetectionSource.None;
    public string? ErrorMessage { get; init; }

    public bool IsReady => Status == ToolStatus.Ready;
    public bool IsIncompatible => Status == ToolStatus.Incompatible;
    public bool IsNotFound => Status == ToolStatus.NotFound;

    public static ToolDetectionResult CreateNotFound(string? error = null) =>
        new()
        {
            Status = ToolStatus.NotFound,
            Source = DetectionSource.None,
            ErrorMessage = error
        };

    public static ToolDetectionResult CreateFound(
        string pgDumpPath,
        string? pgRestorePath,
        string? psqlPath,
        ToolVersion? version,
        DetectionSource source) =>
        new()
        {
            Status = ToolStatus.Ready,
            PgDumpPath = pgDumpPath,
            PgRestorePath = pgRestorePath,
            PsqlPath = psqlPath,
            Version = version,
            Source = source
        };
}
