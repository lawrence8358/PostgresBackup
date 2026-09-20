# 05: `ProcessRunner` 沒有任何測試

**What to build:** 為 `ProcessRunner` 建立測試防線，特別是「環境變數傳 `null` 代表移除該變數」這項被兩個服務依賴卻無人保護的約定。

**Blocked by:** 無

**Status:** needs-triage

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
