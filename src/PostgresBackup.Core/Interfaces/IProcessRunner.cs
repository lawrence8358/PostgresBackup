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
        CancellationToken ct = default);
}
