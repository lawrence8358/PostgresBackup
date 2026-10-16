# 04: 長時間備份與還原作業無法中止

**What to build:** 讓使用者能中止執行中的備份或還原作業。型別上的接縫早已存在，只是沒有任何呼叫端使用它。

**Blocked by:** 無（與票 01–03 無相依，可獨立進行）

**Status:** resolved

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

## 2026-10-16 實作範圍與驗收

依使用者要求完成剩餘票券，本次採用以下行為；前面的問題描述保留為提出時的背景。

- 部分備份檔保留，取消訊息提供完整路徑並明示不得當作完整備份；不自動刪除或更名檔案。
- WPF 在頁首作業狀態區提供取消按鈕，取消後呈現獨立的「已取消」狀態，還原須重新確認才能再執行。
- CLI 使用 System.CommandLine 非同步 action 的取消 token 接收 Ctrl+C；取消回傳 130，並確保日誌關閉。終止寬限為 15 秒。
- 已進入客戶端工具作業的取消會以 `BackupStatus.Cancelled` 記錄，使用獨立且最多 5 秒的 token 寫入稽核。準備階段或唯讀探查取消不建立虛構的工具作業紀錄；快照取消只留下該次快照的取消紀錄，不開始還原。
- 還原取消訊息區分尚未修改與可能已修改目標的情形，提供已完成安全快照的完整路徑，沒有快照時明確說明。取消不承諾復原已提交修改。
- 歷史紀錄中的取消備份與取消快照不允許發起還原；取消還原的來源備份仍可使用。
- 公開測試介面為 `ProcessRunner.RunAsync`、備份／還原服務、WPF 作業指令與 CLI 指令執行；測試不啟動一般 GUI。

驗收清單：

- [x] WPF 與 CLI 取消訊號抵達實際工具作業。
- [x] 部分檔案、取消紀錄與安全快照提示有回歸測試。
- [x] 隔離 PostgreSQL 實機取消與 WPF 按鈕畫面驗證完成。
- [x] 完整非增量建置、全套測試與雙軸審查通過。

## Comments

### 2026-10-16 完成與驗證

- `dotnet build PostgresBackup.sln --no-incremental --no-restore -v:minimal`：0 警告、0 錯誤。
- `dotnet test PostgresBackup.sln --no-build --no-restore --logger trx --results-directory .scratch/test-reports/cancellation-20261016/final-tests -v:minimal`：Core 124、CLI 56、WPF 49，共 **229 通過、0 失敗、0 略過**。一般測試不啟動 GUI。
- Standards 軸：0 項硬性違反、0 項需修正的重要 smell。Spec 軸提出相對路徑未正規化的問題，補上同磁碟相對路徑回歸測試（舊實作失敗、修正後通過），複查確認可關閉，無剩餘發現。
- 測試輸出與實機驗證資料留在忽略版控的 `.scratch/test-reports/cancellation-20261016/`。

隔離 PostgreSQL 18.6 容器搭配真正 `pg_dump`／`psql` 完成驗證：WPF 取消按鈕可見且能取消、CLI 命令執行器收到取消 token 後回傳 130、兩次備份及一次還原留下 Cancelled 紀錄、安全快照保留且詳情顯示完整路徑。psql 在先提交一筆資料後進入長查詢，取消後已提交資料保留、後續 SQL 未執行，符合不自動復原的語意。實機驗證使用命令執行器的取消 token，未以鍵盤輸入實測 Ctrl+C 事件；事件接線使用 System.CommandLine 既有終止訊號處理。

四張操作畫面：`backup-running.png`、`backup-cancelled.png`、`restore-running.png`、`restore-cancelled.png`。測試靶為專用且只綁定 loopback 的容器，未使用本機真實連線設定；測試程序、容器及其匿名資料卷均已清理。
