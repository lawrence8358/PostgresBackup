# scripts — 排程備份用的現成腳本

這四支腳本使用 CLI 連線設定執行 SYSTEM 排程，不存放資料庫密碼。

完整的背景說明見 [`docs/USER_MANUAL.md` 第 5 節](../docs/USER_MANUAL.md#5-cli-自動化排程備份實戰指南-sop)。

---

## 檔案

| 檔案 | 用途 | 誰執行 | 要管理員權限？ |
| :--- | :--- | :--- | :---: |
| `register-backup-task.ps1` | **一次性設定。** 建立連線設定 → 註冊 SYSTEM 排程 → 立即試跑驗證 | 你，只跑一次 | 是 |
| `backup_task.ps1` | **每天實際執行的備份。** 備份、判斷成敗、依保留天數清理 | Windows 工作排程器，以 `SYSTEM` 身分，自動 | 由排程處理 |
| `check-backup-status.ps1` | **查看排程跑得好不好。** 排程狀態、上次退出碼、最近備份、日誌、磁碟空間 | 你，隨時 | **否** |
| `unregister-backup-task.ps1` | **移除排程。** 預設只移除排程任務，其餘一律保留 | 你，要收掉的時候 | 是（`-DryRun` 不用） |

---

## 快速開始

先依 [Windows 手冊](../docs/sop/windows-cli.md#1-準備工具)準備 `pgbackup.exe` 與 PostgreSQL 工具，放在 `C:\Tools` 等固定位置。

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

## 使用提醒

- 以退出碼與日誌判斷成功，不能只看備份檔是否存在。
- 可攜式 PostgreSQL 工具須指定 `-PgBinPath`。
- 備份、快照與日誌分別套用保留天數，請確認磁碟空間。
- SYSTEM 歷史與目前使用者歷史分開，查看排程結果請用 `check-backup-status.ps1`。
## 移除排程

用 `unregister-backup-task.ps1`，預設只移除任務，保留備份與連線設定。

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

預設只移除任務；刪除設定或備份須明確加參數。破壞性操作會先盤點並要求確認。

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
