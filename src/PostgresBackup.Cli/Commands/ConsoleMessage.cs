namespace PostgresBackup.Cli.Commands;

/// <summary>
/// 命令列訊息的單一輸出點。
/// 前綴與顏色在此定案，讓所有指令印出的警告與錯誤看起來是同一個工具說的話。
/// </summary>
internal static class ConsoleMessage
{
    public static void WriteWarning(string message) => Write(ConsoleColor.Yellow, "WARNING", message);

    public static void WriteError(string message) => Write(ConsoleColor.Red, "ERROR", message);

    private static void Write(ConsoleColor color, string level, string message)
    {
        Console.ForegroundColor = color;
        Console.WriteLine($"[{level}] {message}");
        Console.ResetColor();
    }
}
