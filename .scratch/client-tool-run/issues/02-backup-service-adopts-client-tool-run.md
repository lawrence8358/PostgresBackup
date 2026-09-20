# 02: `BackupService` 改用客戶端工具作業

**What to build:** 讓 `BackupService` 把工具執行、環境變數、計時、結果判定與紀錄寫入全部交給客戶端工具作業，自己只保留備份專屬的責任；`BackupArgumentsBuilder` 同步改為回傳 argv 清單。

**Blocked by:** 01

**Status:** ready-for-agent

## 要做的事

- [ ] `BackupArgumentsBuilder.Build` 回傳型別由 `string` 改為 `IReadOnlyList<string>`，移除其私有 `Escape`
- [ ] 產出的清單**不含**連線參數（`-h`、`-p`、`-U`、`-d`），那部分由客戶端工具作業補上
- [ ] `BackupService` 注入並使用客戶端工具作業，移除其中的環境變數區塊、`Stopwatch`、離開碼判定、錯誤訊息擷取、兩段 `AddRecordAsync`
- [ ] `BackupService` 保留：客戶端工具偵測、輸出目錄預設值與建立、`GetTargetFilePath`、產出檔案存在判定、`FormatBytes`、開頭四行記錄、`BackupResult` 轉換
- [ ] 更新 `App.xaml.cs` 與 `Program.cs` 的相依註冊
- [ ] 改寫 `BackupArgumentsBuilderTests` 的 4 個測試，斷言對象改為 argv 清單
- [ ] 調整 `BackupServiceTests` 的 3 個測試與 `FakeProcessRunner` 的參數擷取
- [ ] `dotnet build` 與 `dotnet test` 全綠

## 受影響的既有測試

| 測試 | 驗證的行為 | 改寫方式 |
| --- | --- | --- |
| `Build_CustomFormatFullDatabase_ContainsExpectedFlags` | 自訂格式產生 `-Fc` | 比對清單含 `-Fc` |
| `Build_PlainFormatSchemaOnly_ContainsSchemaOnlyAndFp` | 純文字＋僅結構產生 `-Fp --schema-only` | 同上 |
| `Build_SpecificSchemas_AppendsSchemaFlags` | 指定綱要產生 `-n` | 比對清單含 `-n` 與綱要名（綱要名不再帶引號） |
| `Build_SpecificTables_AppendsTableFlags` | 指定資料表產生 `-t` | 同上 |
| `BackupAsync_WhenSuccessful_ReturnsSuccessAndFileDetails` | 成功路徑 | 視 `FakeProcessRunner` 調整 |
| `BackupAsync_WhenProcessFails_ReturnsFailure` | 失敗路徑 | 同上 |
| `BackupAsync_WhenToolNotFound_ReturnsFailureWithoutRunning` | 工具找不到時不執行 | 應無需改動（未進入執行階段） |

`FakeProcessRunner` 既有的 `CapturedPassword`、`CapturedClientEncoding`、`CapturedLocaleAll`、`CapturedLocaleMessages`、`CapturedLanguage`、`LanguageVariableRemoved` 全部保留 —— 環境變數政策雖然搬了家，這些斷言驗證的是**經由 `BackupService` 這條路徑跑完之後環境變數確實正確**，仍然成立且仍然有價值。

## 驗收標準

改寫後任一既有測試若無法成立，代表本次引入了行為變更。**停止並回報，不得調整斷言使其通過。**

唯一預期的行為變化是 `BackupRecord.Arguments` 存下的字串中，不含空白的值不再帶引號（spec 已接受）。
