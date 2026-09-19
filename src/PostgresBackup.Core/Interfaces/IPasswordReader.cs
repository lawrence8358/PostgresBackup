namespace PostgresBackup.Core.Interfaces;

/// <summary>
/// 密碼輸入接縫，封裝密碼的取得來源。
/// 接縫刻意置於命令列層：由此取得密碼字串後傳入既有的儲存介面，
/// 儲存層不自行讀取任何輸入裝置。
/// 每個來源各為一個並列方法，新增來源不影響既有方法的簽章。
/// 任何來源皆不經由命令列參數傳遞密碼。
/// </summary>
public interface IPasswordReader
{
    /// <summary>
    /// 以互動式遮蔽輸入取得密碼。
    /// 使用者取消輸入或無可用的互動式主控台時回傳 <c>null</c>。
    /// </summary>
    /// <param name="prompt">提示文字。</param>
    string? ReadPassword(string prompt);

    /// <summary>
    /// 自標準輸入取得密碼，供自動化部署以管線方式提供密碼之用。
    /// 讀取的是第一行內容，尾端換行字元不計入密碼；讀取過程不回顯任何字元，
    /// 亦不輸出提示文字，以免密碼或其長度線索出現在自動化流程的紀錄中。
    /// 標準輸入已結束而無任何內容時回傳 <c>null</c>。
    /// </summary>
    string? ReadPasswordFromStandardInput();
}
