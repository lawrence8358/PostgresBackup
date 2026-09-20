# 01: 建立「客戶端工具作業」模組

**What to build:** 建立一個負責「執行一次客戶端工具並留下備份紀錄」的模組，涵蓋環境變數政策、連線參數組裝、argv 跳脫與拼接、計時、結果判定與紀錄寫入。此票結束時尚無任何呼叫端，既有行為完全不變。

**Blocked by:** 無

**Status:** resolved

## 要做的事

- [x] 新增 `src/PostgresBackup.Core/Services/ClientToolRun.cs`（具體類別，**不設介面** —— 理由見 spec「不為模組設介面」）
- [x] 建構子接受 `IProcessRunner` 與可為 null 的 `IBackupHistoryRepository`
- [x] 實作環境變數組裝：`PGPASSWORD`（密碼非空時才設）、`PGCLIENTENCODING=UTF8`、`LC_ALL=C`、`LC_MESSAGES=C`、`LANG=C`、`LANGUAGE=null`
- [x] 實作連線參數前綴組裝，含 `localhost` / `5432` / `postgres` 三個後備值；還原情形下 `TargetDatabase` 優先於 `Connection.Database`
- [x] 實作 argv 拼接與引號規則：元素為空字串、或含空白字元或雙引號時加雙引號並跳脫內部雙引號，否則原樣輸出
- [x] 實作計時、離開碼判定、錯誤訊息擷取（`StandardError` 有值即用，否則用呼叫端傳入的資源鍵）
- [x] 實作備份紀錄寫入，成功與失敗各一條路徑，統一以 `[WARNING]` 記錄寫入失敗且不改變作業結果
- [x] `OperationCanceledException` 不攔截，向上傳播
- [x] 新增 `CONTEXT.md` 的「客戶端工具作業（Client Tool Run）」詞彙（內容見 spec「`CONTEXT.md` 詞彙新增」）
- [x] 於 `IProcessRunner` 補上兩項既有約定的文件註解：環境變數 `null` 值代表移除該變數；`arguments` 為已跳脫的完整命令列字串而非參數陣列
- [x] 新增測試檔，涵蓋 spec「新增測試」所列全部項目
- [x] `dotnet build` 與 `dotnet test` 全綠

## 介面形狀

呼叫端交出的是**資料**，不是命令列字串。大致形狀（實作時可微調命名，但不得把跳脫責任推回呼叫端）：

```
RunAsync(
    工具執行檔路徑,
    作業專屬 argv 清單        // IReadOnlyList<string>，未跳脫
    連線設定,
    目標資料庫（可為 null，null 時採用連線設定中的資料庫）,
    記錄前綴                  // "pg_dump"、"pg_restore"、"psql"
    作業類型,                 // BackupOperationType
    紀錄用的檔案路徑,
    紀錄用的備份格式,
    非零離開碼時的錯誤資源鍵,
    onLogLine,
    CancellationToken)
```

回傳一個模組內部的結果型別，攜帶離開碼、耗時、拼接完成的命令列字串（供 `BackupRecord.Arguments` 與對外結果使用）、錯誤訊息。

`BackupResult` / `RestoreResult` **不合併**，由兩個服務各自從此結果轉換。

## 注意事項

**紀錄用的檔案大小**：`BackupRecord.FileSizeBytes` 在備份路徑是產出檔案的大小、在還原路徑是來源檔案的大小，且備份必須在確認檔案存在之後才取得。此欄位的取得方式因作業而異，不要在模組內硬寫一種 —— 交由呼叫端提供，或以委派傳入。

**`-p` 連接埠的外觀會改變**：現行一律輸出 `-p 5432`（無引號），新規則同樣不加引號，此處行為一致。但 `-h localhost`、`-d mydb` 這類值會從 `-h "localhost"` 變成 `-h localhost`。這是 spec 已接受的外觀變化，不是缺陷，但實作時要有意識 —— 它會影響 `BackupRecord.Arguments` 存下的字串。

**不要順手改 `RestoreArgumentsBuilder` 裡的兩個 `InvalidOperationException`**，那屬候選 2 的範圍。
