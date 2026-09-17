using System.Diagnostics;
using System.Text;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 實作官方客戶端工具執行器，支援安全密碼環境變數傳遞與即時輸出串流捕獲
/// </summary>
public class ClientToolRunner : IClientToolRunner
{
    public async Task<ProcessResult> RunToolAsync(
        string executablePath,
        string arguments,
        string? password = null,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            throw new ArgumentException("Executable path cannot be empty.", nameof(executablePath));

        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        // 透過環境變數傳遞 PGPASSWORD，避免出現在命令列或程序監視工具中
        if (!string.IsNullOrEmpty(password))
        {
            startInfo.EnvironmentVariables["PGPASSWORD"] = password;
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
                return new ProcessResult(-1, string.Empty, $"無法啟動處理序: {executablePath}");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);

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
            return new ProcessResult(-1, string.Empty, $"執行工具時發生未預期例外: {ex.Message}");
        }
    }
}
