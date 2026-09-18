# PostgresBackup 完整使用者手冊 (User Manual)

> **版本**：v1.0.0  
> **適用環境**：Windows 10 / 11 / Windows Server 2019+ (.NET 10.0)  
> **支援資料庫**：PostgreSQL 12 ~ 18.x（完全支援 PostgreSQL 18.6）

---

## 目錄
1. [系統簡介與特色](#1-系統簡介與特色)
2. [PostgreSQL 官方客戶端工具下載與安裝 SOP](#2-postgresql-官方客戶端工具下載與安裝-sop)
   - [方法 A：官方 EDB 可攜式免安裝版 (最推薦)](#方法-a官方-edb-可攜式免安裝版-最推薦)
   - [方法 B：Windows Winget 套件管理員安裝](#方法-bwindows-winget-套件管理員安裝)
   - [驗證工具就緒狀態 SOP](#驗證工具就緒狀態-sop)
3. [WPF 現代化圖形介面使用指南](#3-wpf-現代化圖形介面使用指南)
   - [3.1 環境檢查與連線設定](#31-環境檢查與連線設定)
   - [3.2 執行資料庫備份](#32-執行資料庫備份)
   - [3.3 執行安全還原 (含前置快照)](#33-執行安全還原-含前置快照)
   - [3.4 歷史紀錄與不可變稽核](#34-歷史紀錄與不可變稽核)
   - [3.5 全域即時日誌串流](#35-全域即時日誌串流)
4. [CLI 命令列工具操作指南](#4-cli-命令列工具操作指南)
   - [4.1 工具狀態診斷 (`check-tools`)](#41-工具狀態診斷-check-tools)
   - [4.2 備份作業 (`backup`)](#42-備份作業-backup)
   - [4.3 還原作業 (`restore`)](#43-還原作業-restore)
5. [CLI 自動化排程備份實戰指南 (SOP)](#5-cli-自動化排程備份實戰指南-sop)
   - [5.1 自動化備份腳本範例 (`backup_task.ps1`)](#51-自動化備份腳本範例-backup_taskps1)
   - [5.2 註冊至 Windows 工作排程器 (Task Scheduler)](#52-註冊至-windows-工作排程器-task-scheduler)
   - [5.3 驗證排程與日誌檢視](#53-驗證排程與日誌檢視)
6. [常見問題排查 (FAQ)](#6-常見問題排查-faq)

---

## 1. 系統簡介與特色

`PostgresBackup` 是一套專為企業與開發團隊打造的 PostgreSQL 資料庫備份與安全還原管理系統。不同於常見的第三方轉譯工具，本系統具有以下核心特色：

- **官方工具核心驅動**：底層完全呼叫 PostgreSQL 官方原生之 `pg_dump`、`pg_restore` 與 `psql`，杜絕因驅動轉譯產生的資料型態失真。
- **雙模式整合體驗**：
  - **WPF GUI**：具備翡翠綠現代化介面、多國語系（繁體中文 / 英文），自適應視窗縮放與響應式排版。
  - **CLI 命令列 (`pgbackup`)**：可直接整合至 Windows Task Scheduler、CI/CD 與自動化營運排程。
- **安全防護核心**：
  - **密碼隔離**：透過內部處理序環境變數 `PGPASSWORD` 傳遞密碼，不暴露於命令列參數或處理序監視器。
  - **Pre-Restore Snapshot（前置快照）**：執行資料庫還原覆蓋前，系統強制先行建立防禦性全庫備份，預防人為操作失誤。
  - **破壞性操作防呆比對**：還原介面要求輸入目標資料庫名稱雙重確認，方可解除執行鎖定。
  - **不可變稽核軌跡**：所有作業自動永久記錄至 SQLite 稽核資料庫 (`history.db`)。

---

## 2. PostgreSQL 官方客戶端工具下載與安裝 SOP

### ⚖️ 商業授權與合規性保證 (Commercial Licensing Compliance)

企業在選擇備份工具時，軟體授權合規性至關重要。本專案所推薦與引導下載之所有 PostgreSQL 客戶端工具，**100% 合法允許商業營利用途，無需支付任何版權或授權費用**：

- **PostgreSQL License (寬鬆自由開源授權)**：
  - PostgreSQL 核心及其官方附屬工具（`pg_dump`、`pg_restore`、`psql`、`libpq`）均發布於 **PostgreSQL License**（為開放源碼促進會 OSI 認證之寬鬆授權，性質與 MIT / BSD 相當）。
  - **條款核心規範**：「*Permission to use, copy, modify, and distribute this software and its documentation for any purpose, without fee, and without a written agreement is hereby granted...*」
  - 明確允許任何個人與企業於**商業環境、生產系統、專有閉源產品**中免費使用、部署與發行，不具備 Copyleft（無 GPL 傳染性問題）。
- **EnterpriseDB (EDB) Windows Community Binaries 合規地位**：
  - EnterpriseDB 是 PostgreSQL 全球開發社群 (PGDG) 官方核心贊助商與長期維護者。
  - 其所提供的「PostgreSQL Windows Binaries」為 PGDG 官方專案所授權之純社群版本，其二進位程式與相依庫同樣遵循標準 **PostgreSQL License**，**企業商業使用完全免費合法**（與其商業付費版產品 EPAS 嚴格分開）。
- **Microsoft Winget 來源合規性**：
  - 套件 `PostgreSQL.PostgreSQL.18` 係直接由社群向官方站點拉取驗證之發行封裝，符合企業開源軟體採購與治理規範。

---

### 方法 A：官方 EDB 可攜式免安裝版 (最推薦)
> **優勢**：無須管理員安裝權限、乾淨綠色免寫入登錄檔、100% 允許商業使用、可任意放置於專案或工具目錄。

#### 下載與解壓縮步驟：
1. 開啟 EnterpriseDB 官方二進位下載頁面：  
   👉 **[PostgreSQL Windows Binaries (EDB)](https://www.enterprisedb.com/download-postgresql-binaries)**
2. 選擇對應版本（例如 `18.6` 或 `17.x`）的 **Windows x86-64** ZIP 壓縮檔下載。
3. 將壓縮檔內的 `pgsql` 目錄解壓至本機固定路徑，例如：  
   `C:\Tools\pgsql\` 或 `D:\Tools\pgsql\`
4. 確認該目錄下的 `bin` 資料夾包含以下核心檔案：
   - `pg_dump.exe` (備份核心)
   - `pg_restore.exe` (自訂二進位還原核心)
   - `psql.exe` (SQL 還原核心與診斷)
   - `libpq.dll`, `libssl-*.dll`, `libcrypto-*.dll` (連線通訊與加密庫)

#### 🚀 PowerShell 自動化下載安裝指令碼：
您可以開啟 PowerShell 直接執行以下腳本自動完成下載解壓：
```powershell
# 設定下載目錄與儲存路徑
$targetDir = "C:\Tools"
if (!(Test-Path $targetDir)) { New-Item -ItemType Directory -Path $targetDir -Force }
$zipPath = "$env:TEMP\postgresql-binaries.zip"

Write-Host "正在下載 PostgreSQL 官方 Windows 客戶端工具..."
# 下載官方 EDB 封裝二進位檔 (以 18.x / 17.x 為例)
Invoke-WebRequest -Uri "https://sbp.enterprisedb.com/getfile.jsp?fileid=1259028" -OutFile $zipPath

Write-Host "正在解壓縮至 $targetDir\pgsql..."
Expand-Archive -Path $zipPath -DestinationPath $targetDir -Force
Remove-Item $zipPath -Force

Write-Host "安裝完成！工具目錄路徑為：$targetDir\pgsql\bin"
```

---

### 方法 B：Windows Winget 套件管理員安裝
> **優勢**：一行指令全自動配置環境變數，適合本機開發機。

1. 以系統管理員身分開啟 PowerShell 或 Windows Terminal。
2. 執行官方套件安裝指令：
   ```powershell
   winget install PostgreSQL.PostgreSQL.18
   ```
3. 預設工具將安裝於：  
   `C:\Program Files\PostgreSQL\18\bin`
4. 安裝完成後，重新啟動終端機，`pg_dump` 將自動加入系統 PATH。

---

### 驗證工具就緒狀態 SOP

#### 1. 透過 CLI 驗證：
執行以下命令檢查工具狀態：
```powershell
# 若已加入 PATH：
pgbackup check-tools

# 若使用自訂目錄：
pgbackup check-tools --pg-bin-path "C:\Tools\pgsql\bin"
```
**預期輸出**：
```text
================================================================================
  PostgreSQL 客戶端工具診斷報告 (Client Tools Diagnostic Report)
================================================================================
  工具狀態: [ 就緒 / READY ]
  偵測來源: CustomPath
  pg_dump   : C:\Tools\pgsql\bin\pg_dump.exe
  pg_restore: C:\Tools\pgsql\bin\pg_restore.exe
  psql      : C:\Tools\pgsql\bin\psql.exe
  工具版本  : 18.6
================================================================================
```

#### 2. 透過 WPF 介面驗證：
1. 啟動 `PostgresBackup.Wpf.exe`。
2. 進入「**設定**」分頁。
3. 若系統未自動偵測到工具，請於「**自訂工具目錄 (bin)**」填入路徑（如 `C:\Tools\pgsql\bin`）。
4. 點擊「**重新偵測**」，狀態指示燈轉為綠色「**Ready 客戶端工具已就緒（版本: 18.6）**」即代表就緒。

---

## 3. WPF 現代化圖形介面使用指南

### 3.1 環境檢查與連線設定
- **工具路徑設定**：支援自動偵測系統環境變數與自訂路徑，路徑過長時自動以省略號顯示，並支援即時點擊瀏覽目錄。
- **連線設定檔管理**：
  - 支援「**新增連線設定檔**」、「**刪除設定檔**」與「**儲存變更**」。
  - 密碼輸入框具備密碼隱藏防偷窺機制，並支援一鍵測試資料庫連線。

### 3.2 執行資料庫備份
1. 切換至「**備份**」分頁。
2. **選取連線設定檔**：從下拉選單選取欲備份的目標伺服器與資料庫。
3. **設定備份格式**：
   - `自訂二進位格式 (Custom, -Fc)` (**企業生產環境強烈建議**)：支援高效 gzip 壓縮、平行還原與綱要/資料彈性過濾。
   - `純文字 SQL 格式 (Plain, -Fp)`：生成標準 SQL 腳本，可用一般文字編輯器檢視或由 `psql` 執行。
4. **設定備份模式**：
   - `結構與資料 (Schema + Data)`：完整還原備份。
   - `僅結構 (Schema Only)`：僅傾印 DDL 建表與索引腳本。
   - `僅資料 (Data Only)`：僅備份資料表紀錄。
5. **設定備份範圍**：支援「完整資料庫」、「指定綱要名稱 (Schema)」或「指定資料表名稱 (Table)」。
6. **備份目錄與命名**：系統自動依 `{database}_{yyyyMMddHHmmss}.dump` 即時預覽輸出檔名。
7. 點擊「**開始執行備份**」，系統將自動切換至「即時日誌」顯示備份串流。

### 3.3 執行安全還原 (含前置快照)
1. 切換至「**還原**」分頁。
2. **選取備份來源檔案**：點擊「瀏覽...」選取 `.dump` 或 `.sql` 備份檔。
3. **指定目標資料庫**：選取目標連線設定檔並確認目標資料庫名稱。
4. **還原前強制建立安全快照 (Pre-Restore Snapshot)**：
   - **預設強制勾選**。系統將在進行可能具破壞性的還原覆蓋前，自動對目標資料庫執行一次完整的防禦性備份，確保隨時可退回還原前狀態。
5. **高危險操作確認防呆**：
   - 於警示框內核對目標資料庫名稱，勾選「我已確認目標資料庫無誤，並了解覆寫風險」。
6. 點擊「**確認並開始執行還原**」。

### 3.4 歷史紀錄與不可變稽核
- **不可變稽核清單**：所有備份、還原與前置快照作業均由 SQLite 原生儲存，包含作業時間、類型、目標庫、格式、檔案大小、耗時與成功狀態。
- **搜尋與篩選**：支援依作業類型（全部、備份、還原、安全快照）或關鍵字（資料庫名稱）快速過濾。
- **一鍵操作**：
  - **開啟檔案位置**：在 Windows 檔案總管中反白選取該備份檔。
  - **還原**：一鍵將該筆備份檔案帶入還原頁面快速發起作業。

### 3.5 全域即時日誌串流
- 顯示底層 `pg_dump` / `pg_restore` 之所有詳細執行進度與標準錯誤串流（如各資料表傾印進度、索引建立狀態）。
- 支援「**清空終端**」與「**複製日誌**」至剪貼簿功能。

---

## 4. CLI 命令列工具操作指南

CLI 工具名稱為 `pgbackup`（本機專案可直接以 `dotnet run --project src/PostgresBackup.Cli --` 或發布之 `pgbackup.exe` 執行）。

### 4.1 工具狀態診斷 (`check-tools`)
```powershell
pgbackup check-tools [選項]
```
#### 核心參數表：
| 參數 | 簡寫 | 說明 | 範例 |
| :--- | :---: | :--- | :--- |
| `--pg-bin-path` | | 官方工具 bin 目錄路徑 | `--pg-bin-path "C:\Tools\pgsql\bin"` |
| `--host` | `-H` | PostgreSQL 主機位址（提供連線資訊時，會一併檢驗伺服器版本相容性） | `-H localhost` |
| `--port` | `-P` | 連接埠 (預設 5432) | `-P 5432` |
| `--database` | `-d` | 資料庫名稱 | `-d my_database` |
| `--username` | `-u` | 資料庫使用者 | `-u postgres` |
| `--password` | `-p` | 資料庫密碼 (安全環境變數傳遞) | `-p "YourPassword123!"` |
| `--connection-string` | `-s` | 完整連線字串，用於伺服器版本相容性檢查 | `-s "Host=localhost;Database=db;..."` |
| `--json` | | 以 JSON 格式輸出診斷報告，便於腳本化健康檢查 | `--json` |

#### 常用範例：
1. **僅檢查本機工具是否就緒**：
   ```powershell
   pgbackup check-tools --pg-bin-path "C:\Tools\pgsql\bin"
   ```
2. **同時檢驗客戶端與伺服器版本相容性**：
   ```powershell
   pgbackup check-tools -H localhost -d my_database -u postgres -p "YourPassword123!" --pg-bin-path "C:\Tools\pgsql\bin"
   ```

> 命令成功時回傳結束代碼 `0`，工具未就緒或版本不相容時回傳非零值，可直接用於排程腳本與 CI 流程的前置檢查。

### 4.2 備份作業 (`backup`)
```powershell
pgbackup backup [選項]
```
#### 核心參數表：
| 參數 | 簡寫 | 說明 | 範例 |
| :--- | :---: | :--- | :--- |
| `--profile` | | 指定已儲存之連線設定檔名稱或識別碼 | `--profile "正式環境"` |
| `--host` | `-H` | PostgreSQL 主機位址 | `-H localhost` |
| `--port` | `-P` | 連接埠 (預設 5432) | `-P 5432` |
| `--database` | `-d` | 目標資料庫名稱 | `-d my_database` |
| `--username` | `-u` | 資料庫使用者 | `-u postgres` |
| `--password` | `-p` | 資料庫密碼 (安全環境變數傳遞) | `-p "YourPassword123!"` |
| `--format` | `-f` | 備份格式 (`custom` 或 `plain`) | `-f custom` |
| `--mode` | `-m` | 模式 (`all`, `schema`, `data`) | `-m all` |
| `--schema` | `-n` | 指定綱要 (可重複使用) | `-n public -n hangfire` |
| `--table` | `-t` | 指定資料表 (可重複使用) | `-t "public.Quote"` |
| `--output-dir` | `-o` | 輸出存放目錄 | `-o "D:\Backups"` |
| `--output-file` | | 直接指定輸出檔案完整路徑，覆寫自動產生的檔名 | `--output-file "D:\Backups\nightly.dump"` |
| `--pg-bin-path` | | 官方工具 bin 目錄路徑 | `--pg-bin-path "C:\Tools\pgsql\bin"` |
| `--log` | | 額外輸出詳細日誌檔路徑 | `--log "D:\Backups\run.log"` |

#### 常用範例：
1. **完整資料庫自訂二進位備份 (最常用)**：
   ```powershell
   pgbackup backup -H localhost -P 5432 -d my_database -u postgres -p "YourPassword123!" -f custom -o "D:\Backups" --pg-bin-path "C:\Tools\pgsql\bin"
   ```
2. **僅傾印結構 (Schema-Only) 純文字 SQL 檔**：
   ```powershell
   pgbackup backup -H localhost -d my_database -u postgres -p "YourPassword123!" -f plain -m schema -o "D:\Backups" --pg-bin-path "C:\Tools\pgsql\bin"
   ```

### 4.3 還原作業 (`restore`)
```powershell
pgbackup restore [選項]
```
#### 核心參數表：
| 參數 | 簡寫 | 說明 | 範例 |
| :--- | :---: | :--- | :--- |
| `--file` | `-f` | **必填。** 來源備份檔案路徑 (`.dump` 或 `.sql`) | `-f "D:\Backups\my_database.dump"` |
| `--profile` | | 指定已儲存之連線設定檔名稱或識別碼 | `--profile "正式環境"` |
| `--host` | `-H` | PostgreSQL 主機位址 | `-H localhost` |
| `--port` | `-P` | 連接埠 (預設 5432) | `-P 5432` |
| `--database` | `-d` | 目標資料庫名稱 | `-d my_database` |
| `--username` | `-u` | 資料庫使用者 | `-u postgres` |
| `--password` | `-p` | 資料庫密碼 (安全環境變數傳遞) | `-p "YourPassword123!"` |
| `--mode` | `-m` | 還原模式 (`normal`, `clean`, `data`)，預設 `normal` | `-m clean` |
| `--no-snapshot` | | **關閉**還原前安全快照（預設為自動啟用） | `--no-snapshot` |
| `--yes` | `-y` | 自動同意高危險操作確認，不跳出互動提示 | `--yes` |
| `--pg-bin-path` | | 官方工具 bin 目錄路徑 | `--pg-bin-path "C:\Tools\pgsql\bin"` |
| `--log` | | 額外輸出詳細日誌檔路徑 | `--log "D:\Backups\restore.log"` |

> **注意**：還原前安全快照為**預設啟用**，無須額外加上任何參數；`--no-snapshot` 是用來「關閉」它的。關閉快照等同於放棄還原後的回復能力，除非目標資料庫可隨意丟棄，否則請勿使用。

#### 還原模式說明：
| 模式 | 對應官方參數 | 說明 |
| :--- | :--- | :--- |
| `normal` | （無） | 一般還原，建立遺漏物件，不刪除既有物件 |
| `clean` | `--clean --create` | 清除並重建，覆寫既有物件 |
| `data` | `--data-only` | 僅寫入資料，不變更結構 |

#### 常用範例：
1. **執行安全還原 (自動建立前置快照，並於覆寫前互動確認)**：
   ```powershell
   pgbackup restore -f "D:\Backups\my_database_20260917.dump" -H localhost -d my_database -u postgres -p "YourPassword123!" --pg-bin-path "C:\Tools\pgsql\bin"
   ```
2. **無人值守還原 (排程腳本用，略過互動確認但仍保留安全快照)**：
   ```powershell
   pgbackup restore -f "D:\Backups\my_database_20260917.dump" -H localhost -d my_database -u postgres -p "YourPassword123!" --yes --pg-bin-path "C:\Tools\pgsql\bin"
   ```
3. **清除並重建模式還原 (完整覆寫目標資料庫)**：
   ```powershell
   pgbackup restore -f "D:\Backups\my_database_20260917.dump" -H localhost -d my_database -u postgres -p "YourPassword123!" -m clean --yes --pg-bin-path "C:\Tools\pgsql\bin"
   ```

---

## 5. CLI 自動化排程備份實戰指南 (SOP)

透過 Windows 工作排程器 (Task Scheduler) 搭配 PowerShell 腳本，可輕鬆實現每日自動備份、日誌輪替與舊檔自動清理。

### 5.1 自動化備份腳本範例 (`backup_task.ps1`)

將以下腳本儲存至您的伺服器，例如 `C:\Scripts\PostgresBackup\backup_task.ps1`：

```powershell
<#
================================================================================
 程式名稱: backup_task.ps1
 說明: PostgreSQL 自動排程備份腳本 (支援 Retention 政策與日誌寫入)
 執行環境: PowerShell 5.1+ / PowerShell 7+
================================================================================
#>
param(
    [string]$HostName = "localhost",
    [int]$Port = 5432,
    [string]$Database = "my_database",
    [string]$Username = "postgres",
    [string]$Password = "YourPassword123!",
    [string]$BackupDir = "D:\DatabaseBackups\my_database",
    [string]$PgBinPath = "C:\Tools\pgsql\bin",
    [string]$CliPath = "C:\Tools\PostgresBackup\pgbackup.exe",
    [int]$RetentionDays = 7   # 備份保留天數 (超過自動清理)
)

$ErrorActionPreference = "Stop"

# 1. 確保輸出目錄與日誌目錄存在
if (-not (Test-Path $BackupDir)) {
    New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null
}
$LogDir = Join-Path $BackupDir "logs"
if (-not (Test-Path $LogDir)) {
    New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
}

$Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$LogFile = Join-Path $LogDir "backup_$Timestamp.log"

function Log-Message([string]$msg) {
    $formatted = "[$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))] $msg"
    Write-Output $formatted
    Add-Content -Path $LogFile -Value $formatted -Encoding utf8
}

Log-Message "=== 開始執行 PostgreSQL 自動排程備份 ==="
Log-Message "目標資料庫: $Database @ $HostName:$Port"

# 2. 呼叫 CLI 執行備份
try {
    & $CliPath backup `
        -H $HostName `
        -P $Port `
        -d $Database `
        -u $Username `
        -p $Password `
        -f custom `
        -m all `
        -o $BackupDir `
        --pg-bin-path $PgBinPath `
        --log $LogFile

    $ExitCode = $LASTEXITCODE
    if ($ExitCode -ne 0) {
        throw "CLI 回傳失敗代碼: $ExitCode"
    }
    Log-Message "備份作業成功完成！"
}
catch {
    Log-Message "[ERROR] 備份程序發生異常: $_"
    exit 1
}

# 3. 執行保留政策 (Retention Policy)：刪除超過 N 天的舊備份檔
Log-Message "開始檢查並清理超過 $RetentionDays 天之舊備份檔案..."
$CutoffDate = (Get-Date).AddDays(-$RetentionDays)
$OldFiles = Get-ChildItem -Path $BackupDir -Include "*.dump", "*.sql" -File | Where-Object { $_.LastWriteTime -lt $CutoffDate }

foreach ($file in $OldFiles) {
    Log-Message "清除過期備份檔: $($file.Name) (建立時間: $($file.LastWriteTime))"
    Remove-Item $file.FullName -Force
}

Log-Message "=== 排程作業圓滿結束 ==="
exit 0
```

---

### 5.2 註冊至 Windows 工作排程器 (Task Scheduler)

本專案已實機測試驗證，建議使用 Windows 內建 `schtasks` 工具以系統管理員身分一鍵建立：

#### 🚀 一鍵註冊每日凌晨 02:00 自動備份任務：
```powershell
$scriptPath = "C:\Scripts\PostgresBackup\backup_task.ps1"

# 註冊每日 02:00 執行之排程 (以 SYSTEM 帳戶於後台安靜執行)
schtasks /Create `
    /TN "PostgresBackup_Daily" `
    /TR "powershell.exe -ExecutionPolicy Bypass -File `"$scriptPath`"" `
    /SC DAILY `
    /ST 02:00 `
    /RU "SYSTEM" `
    /F
```

#### 參數說明：
- `/TN "PostgresBackup_Daily"`：排程任務名稱。
- `/TR "powershell.exe ..."`：要觸發的命令（加上 `-ExecutionPolicy Bypass` 防止腳本被執行策略阻擋）。
- `/SC DAILY /ST 02:00`：每日凌晨 2 點觸發。
- `/RU "SYSTEM"`：以本機最高系統權限於背景執行（無論是否有使用者登入 Windows 均會穩定觸發）。

---

### 5.3 驗證排程與日誌檢視

#### 1. 查詢排程狀態：
```powershell
schtasks /Query /TN "PostgresBackup_Daily" /FO LIST
```

#### 2. 手動觸發排程測試：
您可以隨時透過指令手動觸發測試，確認排程是否正常執行：
```powershell
schtasks /Run /TN "PostgresBackup_Daily"
```

#### 3. 檢查備份結果與日誌：
進入備份輸出目錄（例如 `D:\DatabaseBackups\my_database\logs`），開啟當日日誌檢視輸出：
```text
[2026-09-17 23:58:37] === 開始執行 PostgreSQL 自動排程備份 ===
[2026-09-17 23:58:37] 目標資料庫: my_database @ localhost:5432
[23:58:37] 啟動備份作業: 資料庫 'my_database'
[23:58:37] 格式: Custom, 模式: SchemaAndData, 範圍: FullDatabase
[23:58:40] [SUCCESS] 備份作業順利完成！
[2026-09-17 23:58:40] 備份作業成功完成！
[2026-09-17 23:58:40] 開始檢查並清理超過 7 天之舊備份檔案...
[2026-09-17 23:58:40] === 排程作業圓滿結束 ===
```

---

## 6. 常見問題排查 (FAQ)

### Q1: 出現 `pg_dump: error: server version: 18.6; pg_dump version: 16.x`
- **原因**：PostgreSQL 官方規範要求客戶端 `pg_dump` 主版本不得低於資料庫伺服器版本。
- **排除方式**：請依本手冊 [第 2 節](#2-postgresql-官方客戶端工具下載與安裝-sop) 下載 PostgreSQL 18.x 官方工具，並於設定中指定該 bin 目錄。

### Q2: 密碼中包含 `@`、`$` 等特殊字元是否會造成備份異常？
- **解答**：不會。本系統嚴格遵守安全規範，密碼不直接串接於命令列字串中，而是透過處理序環境變數 `PGPASSWORD` 傳遞，因此支援任何高強度複雜密碼。

### Q3: 還原作業時提示「請確認受影響之目標資料庫名稱」無法點擊？
- **原因**：此為破壞性覆寫防呆安全機制。
- **排除方式**：請於「確認受影響之目標資料庫名稱」輸入方塊中，輸入與上方「目標資料庫名稱」完全相同之字串，並勾選風險確認，按鈕即會解鎖為可點擊狀態。

### Q4: 如何還原至不同名稱的資料庫進行測試？
- **解答**：在還原頁面中，將「目標資料庫名稱」修改為測試庫名稱（例如 `my_database_test`），並勾選「強制建立安全快照」，即可安全還原至新資料庫進行驗證，不影響正式環境資料。
