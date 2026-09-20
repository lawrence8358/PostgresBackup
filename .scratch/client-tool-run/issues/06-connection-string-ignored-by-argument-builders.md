# 06: 連線字串與命令列參數是兩套不相通的世界

**What to build:** 讓備份與還原作業在收到以完整連線字串描述的連線時明確拒絕，而不是靜默忽略它。

**Blocked by:** 無

**Status:** resolved

**驗證狀態：** 已於 2026-09-20 完成建置與測試驗證。建置 0 警告 0 錯誤；192 個測試全綠（Core 96、CLI 52、WPF 44），其中新增 3 個測試實際執行並通過。

## 問題

`ConnectionSettings` 同時容納兩種描述連線的方式，而程式中有兩派各讀各的：

- **`ToConnectionString()`** —— 只要 `ConnectionString` 有值就整串回傳，其餘欄位全部忽略。
- **兩個參數構建器** —— 只讀 `Host`、`Port`、`Username`、`Database`，完全不看 `ConnectionString`。

**分歧發生在還原流程內部**，這是本票最初描述所低估的地方：

| 還原流程的步驟 | 實作位置 | 認不認 `ConnectionString` |
| --- | --- | --- |
| 讀取目標資料庫現況 | `NpgsqlRestoreTargetCatalogReader:75` → `ToConnectionString()` | 認 |
| 清空資料表（DataOnly） | `NpgsqlRestoreDataPreparationService:26` → `ToConnectionString()` | 認 |
| 執行 `pg_restore` | `RestoreArgumentsBuilder` → 讀分開欄位 | 不認 |

兩者若指向不同伺服器，一次還原會**檢查並清空 A 伺服器，然後把資料寫進 B 伺服器**。清空是破壞性的，而且清錯機器。

## 當時不是活的缺陷

全專案搜尋確認，`ConnectionString` 只有一處會被設定：

```csharp
// src/PostgresBackup.Cli/Commands/CheckToolsCommand.cs:155
ConnectionString = connStr
```

且該 `ConnectionSettings` 只餵給 `CheckCompatibilityAsync`，不流向備份或還原。備份與還原沒有任何接受連線字串的入口：CLI 沒有對應選項，`ConnectionProfile` 也**沒有連線字串欄位**（`ToConnectionSettings()` 從不設定它），因此連線字串永遠不可能從儲存的連線設定流入。

所以這是一個等人打開的陷阱，不是當時正在漏的洞。處理它的目的是讓打開入口的那個人，在跑測試時就撞到牆。

## 已排除的方向：讓備份與還原支援連線字串

**不採用，且不是工作量問題，是方向錯誤。**

`--connection-string` / `-s` 目前只存在於 `check-tools`，而使用手冊第 211 行明載：

> 完整連線字串，用於伺服器版本相容性檢查。**僅供互動式除錯使用，不得用於排程腳本**（同樣可能讓密碼暴露於處理序命令列或記錄中）

手冊第 602 行更把 `-s` 與 `-p` 並列為會讓密碼出現在處理序命令列上的兩個參數。而 `docs/adr/0001-cli-machine-scoped-credential-store.md` 的整個目的，是把排程備份的密碼從命令列趕進機器範圍加密存放區。在 `backup` 指令上增加連線字串選項，等於在最需要跑排程的指令上開一個密碼外洩的口子，與該 ADR 直接相衝。

## 已完成的修改

- [x] 新增資源字串 `Backup_Error_ConnectionStringNotSupported` 與 `Restore_Error_ConnectionStringNotSupported`（英文與 zh-TW 各一份）。兩則訊息分別說明「為何無法採用」與「該怎麼改」；還原那則明確點出「可能清空一台、寫入另一台」
- [x] `BackupService.BackupAsync` 於方法最前端（工具偵測之前）加入守門，回傳 `BackupResult.Failure(errMsg, -1, TimeSpan.Zero, string.Empty)`，與既有「找不到 pg_dump」的失敗形狀一致
- [x] `RestoreService.RestoreAsync` 同樣於最前端加入守門，位置在來源檔案檢查**之前** —— 連線設定本身有問題時，不需要先去確認檔案在不在
- [x] 兩處守門皆附註解說明分歧的來源，不只寫「不支援」
- [x] 新增 `test/PostgresBackup.Core.Tests/Services/ConnectionStringRejectionTests.cs`，3 個測試
- [x] 建置 0 警告 0 錯誤 —— 過程中一度因使用 `options.Connection?.ConnectionString` 而讓編譯器將 `Connection` 視為可能為 null，引入 4 個 CS8602 警告。`BackupOptions.Connection` 與 `RestoreOptions.Connection` 皆為非可空且有預設值，已改回 `options.Connection.ConnectionString`
- [x] `dotnet test` 全綠（192 個測試）

### 測試設計

三個測試皆使用 `MockBehavior.Strict` 的替身，因此**守門若被移除，工具偵測一被呼叫就會使測試失敗** —— 斷言不是空的。

- `BackupAsync_WhenConnectionCarriesConnectionString_FailsWithoutDetectingTools` —— 同時驗證失敗結果與「工具偵測從未發生」
- `RestoreAsync_WhenConnectionCarriesConnectionString_FailsWithoutTouchingTargetDatabase` —— 最重要的一項，驗證 `IRestoreDataPreparationService`（清空資料表）與 `IRestoreTargetCatalogReader` 都從未被呼叫
- `BackupAsync_WhenConnectionStringIsBlank_IsNotRejected` —— 邊界：空白字串不算「帶有連線字串」，必須繼續往下走到工具偵測

### 未變更的部分

- `check-tools` 的 `-s` 選項維持不變，它的用途（版本相容性檢查）本來就走 `ToConnectionString()`，不受影響
- `ProfileCommand` 與 `SettingsViewModel` 的測試連線維持不變，兩者的連線設定皆來自 `ConnectionProfile`，永遠不含連線字串
- 使用手冊與 README 未修改：備份與還原本來就沒有連線字串選項，使用者操作介面上沒有任何可見變化

## 剩餘工作 —— 已完成（2026-09-20，票 01–03 之後）

- [x] **型別重整：讓「同時填兩種寫法」無法表示。**

### 實際做法與原規劃不同

原規劃是把 `ConnectionSettings` 改成兩種互斥的連線描述方式（分開欄位／完整連線字串）。實際查證後採用了更簡單的做法：**直接刪除 `ConnectionSettings.ConnectionString` 欄位。**

理由是該欄位的生命週期只有兩行：

```csharp
ConnectionString = connStr                                                    // CheckToolsCommand.cs:155
...
await detector.CheckCompatibilityAsync(detectionResult, connSettings.ToConnectionString());   // 兩行之後
```

寫它的只有一處，讀它的只有兩行之後的同一處，而它要餵的 `CheckCompatibilityAsync` 本來就只收一個 `string`。整個欄位的作用僅是讓一個字串繞進物件再繞出來，代價是其餘六個讀 `ConnectionSettings` 的地方從此都得假設它可能藏著指向另一台伺服器的連線字串。

引入互斥型別會保留這個繞路，只是讓它變得型別安全；刪除欄位則讓繞路本身消失。適用「刪除測試」：欄位拿掉之後複雜度是消失，不是搬到別處。

### 連鎖移除

- [x] `ConnectionSettings.ConnectionString` 欄位
- [x] `ToConnectionString()` 中的連線字串分支（現為無條件依分開欄位產生）
- [x] `BackupService` 與 `RestoreService` 的兩處執行期守門
- [x] `Backup_Error_ConnectionStringNotSupported` 與 `Restore_Error_ConnectionStringNotSupported` 兩則資源字串（中英各一份）
- [x] `ConnectionStringRejectionTests.cs`（3 個測試）—— 它們防的狀態已無法表示

守門與其測試都是撐到型別修好為止的臨時措施，任務結束即撤除。`ConnectionSettings` 上留有 `<remarks>` 記載這段歷史與「若日後真需要接受連線字串，正確做法是讓呼叫端直接持有那個字串」。

### 新增的測試

`-s/--connection-string` 改為直接抵達 `CheckCompatibilityAsync`，此路徑先前無測試，故補上兩個（`test/PostgresBackup.Cli.Tests/CheckToolsCommandTests.cs`）：

- `CheckToolsCommand_WhenConnectionStringGiven_PassesItThroughVerbatim` —— 連線字串原封不動抵達相容性檢查
- `CheckToolsCommand_WhenBothConnectionStringAndFieldsGiven_ConnectionStringWins` —— 同時給連線字串與分開欄位時的優先序，與移除欄位前一致

### 使用者可見行為

無變化。`-s` 照常運作，備份與還原本來就沒有連線字串入口。

## Comments

### 2026-09-20 — 本票的初稿低估了嚴重度

初稿描述為「連線檢查查一台、實際備份備另一台」。實際查證 `NpgsqlRestoreTargetCatalogReader` 與 `NpgsqlRestoreDataPreparationService` 後發現，分歧存在於**單次還原作業內部**，且涉及清空資料表這個破壞性動作。嚴重度因此上調，處理時機也從「延後」改為「立即」。

可達性未變（仍然沒有入口），所以這不是一次事故修補，而是在入口被打開之前先把牆立起來。
