# 14: 圖形介面「測試連線」失敗時例外訊息可能回吐含密碼的連線字串

**What to build:** 堵住一條窄但真實的密碼外洩管道——圖形介面測試連線失敗時，顯示給使用者的例外訊息有可能包含連線字串，而連線字串含密碼。

**Blocked by:** 無

**Status:** resolved

## 問題

`SettingsViewModel.TestConnectionAsync` 會把含密碼的連線字串交給資料庫驅動程式。連線字串格式異常時，驅動程式拋出的例外訊息**有可能包含其內容**。

發生條件狹窄：需密碼含特殊字元導致連線字串格式異常。`ConnectionSettings.ToConnectionString()` 目前使用 `NpgsqlConnectionStringBuilder`（會正確跳脫），因此機率低——但這條路徑上沒有任何遮蔽，一旦成立就是明文密碼直接顯示在介面上。

此項在原始設計訪談的稽核中發現，spec 明列於 **Out of Scope** 並建議另立項目處理，故不屬票 01–10 任何一張。

## 參考實作

票 05 已在命令列的 `profile set` 路徑加入 `ProfileCommand.Redact(message, password)`，把訊息中出現的密碼換成 `***`。本票可沿用同樣的做法，或把該方法抽到共用位置供兩邊使用。

注意票 12 指出 `ToolDetectionService.VerifyConnectionAsync` 的記錄器會繞過 `Redact` 先印出完整例外——若兩票一起處理，遮蔽應涵蓋**顯示**與**記錄**兩條路徑。

## 驗收

- [x] 圖形介面測試連線失敗時，顯示的訊息不包含密碼 — `SettingsViewModel.TestConnectionAsync` 的兩條失敗路徑（相容性檢查回傳失敗、以及擲出例外）都改為經 `SensitiveText.Redact` 後才呈現
- [x] 記錄（logger）路徑同樣不會回吐密碼 — 見票 12 第一項，`ToolDetectionService.VerifyConnectionAsync` 的 `LogError(ex, ...)` 改為只記錄例外型別名稱的 `LogDebug`
- [x] 補上測試 — `SettingsViewModelTestConnectionRedactionTests`（3 項）
- [x] 不改變測試連線本身的行為與成功／失敗判定 — 第三個測試即為此而設

## Comments

### 2026-09-19 — 實作完成

票 05 的 `ProfileCommand.Redact` 已抽到 `PostgresBackup.Core.Services.SensitiveText.Redact`，命令列與圖形介面共用同一份規則；`ProfileCommand.Redact` 保留為轉呼叫的薄殼，呼叫端不受影響。

與票 12 第一項一併處理，因此遮蔽同時涵蓋**顯示**與**記錄**兩條路徑（本票原文即提醒過這一點）。
