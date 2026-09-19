using PostgresBackup.Core.Interfaces;

namespace PostgresBackup.Cli.Services;

/// <summary>
/// 以主控台互動式遮蔽輸入取得密碼。
/// 輸入過程中不回顯任何字元，密碼亦不經由命令列參數傳遞，因此不會出現在行程資訊或殼層歷史紀錄中。
/// </summary>
public class ConsolePasswordReader : IPasswordReader
{
    public string? ReadPassword(string prompt)
    {
        if (Console.IsInputRedirected)
        {
            // 無互動式主控台時不退回讀取標準輸入：那是另一項明確的使用者選擇，
            // 應由專屬參數表達，而非在此靜默改變密碼來源。
            return null;
        }

        Console.Write(prompt);

        var password = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                return password.ToString();
            }

            if (key.Key == ConsoleKey.Escape)
            {
                Console.WriteLine();
                return null;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (password.Length > 0)
                {
                    password.Length--;
                }
                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                password.Append(key.KeyChar);
            }
        }
    }

    public string? ReadPasswordFromStandardInput()
    {
        // 不輸出任何提示：此路徑供管線與自動化部署使用，提示文字只會混入其紀錄。
        // 讀取單一行；尾端的 CR/LF 由 ReadLine 移除，殘留的 CR（Unix 管線於 Windows 上）另行去除。
        var line = Console.ReadLine();
        return line?.TrimEnd('\r', '\n');
    }
}
