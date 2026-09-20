using PostgresBackup.Core.Models;

namespace PostgresBackup.Core.Interfaces;

/// <summary>
/// 封裝外部指令/處理序執行的接縫介面
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// 執行一次外部處理序並串流捕獲其標準輸出與標準錯誤。
    /// </summary>
    /// <param name="executable">執行檔的完整路徑。</param>
    /// <param name="arguments">
    /// **已完成跳脫的完整命令列字串**，不是參數陣列。含空白或雙引號的值必須由呼叫端
    /// 自行加上引號並跳脫 —— Core 之內只有客戶端工具作業與工具偵測服務直接面對此約定。
    /// </param>
    /// <param name="environmentVariables">
    /// 要套用至子處理序的環境變數。值為 <c>null</c> 代表**自子處理序環境中移除該變數**，
    /// 而非將其設為空字串。
    /// </param>
    /// <remarks>
    /// 取消時丟出 <see cref="OperationCanceledException"/>；其餘例外轉為離開碼 -1 的
    /// <see cref="ProcessResult"/>。
    /// </remarks>
    Task<ProcessResult> RunAsync(
        string executable,
        string arguments,
        IDictionary<string, string?>? environmentVariables = null,
        Action<string>? onOutputLine = null,
        Action<string>? onErrorLine = null,
        CancellationToken ct = default);

    /// <summary>
    /// 不需要串流輸出時的便捷多載，兩項約定（已跳脫的命令列字串、環境變數的 null 值
    /// 代表移除）與上方完全相同。
    /// </summary>
    Task<ProcessResult> RunAsync(
        string executable,
        string arguments,
        IDictionary<string, string?>? environmentVariables,
        CancellationToken ct) =>
        RunAsync(executable, arguments, environmentVariables, null, null, ct);
}
