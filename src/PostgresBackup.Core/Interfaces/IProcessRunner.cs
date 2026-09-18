using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Interfaces;

/// <summary>
/// 封裝外部指令/處理序執行的接縫介面
/// </summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string executable,
        string arguments,
        IDictionary<string, string?>? environmentVariables = null,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null,
        CancellationToken ct = default);

    Task<ProcessResult> RunAsync(
        string executable,
        string arguments,
        IDictionary<string, string?>? environmentVariables,
        CancellationToken ct) =>
        RunAsync(executable, arguments, environmentVariables, null, null, ct);
}
