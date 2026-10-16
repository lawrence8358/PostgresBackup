# 05: `ProcessRunner` 沒有任何測試

**What to build:** 為 `ProcessRunner` 建立測試防線，特別是「環境變數傳 `null` 代表移除該變數」這項被兩個服務依賴卻無人保護的約定。

**Blocked by:** 無

**Status:** resolved

## 問題

`src/PostgresBackup.Core/Services/ProcessRunner.cs`（127 行）在 `test/PostgresBackup.Core.Tests` 中**沒有任何測試檔引用**。

它是 Core 中唯一實際做這些事的地方：

- 啟動 `Process`、重新導向並讀取標準輸出與標準錯誤
- 取消時 `Kill(entireProcessTree: true)`
- **環境變數字典中 `null` 值代表自子處理序環境移除該變數**
- 取消丟出 `OperationCanceledException`，其餘例外轉為 `ProcessResult(-1, ...)`

第三項最值得補：`BackupService` 與 `RestoreService` 都靠它把 `LANGUAGE` 移除（目的是避免本地化的 Windows 訊息以未知字碼頁輸出，導致重新導向的輸出無法解碼）。這條約定寫在 `ProcessRunner` 的實作裡，介面上沒有，也沒有任何測試釘住它。有人把 `null` 改成寫入空字串，備份看起來照常運作，只有在特定語系的機器上才會出現亂碼。

## 為何不併入票 01–03

補這個測試需要實際執行外部程序 —— 得找一個在持續整合環境中穩定存在的靶（`cmd /c`、`where`、或專案自帶的小程式），性質上屬整合測試，與那批純重構不同。

## 需要決定的事（尚未討論）

- 用什麼當測試靶？必須在持續整合環境中穩定可用，且能觀察到環境變數的實際值（例如 `cmd /c echo %LANGUAGE%`）
- 取消路徑要怎麼測？需要一個會持續執行到被砍掉的靶
- `Kill(entireProcessTree)` 要不要測？測了會比較慢，也比較脆

## 相關

票 01 會在 `IProcessRunner` 上補文件註解說明這兩項約定。文件不等於防線 —— 此票才是防線。

## 2026-10-16 實作範圍與驗收

使用本專案支援之 Windows 環境內建的 Windows PowerShell 作為實際程序測試靶，以 `-NoProfile -NonInteractive -EncodedCommand` 啟動，不依賴使用者 shell 設定、資料庫或網路。環境變數使用每次唯一名稱並於測試後清除。

取消測試由程序輸出的 READY 訊號觸發，不以固定睡眠猜測啟動時機；驗證父程序與其子程序均結束，並於 finally 清理測試程序。程序使用有時限的等待，避免回歸造成測試永久阻塞。

- [x] 驗證 null 移除繼承的環境變數，且不修改父程序環境。
- [x] 驗證 UTF-8 stdout、stderr、串流回呼與非零離開碼。
- [x] 驗證已取消的作業不啟動程序、執行中取消終止程序樹。
- [x] 驗證啟動失敗回傳 -1 而非逸出例外。
- [x] 完整非增量建置、全套測試與雙軸審查通過。

## Comments

### 2026-10-16 完成與驗證

- `dotnet build PostgresBackup.sln --no-incremental --no-restore -v:minimal`：0 警告、0 錯誤。
- `dotnet test PostgresBackup.sln --no-build --no-restore --logger trx --results-directory .scratch/test-reports/cancellation-20261016/final-tests -v:minimal`：Core 124、CLI 56、WPF 49，共 **229 通過、0 失敗、0 略過**。一般測試不啟動 GUI。
- Standards 軸：0 項硬性違反、0 項需修正的重要 smell。Spec 軸提出相對路徑未正規化的問題，補上同磁碟相對路徑回歸測試（舊實作失敗、修正後通過），複查確認可關閉，無剩餘發現。
- 測試輸出與實機驗證資料留在忽略版控的 `.scratch/test-reports/cancellation-20261016/`。

五項 `ProcessRunnerTests` 均實際執行通過，包括父／子程序終止。新增測試先找出預先取消仍嘗試啟動程序的缺陷，再加上啟動前 token 檢查；取消後亦等待程序結束及串流回呼排空，避免日誌關閉後仍有回呼寫入。
