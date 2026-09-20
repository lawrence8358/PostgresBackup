# 03: `RestoreService` 改用客戶端工具作業

**What to build:** 讓 `RestoreService` 把工具執行、環境變數、計時、結果判定與紀錄寫入交給客戶端工具作業；`RestoreArgumentsBuilder` 同步改為回傳 argv 清單。此票完成後，備份與還原兩條路徑上的五項重複全部消除。

**Blocked by:** 01, 02

**Status:** ready-for-agent

## 要做的事

- [ ] `RestoreArgumentsBuilder.Build` 回傳型別由 `string` 改為 `IReadOnlyList<string>`，移除其私有 `Escape`
- [ ] 產出的清單**不含**連線參數（`-h`、`-p`、`-U`、`-d`）
- [ ] `RestoreService` 注入並使用客戶端工具作業，移除 `BuildEnvironmentVariables`、自己的 `EscapeArgument`、`Stopwatch`、離開碼判定、錯誤訊息擷取、兩段 `AddRecordAsync`
- [ ] `--list` 那次呼叫（取得封存目錄）也改走客戶端工具作業或共用的拼接，**不得留下第三份跳脫實作**
- [ ] `RestoreService` 保留：客戶端工具偵測與挑選、目標資料庫預設、格式推斷、計畫產出與驗證、暫存 `.list` 檔生命週期、安全快照協調、`RestoreResult` 轉換
- [ ] 安全快照**維持**呼叫 `IBackupService.BackupAsync`，不改為直接使用客戶端工具作業
- [ ] 改寫 `RestoreArgumentsBuilderTests` 的 5 個測試
- [ ] 調整 `RestoreServiceTests` 的 8 個測試與 `TestProcessRunner`
- [ ] 調整 `RestoreTargetDatabaseTests` 的 1 個測試
- [ ] `dotnet build` 與 `dotnet test` 全綠

## `TestProcessRunner` 的反解需要調整

現行實作靠字串反解取得快照檔路徑：

```csharp
var fileArgIndex = arguments.IndexOf("-f \"", StringComparison.Ordinal);
```

拼接規則改變後，不含空白的路徑不再帶引號，這段反解會失效。測試用的路徑多半位於暫存目錄且可能含空白，**兩種情形都要能處理**，不要只針對其中一種修補。

（更乾淨的做法是在客戶端工具作業之上攔截而非在 `IProcessRunner` 攔截，但那需要為模組設介面 —— spec 已決定不設。）

## 受影響的既有測試

`RestoreArgumentsBuilderTests` 5 個：

- `Build_CustomFormatNormal_RequiresFilteredArchiveList` — **行為不變**，`InvalidOperationException` 原樣保留
- `Build_CustomFormatNormal_UsesFilteredArchiveListAndProtectsExistingTableData` — 比對清單含 `--use-list` 與 `--no-data-for-failed-tables`
- `Build_CustomFormatDataOnly_RequiresOrderedArchiveList` — **行為不變**
- `Build_CustomFormatCleanAndRecreate_ContainsExpectedFlags` — 比對清單含 `--clean`、`--if-exists`
- `Build_PlainFormatSql_ContainsPsqlFileFlag` — 比對清單含 `-f` 與來源路徑

`RestoreServiceTests` 8 個（`RestoreAsync_WhenSnapshotFails_...`、`..._WhenSnapshotSucceeds_ExecutesRestoreAndRecordsBoth`、`..._NormalModeBuildsAndUsesFilteredArchiveList`、`..._NormalModeWithNothingMissing_SkipsSafetySnapshot`、`..._NormalModeWithUnsupportedEntry_...`、`..._DataOnlyInspectsArchiveBeforeWritingData`、`..._DataOnlyWithMissingTargetTable_...`、`..._DataOnlyWithForeignKeyCycle_...`）與 `RestoreTargetDatabaseTests` 1 個。

特別留意 `RestoreAsync_WhenSnapshotSucceeds_ExecutesRestoreAndRecordsBoth` —— 它斷言的正是「一次還原留下兩筆紀錄」這個刻意行為，必須在改動後依然成立。

## 不要順手做的事

- `RestoreAsync` 仍是一個很長的方法，本票**不**負責拆解它（候選 2）
- 兩個 `InvalidOperationException` 原樣保留（候選 2）
- `options.Format` 被直接改寫的問題原樣保留（候選 2）
- 暫存 `.list` 檔的生命週期原樣保留（候選 2）

## 驗收標準

改寫後任一既有測試若無法成立，代表本次引入了行為變更。**停止並回報，不得調整斷言使其通過。**
