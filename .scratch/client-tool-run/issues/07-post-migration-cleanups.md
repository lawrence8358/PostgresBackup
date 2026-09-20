# 07: 收攏之後浮現的兩項清理

**What to build:** 票 01–03 完成後才成立的兩項清理。兩項都不是活的缺陷，但都是這次收攏直接造成的殘留，記下來以免遺失。

**Blocked by:** 01, 02, 03（皆已 resolved）

**Status:** resolved

> 兩項皆已處理。第二項採乙案，隨票 06 的剩餘工作一併解決。

## 要做的事

- [x] 刪除已成死碼的 `ProcessResult.ErrorMessage`
- [x] 決定連線字串守門的落點 —— 採乙案，守門連同 `ConnectionSettings.ConnectionString` 欄位一併移除（見票 06）

## 一、`ProcessResult.ErrorMessage` 已成死碼 — 已處理

`src/PostgresBackup.Core/Models/ProcessResult.cs` 原有：

```csharp
public string? ErrorMessage => !string.IsNullOrWhiteSpace(StandardError) ? StandardError : null;
```

錯誤訊息擷取收進 `ClientToolRun.ExtractErrorMessage` 之後，此屬性在 `src/` 與 `test/` 已無任何呼叫端（同檔的 `Success` 仍由 `ToolDetectionService` 使用，不受影響）。

spec 的 Problem Statement「後果三」正是在講這個屬性誘發的死碼：它的語意與 `StandardError` 完全重複，兩個服務都圍著它重新實作了一次判斷，而且都判斷錯了。

**處置：直接刪除**，並在 `ProcessResult` 上留下一段 `<remarks>` 記載它為何不該再被加回來。判斷依據：零呼叫端、本方案為單一應用程式而非函式庫、無外部相依，且留著等於留一個會誘發同一個錯誤的陷阱。

## 二、連線字串守門住在不變式的上一層 — 已處理（採乙案）

票 06 的守門目前各寫一份於 `BackupService.BackupAsync` 與 `RestoreService.RestoreAsync` 的第 0 步：

```csharp
if (!string.IsNullOrWhiteSpace(options.Connection.ConnectionString)) { ... }
```

但它要保護的不變式 —— 「連線參數只由 Host / Port / Username / Database 組成，永不讀 `ConnectionString`」—— 住在 `ClientToolRun.BuildConnectionArguments`。鎖裝在兩扇門上，門框本身沒有鎖：日後第三個呼叫端接上 `ClientToolRun`，會靜默繞過守門。

**待決定**：

- **甲案** —— 守門下沉至 `ClientToolRun`（訊息資源鍵由 request 帶入）。代價：既有的 `ConnectionStringRejectionTests` 兩個「守門發生在工具偵測與處理序執行之前」的斷言必須改寫，因為屆時守門會發生在偵測之後。這會讓守門變晚，而還原路徑的價值有一部分正是「在碰到目標資料庫之前就擋下來」。
- **乙案** —— 併入票 06 的剩餘工作：把 `ConnectionSettings` 改為兩種互斥的連線描述方式，使該狀態在型別上無法表示，屆時兩處守門與本項一併消失。

**採乙案，且比原先設想的更徹底。** 查證後發現 `ConnectionString` 欄位只有一個寫入點與一個讀取點，且兩者相隔兩行，因此不需要引入互斥型別 —— 直接刪除該欄位即可。守門、其資源字串與其測試隨之全部移除，本項與票 06 的剩餘工作一併關閉。詳見票 06 的「剩餘工作」一節。

甲案未採用，因為它會讓守門發生在工具偵測之後（變晚），而還原路徑的價值有一部分正是「在碰到目標資料庫之前就擋下來」。乙案連守門都不需要，不必做這個取捨。

## 來源

票 01–03 完成後的 `/code-review`（標準軸與規格軸各一）。兩項皆由標準軸提出，規格軸未反對。
