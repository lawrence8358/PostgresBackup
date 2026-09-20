namespace PostgresBackup.Core.Models;

/// <summary>
/// 外部處理序執行結果
/// </summary>
/// <remarks>
/// 此處刻意不提供「錯誤訊息」屬性。曾有一個 <c>ErrorMessage</c>，其定義是
/// 「<see cref="StandardError"/> 有值就回傳它，否則回傳 null」—— 與 <see cref="StandardError"/>
/// 講的是同一件事。兩個服務都因此在它之外又寫了一次同樣的判斷，寫成
/// <c>StandardError 有值 ? StandardError : ErrorMessage ?? 後備字串</c>，
/// 中間那個運算元永遠不可能貢獻任何值 —— 兩邊都是死碼，而且錯得一樣。
///
/// 錯誤訊息的擷取現在只有一份，位於 <c>ClientToolRun.ExtractErrorMessage</c>：
/// 標準錯誤有值即採用，否則採用呼叫端傳入的資源鍵。
/// </remarks>
public record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;
}
