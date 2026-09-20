# scripts — 排程備份用的現成腳本

這個目錄放的是**給使用者直接拿去用**的 PowerShell 腳本，不是專案的建置工具。

四支腳本都**不含任何資料庫密碼**，可以安全地放在共用位置，也可以簽入版控。密碼由
`pgbackup profile set` 事先以機器範圍加密存放，腳本只用 `--profile` 參照那筆設定。

完整的背景說明見 [`docs/USER_MANUAL.md` 第 5 節](../docs/USER_MANUAL.md#5-cli-自動化排程備份實戰指南-sop)。

---

## 檔案

| 檔案 | 用途 | 誰執行 | 要管理員權限？ |
| :--- | :--- | :--- | :---: |
| `register-backup-task.ps1` | **一次性設定。** 建立連線設定 → 註冊 SYSTEM 排程 → 立即試跑驗證 | 你，只跑一次 | 是 |
| `backup_task.ps1` | **每天實際執行的備份。** 備份、判斷成敗、依保留天數清理 | Windows 工作排程器，以 `SYSTEM` 身分，自動 | 由排程處理 |
| `check-backup-status.ps1` | **查看排程跑得好不好。** 排程狀態、上次退出碼、最近備份、日誌、磁碟空間 | 你，隨時 | **否** |
| `unregister-backup-task.ps1` | **移除排程。** 預設只移除排程任務，其餘一律保留 | 你，要收掉的時候 | 是（`-DryRun` 不用） |

一般情況下你只需要跑 `register-backup-task.ps1`，它會把 `backup_task.ps1` 註冊進工作排程器。

---

## 快速開始

以**系統管理員身分**開啟 PowerShell（兩件事都需要：寫入 `%ProgramData%`、註冊 SYSTEM 排程）：

```powershell
cd <專案目錄>\scripts
.\register-backup-task.ps1
```

腳本會逐項詢問，沒填的才問，密碼是遮蔽輸入（畫面上不會顯示任何字元）。跑完你會看到一份
「這些東西放在哪裡」的總結。

也可以先把答案都用參數帶進去，省掉互動：

```powershell
.\register-backup-task.ps1 `
    -ProfileName "正式環境" `
    -Database my_database -Username postgres `
    -BackupDir "D:\DatabaseBackups\my_database" `
    -PgBinPath "C:\Tools\pgsql\bin" `
    -CliPath "C:\Tools\PostgresBackup\pgbackup.exe" `
    -At 02:00 -RetentionDays 7 -SnapshotRetentionDays 30
```

只有密碼永遠不能用參數傳——那等於把密碼寫進 PowerShell 的操作歷史紀錄。

---

## `register-backup-task.ps1` 參數

| 參數 | 預設 | 說明 |
| :--- | :--- | :--- |
| `-ProfileName` | 互動詢問 | 命令列連線設定的名稱 |
| `-ServerHost` | `localhost` | PostgreSQL 主機位址 |
| `-Port` | `5432` | 連接埠 |
| `-Database` | 互動詢問 | 資料庫名稱 |
| `-Username` | 互動詢問 | 資料庫使用者 |
| `-CliPath` | 自動偵測 | `pgbackup.exe` 的完整路徑 |
| `-PgBinPath` | 無 | 官方客戶端工具的 `bin` 目錄。工具已在系統 PATH 時可留空；**用免安裝可攜版時必填** |
| `-BackupDir` | 互動詢問 | 備份檔輸出目錄 |
| `-ScriptPath` | 同目錄的 `backup_task.ps1` | 要註冊進排程的備份腳本 |
| `-TaskName` | `PostgresBackup_Daily` | 排程任務名稱 |
| `-At` | `02:00` | 每日執行時間 |
| `-RetentionDays` | `7` | 備份檔保留天數 |
| `-SnapshotRetentionDays` | `30` | 還原前快照保留天數 |
| `-SkipProfile` | — | 連線設定已經建好了，只註冊排程 |
| `-SkipTestRun` | — | 註冊完不要立刻試跑 |

它做的檢查（任何一項不過就直接停下來，不會做一半）：

- 是否為系統管理員身分
- `pgbackup.exe` 與 `backup_task.ps1` 是否存在，**且是否被放在 `SYSTEM` 讀不到的個人資料夾底下**
- 客戶端工具是否就緒（跑一次 `check-tools`）
- 連線設定是否真的寫進去、`profile list` 讀不讀得到、存放區權限有無警告

---

## `backup_task.ps1` 參數

這支通常由排程呼叫，不必手動跑。要手動測試的話一樣需要系統管理員身分（要讀機器範圍的連線設定）。

| 參數 | 預設 | 說明 |
| :--- | :--- | :--- |
| `-ProfileName` | **必填** | 命令列連線設定名稱 |
| `-BackupDir` | **必填** | 備份檔輸出目錄 |
| `-CliPath` | **必填** | `pgbackup.exe` 的完整路徑 |
| `-PgBinPath` | 無 | 客戶端工具 `bin` 目錄（已在 PATH 則可留空） |
| `-Format` | `custom` | `custom`（自訂二進位，建議）或 `plain`（純文字 SQL） |
| `-Mode` | `all` | `all` / `schema` / `data` |
| `-RetentionDays` | `7` | 備份檔保留天數，設 `0` 表示不清理 |
| `-SnapshotRetentionDays` | `30` | 還原前快照保留天數，設 `0` 表示不清理 |
| `-LogRetentionDays` | `30` | 執行日誌保留天數，設 `0` 表示不清理 |
| `-SkipCleanup` | — | 只備份，完全不清理 |

**退出碼**：`0` 成功 ／ `1` 備份失敗 ／ `2` 設定或環境有問題（還沒開始備份就停了）。

---

## 這些腳本替你擋掉的坑

都是實測踩出來的，照抄網路上的範例不會有這些處理：

- **保留政策要能真的刪到檔案。** `Get-ChildItem -Path $dir -Include "*.dump"` 在沒有 `-Recurse`、
  且路徑結尾沒有 `\*` 的情況下，`-Include` 會被**完全忽略**，一個檔案都不刪卻毫無錯誤訊息——
  備份目錄會無聲無息地一直長大，直到磁碟滿了才發現。這裡改用取回全部檔案再以副檔名過濾。
- **用退出碼判斷成敗，不要看目錄裡有沒有檔案。** 備份失敗時 `pg_dump` 會留下一個 0 位元組的空檔
  （它先建輸出檔、再去連資料庫）。`backup_task.ps1` 會在失敗時把**這一輪**產生的空檔清掉，
  而且只清這一輪的，不會誤刪既有檔案。
- **還原前快照會佔用等量空間。** `pgbackup restore` 會把快照寫到來源備份檔所在目錄下的
  `snapshots\`，等於要再放得下一份完整備份。這裡用獨立的 `-SnapshotRetentionDays` 管理，
  預設留得比例行備份久，因為那是「還原前的救命繩」。
- **排程以 `SYSTEM` 身分執行，讀不到你的個人資料夾。** `register-backup-task.ps1` 會檢查
  `pgbackup.exe`、`backup_task.ps1` 與備份目錄有沒有放在個人資料夾底下，有的話會警告。
- **連線設定不記錄工具路徑。** `--profile` 只記「連哪台資料庫、用什麼帳密」，所以用可攜版工具時
  排程仍需帶 `--pg-bin-path`，漏掉會失敗。`register-backup-task.ps1` 會把它一併寫進排程指令。
- **註冊排程用 `Register-ScheduledTask` 而不是 `schtasks /TR`。** 後者的命令字串有長度上限
  （約 261 字元），路徑一長就會被無聲截斷。

---

## 移除排程

用 `unregister-backup-task.ps1`。**預設只移除排程任務，備份檔與連線設定都留著**——停掉排程
跟丟掉既有備份是兩件完全不同的決定，腳本不會替你做第二個。

先看看會動到什麼（**不需要管理員權限**，也不會刪任何東西）：

```powershell
.\unregister-backup-task.ps1 -BackupDir "D:\DatabaseBackups\my_database" -DryRun
```

確認無誤後，以系統管理員身分把 `-DryRun` 拿掉重跑：

```powershell
# 只停排程（最常見）
.\unregister-backup-task.ps1

# 停排程並清掉連線設定（含其加密密碼）
.\unregister-backup-task.ps1 -RemoveProfile -ProfileName "正式環境"

# 整套拆乾淨，連備份檔一起刪（會刪掉資料，刪了拿不回來）
.\unregister-backup-task.ps1 -RemoveProfile -ProfileName "正式環境" `
                             -RemoveStore -RemoveBackups -BackupDir "D:\DatabaseBackups\my_database"
```

| 參數 | 預設 | 說明 |
| :--- | :--- | :--- |
| `-TaskName` | `PostgresBackup_Daily` | 要移除的排程任務名稱 |
| `-RemoveProfile` | — | 一併移除連線設定（需搭配 `-ProfileName`） |
| `-ProfileName` | — | 要移除的連線設定名稱 |
| `-CliPath` | 自動偵測 | `pgbackup.exe` 路徑，`-RemoveProfile` 時需要 |
| `-RemoveStore` | — | 一併刪除 `%ProgramData%\PostgresBackup` 整個目錄 |
| `-RemoveBackups` | — | **一併刪除整個備份目錄**（需搭配 `-BackupDir`） |
| `-BackupDir` | — | 備份目錄，用於盤點與 `-RemoveBackups` |
| `-DryRun` | — | 只顯示會做什麼，不實際執行。**不需要管理員權限** |
| `-Yes` | — | 不跳出互動確認（自動化用） |
| `-Force` | — | 存放區中仍有其他連線設定時，仍然刪除存放區 |

**退出碼**：`0` 全部順利 ／ `1` 有項目失敗 ／ `2` 環境或參數有問題。

它的幾個安全設計：

- **預設什麼資料都不刪。** 要刪連線設定、存放區、備份檔，都得明確加上對應參數。
- **刪除前先盤點。** 會列出排程狀態、存放區檔案數、備份檔數量與總大小、最新一份的日期，
  讓你在按下去之前看清楚自己要刪掉什麼。
- **破壞性動作要求輸入完整路徑確認**，不是打個 `y` 就算數（比照本工具還原作業的防呆）。
  在沒有人值守的環境下讀不到輸入，會安全取消而不是硬做。`-Yes` 可略過，供自動化使用。
- **存放區裡還有其他連線設定時拒絕刪除。** 同一台機器可能有多組排程共用那個存放區，
  刪掉會一起弄壞。要強制刪除得再加 `-Force`。
- **找不到排程任務不算錯誤**，會提示你可能用了不同的 `-TaskName` 並給出列出所有任務的指令。
- **明講什麼沒被刪。** 結尾會列出它不會碰的東西（兩份稽核歷史、`pgbackup.exe` 與客戶端工具本身），
  免得你以為已經清乾淨了。

---

## 事後維護

```powershell
# 看排程狀態與上次執行結果（不必剖析文字輸出）
Get-ScheduledTask -TaskName "PostgresBackup_Daily"
Get-ScheduledTaskInfo -TaskName "PostgresBackup_Daily" | Select-Object LastRunTime, LastTaskResult, NextRunTime

# 手動觸發一次
Start-ScheduledTask -TaskName "PostgresBackup_Daily"

# 換了資料庫密碼（以系統管理員身分，重跑一次即可，排程不用動）
pgbackup profile set --name "正式環境" -H localhost -d my_database -u postgres

# 全部移除（建議改用 unregister-backup-task.ps1，它會先盤點再確認）
Unregister-ScheduledTask -TaskName "PostgresBackup_Daily" -Confirm:$false
pgbackup profile remove --name "正式環境"
```

> **排程的執行結果不會出現在圖形介面的「備份歷史」頁面。** 稽核歷史是依 Windows 帳號各自獨立的，
> `SYSTEM` 那一份寫在 `C:\Windows\System32\config\systemprofile\AppData\Local\PostgresBackup\history.db`。
> 要確認排程跑得好不好，請看備份目錄底下的 `logs\`。
