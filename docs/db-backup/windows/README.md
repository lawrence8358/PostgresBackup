# Windows：DB 備份與還原 v1.0.0

[下載 Windows 完整交接包](https://github.com/lawrence8358/PostgresBackup/releases/latest/download/PostgresBackup-Windows-x64.zip)

Windows 10/11 x64、PowerShell 5.1，預留 5 GB 加備份空間。整包解壓到 `D:\DBBackup`；設定排程後勿搬動，搬家或改連線須先暫停舊排程，再重新設定。

軟體已附：[PostgreSQL](software/postgresql-18.6-windows-x64.zip)、[CLI](software/PostgresBackup-CLI-windows-x64.zip)、[VC++](software/vc_redist.x64.exe)。未裝 VC++ 請先安裝。

## 1. 正式排程

向 DBA 取得正式 `DB_HOST`、`DB_PORT`、`DB_NAME`、`DB_USER`、`DB_PASSWORD`，確認帳號有備份／還原權限。**以系統管理員開啟 Windows PowerShell**：

```powershell
Set-Location D:\DBBackup
Set-ExecutionPolicy -Scope Process Bypass
notepad .\production.json
```

填入前四個連線值與 `BACKUP_DIR`（例如 `D:/Backups/Postgres`，用正斜線）；密碼留待指令遮蔽詢問，不填測試值。

```powershell
.\scripts\00-install-software.ps1
.\scripts\08-register-production.ps1 -At 02:00
.\scripts\11-check-production.ps1
```

輸入 `DB_PASSWORD` 後，每天本機時間 02:00 由 SYSTEM 備份，登出仍執行；建立時立即試跑。**SYSTEM 排程需管理員，本次非管理員環境未實測此權限路徑，請確認試跑結果。** 密碼檔在 `work\production`，勿分享。

## 2. 查結果

```powershell
.\scripts\11-check-production.ps1
```

確認上次結果 `0`、`BACKUP_DIR` 有最近一天的非空 `.dump`；失敗查 `BACKUP_DIR\logs` 或 `work\production\last-error.log`。手動備份：`.\scripts\09-backup-production.ps1`。備份保留 7 天、快照 30 天。

## 3. 還原

停止應用程式寫入，核對 `production.json` 目標；以下仍用管理員 PowerShell。先暫停排程，若提示作業仍在執行，等完成再執行 Pause。

```powershell
.\scripts\11-check-production.ps1 -Action Pause
$backupFile = Read-Host '輸入選定 .dump 的完整路徑（不加引號）'
.\scripts\10-restore-production.ps1 -BackupFile $backupFile
```

輸入畫面顯示的 `DB_NAME` 確認；程式先存快照，再清除並還原備份涵蓋的物件。請 DBA 核對資料後恢復服務及排程：

```powershell
.\scripts\11-check-production.ps1 -Action Resume
```

快照在來源備份旁的 `snapshots`；要回復還原前狀態，用同一還原指令選擇快照檔。

## 可選：先練習

以下只動本包測試庫，密碼自動產生，勿拿去正式使用。一般 PowerShell 在本包目錄執行：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\01-prepare-demo.ps1
.\scripts\03-register-demo-schedule.ps1 -At 02:00 -LogonType Interactive
.\scripts\04-damage-demo.ps1
.\scripts\05-restore-demo.ps1
.\scripts\06-stop-demo.ps1
```

排程備份結果須為 `0`；還原看到成功訊息 即完成十萬筆演練。**此練習排程只在帳號已登入時執行**，最後一行停止練習排程與資料庫。
