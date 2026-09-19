using System.Diagnostics;
using System.Text;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

using PostgresBackup.Core.Resources;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 實作外部處理序執行器
/// </summary>
public class ProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(
        string executable,
        string arguments,
        IDictionary<string, string?>? environmentVariables = null,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null,
        CancellationToken ct = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        if (environmentVariables != null)
        {
            foreach (var kvp in environmentVariables)
            {
                if (kvp.Value is not null)
                {
                    startInfo.EnvironmentVariables[kvp.Key] = kvp.Value;
                }
                else
                {
                    startInfo.EnvironmentVariables.Remove(kvp.Key);
                }
            }
        }

        using var process = new Process { StartInfo = startInfo };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (outputBuilder)
                {
                    outputBuilder.AppendLine(e.Data);
                }
                onOutputLine?.Invoke(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                lock (errorBuilder)
                {
                    errorBuilder.AppendLine(e.Data);
                }
                onErrorLine?.Invoke(e.Data);
            }
        };

        try
        {
            if (!process.Start())
            {
                return new ProcessResult(-1, string.Empty, CoreStrings.Format("Process_Error_CannotStart", executable));
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);
            // WaitForExitAsync observes process termination, but the asynchronous
            // redirected stream callbacks can still be queued. Drain them before
            // returning so the final log lines reach the UI and CLI callers.
            process.WaitForExit();

            return new ProcessResult(
                process.ExitCode,
                outputBuilder.ToString().Trim(),
                errorBuilder.ToString().Trim());
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // 忽略終止處理序時的例外
            }
            throw;
        }
        catch (Exception ex)
        {
            return new ProcessResult(-1, string.Empty, CoreStrings.Format("Process_Error_Exception", ex.Message));
        }
    }

    public Task<ProcessResult> RunAsync(
        string executable,
        string arguments,
        IDictionary<string, string?>? environmentVariables,
        CancellationToken ct) =>
        RunAsync(executable, arguments, environmentVariables, null, null, ct);
}
