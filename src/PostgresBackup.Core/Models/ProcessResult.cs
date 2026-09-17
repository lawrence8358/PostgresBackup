namespace PostgresBackup.Core.Models;

/// <summary>
/// 外部處理序執行結果
/// </summary>
public record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;
    public string? ErrorMessage => !string.IsNullOrWhiteSpace(StandardError) ? StandardError : null;
}
