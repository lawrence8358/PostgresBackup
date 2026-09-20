# 04: 長時間備份與還原作業無法中止

**What to build:** 讓使用者能中止執行中的備份或還原作業。型別上的接縫早已存在，只是沒有任何呼叫端使用它。

**Blocked by:** 無（與票 01–03 無相依，可獨立進行）

**Status:** needs-triage

## 問題

`IBackupService.BackupAsync` 與 `IRestoreService.RestoreAsync` 都接受 `CancellationToken`，`ProcessRunner` 也已實作取消（會丟出 `OperationCanceledException`，並以 `Kill(entireProcessTree)` 收掉子處理序）。

但**四個呼叫端全部省略不傳**：

- `src/PostgresBackup.Wpf/ViewModels/BackupViewModel.cs` — `StartBackupAsync` 中的服務呼叫
- `src/PostgresBackup.Wpf/ViewModels/RestoreViewModel.cs` — `StartRestoreAsync` 中的服務呼叫
- `src/PostgresBackup.Cli/Commands/BackupCommand.cs`
- `src/PostgresBackup.Cli/Commands/RestoreCommand.cs`

全專案搜尋 `CancellationToken`，在 WPF 與 CLI 兩個專案中只出現於 `IClientToolPreferencesStore` 相關類別與 `Program.cs` 的 `CancellationToken.None`。

實際後果：一個跑了二十分鐘、明顯跑錯目標的還原作業，使用者沒有任何方法中止，只能強制關閉應用程式 —— 而強制關閉時子處理序的下場不確定。

## 為何不併入票 01–03

那批是重構，對外可觀察行為刻意維持不變；此票是新功能。混在同一批變更中，審查時無法分辨行為變化是重構造成的還是新功能造成的。

## 需要決定的事（尚未討論）

- 取消後已經寫出的部分備份檔如何處理？刪除、保留、或保留但標記？
- 還原中途取消，目標資料庫會處於半完成狀態 —— 是否需要提示使用者安全快照的位置？（`--exit-on-error` 已在，但取消不是錯誤）
- 取消是否要寫入一筆備份紀錄？若要，`BackupStatus` 需不需要新增列舉值？
- WPF 的取消按鈕放在哪、CLI 是否以 Ctrl+C 對應？

## 備註

若票 01–03 已完成，客戶端工具作業會是取消路徑唯一需要處理的位置，實作範圍比現在小。建議排在其後。
