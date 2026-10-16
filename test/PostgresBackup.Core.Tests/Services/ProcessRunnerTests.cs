using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using PostgresBackup.Core.Services;

namespace PostgresBackup.Core.Tests.Services;

public class ProcessRunnerTests
{
    private static readonly string PowerShell = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System),
        "WindowsPowerShell", "v1.0", "powershell.exe");

    private static string Arguments(string script) =>
        "-NoLogo -NoProfile -NonInteractive -EncodedCommand " +
        Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    [Fact]
    public async Task Captures_both_streams_callbacks_exit_code_and_environment_override()
    {
        var output = new ConcurrentQueue<string>();
        var errors = new ConcurrentQueue<string>();
        var result = await new ProcessRunner().RunAsync(PowerShell,
            Arguments("[Console]::OutputEncoding = [Text.Encoding]::UTF8; " +
                "[Console]::WriteLine($env:POSTGRESBACKUP_MESSAGE); [Console]::Error.WriteLine('diagnostic'); exit 7"),
            new Dictionary<string, string?> { ["POSTGRESBACKUP_MESSAGE"] = "備份測試" },
            output.Enqueue, errors.Enqueue);

        Assert.Equal(7, result.ExitCode);
        Assert.Equal("備份測試", result.StandardOutput);
        Assert.Equal("diagnostic", result.StandardError);
        Assert.Equal(["備份測試"], output.ToArray());
        Assert.Equal(["diagnostic"], errors.ToArray());
    }

    [Fact]
    public async Task Already_cancelled_operation_does_not_start_a_process()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // 不存在的執行檔必須仍以取消收場，而非嘗試啟動後回傳失敗。
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ProcessRunner().RunAsync(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe"), "", ct: cancellation.Token));
    }

    [Fact]
    public async Task Cancelling_a_running_operation_terminates_the_parent_and_child_processes()
    {
        using var cancellation = new CancellationTokenSource();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processIds = new ConcurrentQueue<int>();
        var childArguments = Arguments("Start-Sleep -Seconds 120");
        var script = $"[Console]::WriteLine('PID:' + $PID); " +
            $"$child = Start-Process -FilePath '{PowerShell}' -ArgumentList '{childArguments}' -WindowStyle Hidden -PassThru; " +
            "[Console]::WriteLine('PID:' + $child.Id); [Console]::WriteLine('READY'); Start-Sleep -Seconds 120";
        var operation = new ProcessRunner().RunAsync(PowerShell, Arguments(script),
            onOutputLine: line =>
            {
                if (line.StartsWith("PID:", StringComparison.Ordinal)) processIds.Enqueue(int.Parse(line[4..]));
                if (line == "READY") ready.TrySetResult();
            }, ct: cancellation.Token);

        try
        {
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20));
            Assert.False(operation.IsCompleted);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(20)));
            Assert.Equal(2, processIds.Count);
            foreach (var id in processIds)
            {
                try
                {
                    using var process = Process.GetProcessById(id);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                }
                catch (ArgumentException)
                {
                    // 已結束且從作業系統移除的程序同樣符合驗收條件。
                }
            }
        }
        finally
        {
            cancellation.Cancel();
            foreach (var id in processIds)
            {
                try
                {
                    using var process = Process.GetProcessById(id);
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch (ArgumentException) { }
            }
            try { await operation.WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (OperationCanceledException) { }
        }
    }

    [Fact]
    public async Task Missing_executable_returns_a_failure_instead_of_throwing()
    {
        var result = await new ProcessRunner().RunAsync(
            Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.exe"), "");

        Assert.Equal(-1, result.ExitCode);
        Assert.False(result.Success);
        Assert.NotEmpty(result.StandardError);
    }

    [Fact]
    public async Task Null_environment_override_removes_an_inherited_variable_without_changing_the_parent()
    {
        var variable = $"POSTGRESBACKUP_TEST_{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(variable, "inherited");
        try
        {
            var result = await new ProcessRunner().RunAsync(PowerShell,
                Arguments($"[Console]::WriteLine([Environment]::GetEnvironmentVariables().Contains('{variable}'))"),
                new Dictionary<string, string?> { [variable] = null });

            Assert.True(result.Success, result.StandardError);
            Assert.Equal("False", result.StandardOutput);
            Assert.Equal("inherited", Environment.GetEnvironmentVariable(variable));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }
}
