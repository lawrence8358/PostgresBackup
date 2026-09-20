# 07: 收攏之後浮現的兩項清理

**What to build:** 票 01–03 完成後才成立的兩項清理。兩項都不是活的缺陷，但都是這次收攏直接造成的殘留，記下來以免遺失。

**Blocked by:** 01, 02, 03（皆已 resolved）

**Status:** needs-triage

> 第一項已處理（見下）。第二項需先決定走哪條路，故本票維持 `needs-triage`。

## 要做的事

- [x] 刪除已成死碼的 `ProcessResult.ErrorMessage`
- [ ] 決定連線字串守門的落點（下沉至 `ClientToolRun`，或併入票 06 的剩餘工作）

## 一、`ProcessResult.ErrorMessage` 已成死碼 — 已處理

`src/PostgresBackup.Core/Models/ProcessResult.cs` 原有：

```csharp
public string? ErrorMessage => !string.IsNullOrWhiteSpace(StandardError) ? StandardError : null;
```

錯誤訊息擷取收進 `ClientToolRun.ExtractErrorMessage` 之後，此屬性在 `src/` 與 `test/` 已無任何呼叫端（同檔的 `Success` 仍由 `ToolDetectionService` 使用，不受影響）。

spec 的 Problem Statement「後果三」正是在講這個屬性誘發的死碼：它的語意與 `StandardError` 完全重複，兩個服務都圍著它重新實作了一次判斷，而且都判斷錯了。

**處置：直接刪除**，並在 `ProcessResult` 上留下一段 `<remarks>` 記載它為何不該再被加回來。判斷依據：零呼叫端、本方案為單一應用程式而非函式庫、無外部相依，且留著等於留一個會誘發同一個錯誤的陷阱。

## 二、連線字串守門住在不變式的上一層 — 待決定

票 06 的守門目前各寫一份於 `BackupService.BackupAsync` 與 `RestoreService.RestoreAsync` 的第 0 步：

```csharp
if (!string.IsNullOrWhiteSpace(options.Connection.ConnectionString)) { ... }
```

但它要保護的不變式 —— 「連線參數只由 Host / Port / Username / Database 組成，永不讀 `ConnectionString`」—— 住在 `ClientToolRun.BuildConnectionArguments`。鎖裝在兩扇門上，門框本身沒有鎖：日後第三個呼叫端接上 `ClientToolRun`，會靜默繞過守門。

**待決定**：

- **甲案** —— 守門下沉至 `ClientToolRun`（訊息資源鍵由 request 帶入）。代價：既有的 `ConnectionStringRejectionTests` 兩個「守門發生在工具偵測與處理序執行之前」的斷言必須改寫，因為屆時守門會發生在偵測之後。這會讓守門變晚，而還原路徑的價值有一部分正是「在碰到目標資料庫之前就擋下來」。
- **乙案** —— 併入票 06 的剩餘工作：把 `ConnectionSettings` 改為兩種互斥的連線描述方式，使該狀態在型別上無法表示，屆時兩處守門與本項一併消失。

乙案較根本，且不必犧牲守門的時機。若採乙案，本項可併入票 06 關閉。

## 來源

票 01–03 完成後的 `/code-review`（標準軸與規格軸各一）。兩項皆由標準軸提出，規格軸未反對。
