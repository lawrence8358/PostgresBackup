using System.Diagnostics;
using System.Globalization;
using System.Text;
using PostgresBackup.Core.Interfaces;
using PostgresBackup.Core.Models;
using PostgresBackup.Core.Resources;

namespace PostgresBackup.Core.Services;

/// <summary>
/// 客戶端工具作業（Client Tool Run）：執行一次客戶端工具的完整過程，涵蓋連線參數組裝、
/// 環境變數設定、argv 跳脫與拼接、處理序執行、計時，以及該次執行所留下的備份紀錄。
/// 備份作業與還原作業皆透過它抵達 <c>pg_dump</c>、<c>pg_restore</c> 與 <c>psql</c>。
///
/// 呼叫端交出的是資料（未跳脫的 argv 元素清單與連線設定），不是命令列字串 ——
/// <see cref="IProcessRunner"/> 那條「呼叫端自行跳脫」的約定到此為止，不再往上傳播。
///
/// 本模組刻意不設介面：正式環境只有一種實作，測試所需的抽換點是它底下既有的
/// <see cref="IProcessRunner"/>。
/// </summary>
public sealed class ClientToolRun
{
    private readonly IProcessRunner _processRunner;
    private readonly IBackupHistoryRepository? _historyRepo;

    public ClientToolRun(IProcessRunner processRunner, IBackupHistoryRepository? historyRepo = null)
    {
        _processRunner = processRunner;
        _historyRepo = historyRepo;
    }

    /// <summary>
    /// 執行一次客戶端工具作業：於作業專屬參數前補上連線參數、套用環境變數政策、計時、
    /// 判定成敗並擷取錯誤訊息，最後嘗試寫入一筆備份紀錄。
    ///
    /// 紀錄寫入失敗只輸出一行 <c>[WARNING]</c>，不改變作業結果 —— 已經落地的備份檔
    /// 不該因為稽核資料庫不可用而被判為失敗，但稽核缺口也不該無聲發生。
    ///
    /// 取消（<see cref="OperationCanceledException"/>）向上傳播，因為取消不是失敗；
    /// 其餘任何例外都收斂為離開碼 -1 的失敗結果，且一樣會嘗試寫入紀錄。
    /// </summary>
    public async Task<ClientToolRunResult> RunAsync(
        ClientToolRunRequest request,
        Action<string>? onLogLine = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var argv = new List<string>(BuildConnectionArguments(request.Connection, request.TargetDatabase));
        argv.AddRange(request.Arguments);
        var commandLine = JoinArguments(argv);

        var stopwatch = Stopwatch.StartNew();
        var processResult = await ExecuteAsync(
            request.ExecutablePath,
            commandLine,
            request.Connection.Password,
            onOutputLine: line => onLogLine?.Invoke($"[{request.LogPrefix}] {line}"),
            ct);
        stopwatch.Stop();

        // 離開碼為零只代表工具沒有抱怨。備份還要求產出檔案確實存在，該判定屬於呼叫端。
        var succeeded = processResult.ExitCode == 0 && (request.ConfirmSuccess?.Invoke() ?? true);
        var fileSize = request.MeasureRecordedFileSize?.Invoke(succeeded) ?? 0;

        var result = new ClientToolRunResult
        {
            IsSuccess = succeeded,
            ExitCode = processResult.ExitCode,
            Elapsed = stopwatch.Elapsed,
            CommandLine = commandLine,
            StandardOutput = processResult.StandardOutput,
            ErrorMessage = succeeded
                ? null
                : ExtractErrorMessage(processResult, request.NonZeroExitErrorKey),
            RecordedFileSizeBytes = fileSize
        };

        await WriteRecordAsync(request, result, onLogLine, ct);
        return result;
    }

    /// <summary>
    /// 執行一次唯讀探查（例如 <c>pg_restore --list</c>）：沿用同一套環境變數政策與
    /// argv 拼接規則，但不補上連線參數、不串流輸出、也不留下備份紀錄。
    /// </summary>
    public async Task<ClientToolRunResult> ProbeAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        ConnectionSettings connection,
        string nonZeroExitErrorKey,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(connection);

        var commandLine = JoinArguments(arguments);

        var stopwatch = Stopwatch.StartNew();
        var processResult = await ExecuteAsync(
            executablePath, commandLine, connection.Password, onOutputLine: null, ct);
        stopwatch.Stop();

        var succeeded = processResult.ExitCode == 0;
        return new ClientToolRunResult
        {
            IsSuccess = succeeded,
            ExitCode = processResult.ExitCode,
            Elapsed = stopwatch.Elapsed,
            CommandLine = commandLine,
            StandardOutput = processResult.StandardOutput,
            ErrorMessage = succeeded ? null : ExtractErrorMessage(processResult, nonZeroExitErrorKey)
        };
    }

    private async Task<ProcessResult> ExecuteAsync(
        string executablePath,
        string commandLine,
        string? password,
        Action<string>? onOutputLine,
        CancellationToken ct)
    {
        try
        {
            return await _processRunner.RunAsync(
                executablePath,
                commandLine,
                BuildEnvironmentVariables(password),
                onOutputLine: onOutputLine,
                onErrorLine: onOutputLine,
                ct: ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 同一類失敗不該有兩種結局：處理序層丟出的例外在此收斂為與非零離開碼
            // 相同的失敗結果，好讓紀錄必定被嘗試寫入。
            return new ProcessResult(-1, string.Empty, ex.Message);
        }
    }

    /// <summary>
    /// 客戶端工具共用的環境變數政策。<c>LANGUAGE</c> 以 null 傳遞，
    /// 依 <see cref="IProcessRunner"/> 的約定代表自子處理序環境中移除該變數。
    /// </summary>
    private static Dictionary<string, string?> BuildEnvironmentVariables(string? password)
    {
        var envVars = new Dictionary<string, string?>();
        if (!string.IsNullOrEmpty(password))
        {
            envVars["PGPASSWORD"] = password;
        }

        // 備份資料的編碼必須明確；診斷訊息則一律壓回 C 語系，避免在地化的 Windows
        // 訊息以未知的字碼頁輸出而無法解碼。
        envVars["PGCLIENTENCODING"] = "UTF8";
        envVars["LC_ALL"] = "C";
        envVars["LC_MESSAGES"] = "C";
        envVars["LANG"] = "C";
        envVars["LANGUAGE"] = null;
        return envVars;
    }

    private static IReadOnlyList<string> BuildConnectionArguments(
        ConnectionSettings connection,
        string? targetDatabase)
    {
        var host = string.IsNullOrWhiteSpace(connection.Host) ? "localhost" : connection.Host;
        var port = connection.Port > 0 ? connection.Port : 5432;
        var username = string.IsNullOrWhiteSpace(connection.Username) ? "postgres" : connection.Username;
        var database = !string.IsNullOrWhiteSpace(targetDatabase)
            ? targetDatabase
            : (string.IsNullOrWhiteSpace(connection.Database) ? "postgres" : connection.Database);

        return
        [
            "-h", host,
            "-p", port.ToString(CultureInfo.InvariantCulture),
            "-U", username,
            "-d", database
        ];
    }

    /// <summary>
    /// 將 argv 元素清單拼接為 <c>ProcessStartInfo</c> 所需的單一命令列字串。
    /// 元素為空字串、或含空白字元或雙引號時加上雙引號並跳脫內部雙引號，否則原樣輸出 ——
    /// 需不需要引號由值本身決定，不由呼叫端的習慣決定。
    /// </summary>
    private static string JoinArguments(IEnumerable<string> arguments)
    {
        var sb = new StringBuilder();
        foreach (var argument in arguments)
        {
            if (sb.Length > 0) sb.Append(' ');

            var value = argument ?? string.Empty;
            var needsQuotes = value.Length == 0
                || value.Any(char.IsWhiteSpace)
                || value.Contains('"');

            if (needsQuotes)
            {
                sb.Append('"').Append(value.Replace("\"", "\\\"")).Append('"');
            }
            else
            {
                sb.Append(value);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 標準錯誤有值即採用，否則採用呼叫端傳入的資源鍵。
    /// 不經由 <see cref="ProcessResult.ErrorMessage"/> —— 該屬性的定義與此處的第一個
    /// 條件完全重複，永遠無法貢獻任何值。
    /// </summary>
    private static string ExtractErrorMessage(ProcessResult processResult, string nonZeroExitErrorKey) =>
        !string.IsNullOrWhiteSpace(processResult.StandardError)
            ? processResult.StandardError
            : CoreStrings.Get(nonZeroExitErrorKey);

    private async Task WriteRecordAsync(
        ClientToolRunRequest request,
        ClientToolRunResult result,
        Action<string>? onLogLine,
        CancellationToken ct)
    {
        if (_historyRepo is null) return;

        try
        {
            await _historyRepo.AddRecordAsync(new BackupRecord
            {
                Timestamp = DateTimeOffset.UtcNow,
                OperationType = request.OperationType,
                DatabaseName = string.IsNullOrWhiteSpace(request.TargetDatabase)
                    ? request.Connection.Database
                    : request.TargetDatabase,
                TargetDatabase = string.IsNullOrWhiteSpace(request.TargetDatabase)
                    ? null
                    : request.TargetDatabase,
                FilePath = request.RecordedFilePath,
                Format = request.RecordedFormat,
                FileSizeBytes = result.RecordedFileSizeBytes,
                DurationMs = (long)result.Elapsed.TotalMilliseconds,
                Status = result.IsSuccess ? BackupStatus.Success : BackupStatus.Failed,
                ErrorMessage = result.ErrorMessage,
                Arguments = result.CommandLine
            }, ct);
        }
        catch (Exception ex)
        {
            onLogLine?.Invoke(
                $"[{DateTime.Now:HH:mm:ss}] [WARNING] {CoreStrings.Format("Backup_Log_HistoryWriteFailed", ex.Message)}");
        }
    }
}
