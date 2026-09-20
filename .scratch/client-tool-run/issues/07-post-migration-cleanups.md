# 07: 收攏之後浮現的兩項清理

**What to build:** 票 01–03 完成後才成立的兩項清理。兩項都不是活的缺陷，但都是這次收攏直接造成的殘留，記下來以免遺失。

**Blocked by:** 01, 02, 03（皆已 resolved）

**Status:** needs-triage

## 一、`ProcessResult.ErrorMessage` 已成死碼

`src/PostgresBackup.Core/Models/ProcessResult.cs`：

```csharp
public string? ErrorMessage => !string.IsNullOrWhiteSpace(StandardError) ? StandardError : null;
```

錯誤訊息擷取收進 `ClientToolRun.ExtractErrorMessage` 之後，此屬性在 `src/` 與 `test/` 已無任何呼叫端（同檔的 `Success` 仍由 `ToolDetectionService` 使用，不受影響）。

spec 的 Problem Statement「後果三」正是在講這個屬性誘發的死碼：它的語意與 `StandardError` 完全重複，兩個服務都圍著它重新實作了一次判斷，而且都判斷錯了。留著它，下一個呼叫端有機會再踩一次。

**待決定**：直接刪除，或保留並加註「勿用於錯誤訊息擷取」。`ProcessResult` 是公開型別，但本方案為單一應用程式而非函式庫，無外部相依。

## 二、連線字串守門住在不變式的上一層

票 06 的守門目前各寫一份於 `BackupService.BackupAsync` 與 `RestoreService.RestoreAsync` 的第 0 步：

```csharp
if (!string.IsNullOrWhiteSpace(options.Connection.ConnectionString)) { ... }
```

但它要保護的不變式 —— 「連線參數只由 Host / Port / Username / Database 組成，永不讀 `ConnectionString`」—— 住在 `ClientToolRun.BuildConnectionArguments`。鎖裝在兩扇門上，門框本身沒有鎖：日後第三個呼叫端接上 `ClientToolRun`，會靜默繞過守門。

**待決定**：守門下沉至 `ClientToolRun`（訊息資源鍵由 request 帶入，既有的 `ConnectionStringRejectionTests` 兩個「守門發生在工具偵測之前」的斷言須改寫，因為屆時守門會發生在偵測之後）；或直接併入票 06 的剩餘工作 —— 把 `ConnectionSettings` 改為兩種互斥的連線描述方式，使該狀態在型別上無法表示，屆時守門本身即可刪除。

後者較根本。若採後者，本項可併入票 06 關閉。

## 來源

票 01–03 完成後的 `/code-review`（標準軸與規格軸各一）。兩項皆由標準軸提出，規格軸未反對。
