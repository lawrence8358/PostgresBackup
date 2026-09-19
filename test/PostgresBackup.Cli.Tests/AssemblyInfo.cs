using Xunit;

// 命令列指令測試以 Console.SetOut 擷取輸出，而該設定為行程層級的共用狀態。
// 若測試平行執行，其他測試還原 Console.Out 時會奪走擷取中的輸出，造成偶發失敗。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
