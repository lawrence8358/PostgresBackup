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
   - [4.4 命令列連線設定管理 (`profile`)](#44-命令列連線設定管理-profile)
5. [CLI 自動化排程備份實戰指南 (SOP)](#5-cli-自動化排程備份實戰指南-sop)
   - [5.1 自動化備份腳本範例 (`backup_task.ps1`)](#51-自動化備份腳本範例-backup_taskps1)
   - [5.2 以 SYSTEM 身分註冊至 Windows 工作排程器 (Task Scheduler)](#52-以-system-身分註冊至-windows-工作排程器-task-scheduler)
   - [5.3 驗證排程與日誌檢視](#53-驗證排程與日誌檢視)
6. [安全性說明](#6-安全性說明)
7. [常見問題排查 (FAQ)](#7-常見問題排查-faq)

---

## 1. 系統簡介與特色

`PostgresBackup` 是一套專為企業與開發團隊打造的 PostgreSQL 資料庫備份與安全還原管理系統。不同於常見的第三方轉譯工具，本系統具有以下核心特色：

- **官方工具核心驅動**：底層完全呼叫 PostgreSQL 官方原生之 `pg_dump`、`pg_restore` 與 `psql`，杜絕因驅動轉譯產生的資料型態失真。
- **雙模式整合體驗**：
  - **WPF GUI**：具備翡翠綠現代化介面、多國語系（繁體中文 / 英文），自適應視窗縮放與響應式排版。
  - **CLI 命令列 (`pgbackup`)**：可直接整合至 Windows Task Scheduler、CI/CD 與自動化營運排程。
- **安全防護核心**：
  - **兩套獨立的連線設定**：介面（WPF）與命令列（CLI）各自擁有一套連線設定，彼此完全獨立。介面連線設定的密碼交由 Windows Credential Manager 保管；命令列連線設定（`pgbackup profile`）的密碼則以「機器範圍加密」保管，讓以 `SYSTEM` 身分執行的排程任務也能安全讀取，腳本中不需要出現任何密碼。詳見[第 6 節：安全性說明](#6-安全性說明)。
  - **`-p`／`--connection-string` 僅供手動除錯**：這兩個參數會讓密碼出現在 `pgbackup.exe` 自己的處理序命令列上，同機的其他使用者可透過工作管理員等工具讀取，因此只適合手動除錯，**不得**用於排程腳本；排程請一律改用 `--profile`。
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
| `--profile` | | 指定已儲存之命令列連線設定名稱或識別碼（見 [4.4 節](#44-命令列連線設定管理-profile)），提供連線資訊時會一併檢驗伺服器版本相容性 | `--profile "正式環境"` |
| `--pg-bin-path` | | 官方工具 bin 目錄路徑 | `--pg-bin-path "C:\Tools\pgsql\bin"` |
| `--host` | `-H` | PostgreSQL 主機位址（提供連線資訊時，會一併檢驗伺服器版本相容性） | `-H localhost` |
| `--port` | `-P` | 連接埠 (預設 5432) | `-P 5432` |
| `--database` | `-d` | 資料庫名稱 | `-d my_database` |
| `--username` | `-u` | 資料庫使用者 | `-u postgres` |
| `--password` | `-p` | 資料庫密碼。**僅供手動除錯使用**：密碼會出現在 `pgbackup.exe` 自己的處理序命令列上，同機的其他使用者可用工作管理員等工具讀取；使用時 CLI 會印出執行時警告。排程腳本請改用 `--profile` | `-p "手動除錯用密碼"` |
| `--connection-string` | `-s` | 完整連線字串，用於伺服器版本相容性檢查。**僅供互動式除錯使用，不得用於排程腳本**（同樣可能讓密碼暴露於處理序命令列或記錄中） | `-s "Host=localhost;Database=db;..."` |
| `--json` | | 以 JSON 格式輸出診斷報告，便於腳本化健康檢查 | `--json` |

明確指定的連線參數會覆寫 `--profile` 帶入的值。

#### 常用範例：
1. **僅檢查本機工具是否就緒**：
   ```powershell
   pgbackup check-tools --pg-bin-path "C:\Tools\pgsql\bin"
   ```
2. **使用命令列連線設定檢驗客戶端與伺服器版本相容性（推薦，腳本中不含密碼）**：
   ```powershell
   pgbackup check-tools --profile "正式環境" --pg-bin-path "C:\Tools\pgsql\bin"
   ```
3. **手動除錯時以 `-p` 直接帶入密碼（僅限互動操作，不建議寫入腳本）**：
   ```powershell
   pgbackup check-tools -H localhost -d my_database -u postgres -p "手動除錯用密碼" --pg-bin-path "C:\Tools\pgsql\bin"
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
| `--password` | `-p` | 資料庫密碼。**僅供手動除錯使用**：密碼會出現在 `pgbackup.exe` 自己的處理序命令列上，同機的其他使用者可用工作管理員等工具讀取；使用時 CLI 會印出執行時警告。**排程腳本一律改用 `--profile`**，腳本中不應出現任何密碼 | `-p "手動除錯用密碼"` |
| `--format` | `-f` | 備份格式 (`custom` 或 `plain`) | `-f custom` |
| `--mode` | `-m` | 模式 (`all`, `schema`, `data`) | `-m all` |
| `--schema` | `-n` | 指定綱要 (可重複使用) | `-n public -n hangfire` |
| `--table` | `-t` | 指定資料表 (可重複使用) | `-t "public.Quote"` |
| `--output-dir` | `-o` | 輸出存放目錄 | `-o "D:\Backups"` |
| `--output-file` | | 直接指定輸出檔案完整路徑，覆寫自動產生的檔名 | `--output-file "D:\Backups\nightly.dump"` |
| `--pg-bin-path` | | 官方工具 bin 目錄路徑 | `--pg-bin-path "C:\Tools\pgsql\bin"` |
| `--log` | | 額外輸出詳細日誌檔路徑 | `--log "D:\Backups\run.log"` |

若指定了不存在的 `--profile` 名稱，`backup` 會立即以非零結束代碼中止，不會略過連線資訊繼續嘗試執行。

#### 常用範例：
1. **使用命令列連線設定執行完整資料庫自訂二進位備份（推薦，排程請用此法）**：
   ```powershell
   pgbackup backup --profile "正式環境" -f custom -o "D:\Backups" --pg-bin-path "C:\Tools\pgsql\bin"
   ```
2. **手動除錯時以 `-p` 直接帶入密碼（僅限互動操作）**：
   ```powershell
   pgbackup backup -H localhost -P 5432 -d my_database -u postgres -p "手動除錯用密碼" -f custom -o "D:\Backups" --pg-bin-path "C:\Tools\pgsql\bin"
   ```
3. **僅傾印結構 (Schema-Only) 純文字 SQL 檔**：
   ```powershell
   pgbackup backup --profile "正式環境" -f plain -m schema -o "D:\Backups" --pg-bin-path "C:\Tools\pgsql\bin"
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
| `--password` | `-p` | 資料庫密碼。**僅供手動除錯使用**：密碼會出現在 `pgbackup.exe` 自己的處理序命令列上，同機的其他使用者可用工作管理員等工具讀取；使用時 CLI 會印出執行時警告。**排程腳本一律改用 `--profile`**，腳本中不應出現任何密碼 | `-p "手動除錯用密碼"` |
| `--mode` | `-m` | 還原模式 (`normal`, `clean`, `data`)，預設 `normal` | `-m clean` |
| `--no-snapshot` | | **關閉**還原前安全快照（預設為自動啟用） | `--no-snapshot` |
| `--yes` | `-y` | 自動同意高危險操作確認，不跳出互動提示 | `--yes` |
| `--pg-bin-path` | | 官方工具 bin 目錄路徑 | `--pg-bin-path "C:\Tools\pgsql\bin"` |
| `--log` | | 額外輸出詳細日誌檔路徑 | `--log "D:\Backups\restore.log"` |

> **注意**：還原前安全快照為**預設啟用**，無須額外加上任何參數；`--no-snapshot` 是用來「關閉」它的。關閉快照等同於放棄還原後的回復能力，除非目標資料庫可隨意丟棄，否則請勿使用。

若指定了不存在的 `--profile` 名稱，`restore` 會立即以非零結束代碼中止，不會靜默略過連線資訊繼續執行。

#### 還原模式說明：
| 模式 | 對應官方參數 | 說明 |
| :--- | :--- | :--- |
| `normal` | （無） | 一般還原，建立遺漏物件，不刪除既有物件 |
| `clean` | `--clean --create` | 清除並重建，覆寫既有物件 |
| `data` | `--data-only` | 僅寫入資料，不變更結構 |

#### 常用範例：
1. **使用命令列連線設定執行安全還原 (自動建立前置快照，並於覆寫前互動確認)**：
   ```powershell
   pgbackup restore -f "D:\Backups\my_database_20260917.dump" --profile "正式環境" --pg-bin-path "C:\Tools\pgsql\bin"
   ```
2. **無人值守還原 (排程腳本用，略過互動確認但仍保留安全快照，腳本中不含任何密碼)**：
   ```powershell
   pgbackup restore -f "D:\Backups\my_database_20260917.dump" --profile "正式環境" --yes --pg-bin-path "C:\Tools\pgsql\bin"
   ```
3. **清除並重建模式還原 (完整覆寫目標資料庫)**：
   ```powershell
   pgbackup restore -f "D:\Backups\my_database_20260917.dump" --profile "正式環境" -m clean --yes --pg-bin-path "C:\Tools\pgsql\bin"
   ```
4. **手動除錯時以 `-p` 直接帶入密碼（僅限互動操作）**：
   ```powershell
   pgbackup restore -f "D:\Backups\my_database_20260917.dump" -H localhost -d my_database -u postgres -p "手動除錯用密碼" --pg-bin-path "C:\Tools\pgsql\bin"
   ```

### 4.4 命令列連線設定管理 (`profile`)

`profile` 指令群組用來建立、列出與刪除**命令列連線設定**（供 `--profile` 使用）。這套設定與 WPF 介面「設定」分頁中建立的**介面連線設定**完全獨立、互不相通——在介面建立的設定不會出現在 `profile list` 中，反之亦然。命令列連線設定的密碼採用「機器範圍加密」保管，因此以 `SYSTEM` 身分執行的排程任務也能讀取；完整說明見[第 6 節：安全性說明](#6-安全性說明)。

```powershell
pgbackup profile [set|list|remove] [選項]
```

#### `profile set`：建立或更新一筆命令列連線設定

| 參數 | 簡寫 | 說明 | 範例 |
| :--- | :---: | :--- | :--- |
| `--name` | | **必填。** 連線設定名稱；使用既有名稱即為更新該設定 | `--name "正式環境"` |
| `--host` | `-H` | PostgreSQL 伺服器主機位址（預設 `localhost`） | `-H db.internal` |
| `--port` | `-P` | PostgreSQL 伺服器連接埠（預設 `5432`） | `-P 5432` |
| `--database` | `-d` | 資料庫名稱 | `-d my_database` |
| `--username` | `-u` | 資料庫使用者名稱 | `-u postgres` |
| `--password-stdin` | | 自標準輸入以管線方式提供密碼，供自動化部署使用；本身不接受任何密碼值 | `--password-stdin` |
| `--force` | | 略過存檔前的連線驗證，直接儲存；僅在資料庫暫時無法連線時使用，儲存後會印出明顯警告 | `--force` |

**刻意不提供**以命令列參數直接傳入密碼的選項——那會在修正密碼暴露問題的同時，重新製造同一個問題。密碼一律以下列兩種方式之一取得：

- **互動式遮蔽輸入**（預設）：執行 `pgbackup profile set --name ...` 後，於畫面提示時輸入密碼，畫面上不會顯示任何字元。
- **標準輸入管線**（`--password-stdin`）：供自動化部署腳本使用，密碼不會出現在 `pgbackup.exe` 自己的命令列上。
  > **注意：** 這只保證密碼不會出現在 `pgbackup` 的命令列。密碼是從管線的**左邊**來的，所以左邊怎麼寫同樣要緊——如果你直接把密碼打成字面值（例如 `"我的密碼" | pgbackup ...`），它一樣會進入 PowerShell 的操作歷史紀錄與 `powershell.exe` 自己的命令列。請改從檔案或部署系統的機密注入取得，如下方範例。

儲存前，系統會先以輸入的帳密**實際連線驗證一次**；驗證失敗即拒絕儲存，並提示可能的原因。只有在資料庫當下確實無法連線（例如尚未開通防火牆、伺服器維護中）時，才加上 `--force` 略過驗證先行建立設定——加上 `--force` 儲存後畫面會印出黃色警告，提醒這份設定尚未被證實可用，待資料庫恢復連線後應重新執行一次不加 `--force` 的指令確認。

範例：

```powershell
# 以系統管理員身分開啟終端機，互動輸入密碼建立一筆設定
pgbackup profile set --name "正式環境" -H localhost -P 5432 -d my_database -u postgres

# 自動化部署：自權限受控的機密檔案讀入密碼，再以管線提供
Get-Content -Raw "C:\ProgramData\deploy-secrets\db.secret" | `
    pgbackup profile set --name "正式環境" -H localhost -d my_database -u postgres --password-stdin

# 或由部署系統（CI／組態管理工具）注入的環境變數提供
$env:DB_PASSWORD | pgbackup profile set --name "正式環境" -H localhost -d my_database -u postgres --password-stdin

# 切勿這樣寫：密碼字面值會進入 PowerShell 歷史紀錄與處理序命令列
# "MyS3cretPassword" | pgbackup profile set --name "正式環境" ... --password-stdin

# 資料庫暫時無法連線時，先略過驗證建立設定
pgbackup profile set --name "正式環境" -H localhost -d my_database -u postgres --force
```

#### `profile list`：列出目前所有命令列連線設定

不需任何參數。輸出每筆設定的名稱、主機與連接埠、資料庫、使用者名稱、來源標示、建立時間，以及**密碼狀態**（「已設定」或「遺失」）——**只呈現有無，絕不呈現密碼內容或長度**。同時會檢查存放區的檔案權限是否仍限定於系統管理員與 `SYSTEM`；若權限被放寬，會印出警告。

```powershell
pgbackup profile list
```

#### `profile remove`：刪除一筆命令列連線設定

| 參數 | 簡寫 | 說明 | 範例 |
| :--- | :---: | :--- | :--- |
| `--name` | | **必填。** 要刪除的命令列連線設定名稱 | `--name "正式環境"` |

刪除時會一併清除其加密密碼，不留下孤兒憑證。

```powershell
pgbackup profile remove --name "正式環境"
```

---

## 5. CLI 自動化排程備份實戰指南 (SOP)

透過 Windows 工作排程器 (Task Scheduler) 搭配 PowerShell 腳本，可輕鬆實現每日自動備份、日誌輪替與舊檔自動清理——且腳本檔案中**完全不含任何資料庫密碼**，可以安全地放在共用位置，甚至簽入版控。

在寫腳本之前，先以系統管理員身分執行一次（僅需一次，換密碼時重跑即可）：

```powershell
pgbackup profile set --name "正式環境" -H localhost -d my_database -u postgres
```

系統會提示遮蔽輸入密碼，並先實際連線驗證一次才儲存。之後排程腳本只需要寫 `--profile "正式環境"`。

### 5.1 自動化備份腳本範例 (`backup_task.ps1`)

將以下腳本儲存至您的伺服器，例如 `C:\Scripts\PostgresBackup\backup_task.ps1`：

```powershell
<#
================================================================================
 程式名稱: backup_task.ps1
 說明: PostgreSQL 自動排程備份腳本 (支援 Retention 政策與日誌寫入)
 執行環境: PowerShell 5.1+ / PowerShell 7+
 注意: 本腳本不含任何資料庫密碼。密碼由 `pgbackup profile set` 事先以
       機器範圍加密存放，此腳本只透過 --profile 參照該筆設定。
================================================================================
#>
param(
    [string]$ProfileName = "正式環境",
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
Log-Message "使用命令列連線設定: $ProfileName"

# 2. 呼叫 CLI 執行備份 — 腳本中不含任何密碼，密碼取自 --profile 所指的
#    命令列連線設定（機器範圍加密存放區），SYSTEM 身分即可讀取
try {
    & $CliPath backup `
        --profile $ProfileName `
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

### 5.2 以 SYSTEM 身分註冊至 Windows 工作排程器 (Task Scheduler)

#### 為什麼一定要以 `SYSTEM` 身分執行，不能用個人帳號？

命令列連線設定是以「機器範圍加密」保管密碼，任何在**同一台機器上**執行的處理序都能解開它，不限於某個特定使用者帳號——`SYSTEM` 正是符合這個條件、且不需要密碼登入即可執行排程的內建身分。改用個人帳號執行排程，會多出兩個實際會發生的問題：

- **密碼到期會讓任務「靜默停止」。** 多數組織的系統管理員帳號依規定每三個月要強制更換 Windows 登入密碼。工作排程器若設定以個人帳號執行，一旦該帳號密碼到期或更換，排程任務會開始失敗——但工作排程器本身通常不會用明顯的方式通知你，你很可能要等到「發現最近幾天都沒有新備份」才察覺，已經斷了一段時間。
- **需要額外維護「儲存的認證」。** 以個人帳號執行的排程任務通常要求在工作排程器中儲存該帳號的登入密碼，這是另一份需要保護與更新的憑證，換密碼時還得同步更新兩個地方（Windows 登入密碼與工作排程器裡儲存的密碼）。

以 `SYSTEM` 身分執行則完全沒有這兩個問題：`SYSTEM` 沒有「密碼」需要到期或更換，工作排程器也不需要儲存任何帳號密碼。這是本工具在使用者無法建立專用服務帳號的環境下，唯一不會隨時間推移而悄悄失效的做法。

#### 🚀 一鍵註冊每日凌晨 02:00 自動備份任務：

以**系統管理員身分**開啟 PowerShell 或命令提示字元，執行：

```powershell
$scriptPath = "C:\Scripts\PostgresBackup\backup_task.ps1"

# 註冊每日 02:00 執行之排程 (以 SYSTEM 帳戶於後台安靜執行，無須任何人登入 Windows)
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
- `/RU "SYSTEM"`：以 `SYSTEM` 身分於背景執行——不需要密碼、不會到期，無論是否有使用者登入 Windows 均會穩定觸發，也才能讀取以機器範圍加密存放的命令列連線設定。

> **務必先以系統管理員身分執行過 `pgbackup profile set` 建立好連線設定，才註冊排程。** `SYSTEM` 讀不到 WPF 介面建立的介面連線設定——那套設定存放在使用者帳號範圍內。命令列連線設定存放於機器層級，任何在這台機器上以系統管理員權限執行 `pgbackup profile list` 的人都能看到同一份清單；註冊排程前，先確認清單中這筆設定的密碼狀態為「已設定」。

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

> **排程備份不會出現在圖形介面的「備份歷史」頁面。** 稽核歷史存放於 `%LOCALAPPDATA%\PostgresBackup\history.db`，而 `%LOCALAPPDATA%` 會隨 Windows 帳號解析到不同位置：以 `SYSTEM` 身分執行的排程任務，其歷史寫在 `C:\Windows\System32\config\systemprofile\AppData\Local\PostgresBackup\history.db`，圖形介面讀的則是你目前登入帳號的那一份。備份檔案本身不受影響，只有稽核紀錄落在別處。排程執行的結果請以下方的日誌檔與命令列退出碼來確認。

#### 3. 檢查備份結果與日誌：
進入備份輸出目錄（例如 `D:\DatabaseBackups\my_database\logs`），開啟當日日誌檢視輸出：
```text
[2026-09-17 23:58:37] === 開始執行 PostgreSQL 自動排程備份 ===
[2026-09-17 23:58:37] 使用命令列連線設定: 正式環境
[23:58:37] 啟動備份作業: 資料庫 'my_database'
[23:58:37] 格式: Custom, 模式: SchemaAndData, 範圍: FullDatabase
[23:58:40] [SUCCESS] 備份作業順利完成！
[2026-09-17 23:58:40] 備份作業成功完成！
[2026-09-17 23:58:40] 開始檢查並清理超過 7 天之舊備份檔案...
[2026-09-17 23:58:40] === 排程作業圓滿結束 ===
```

---

## 6. 安全性說明

這一節用一般人能懂的話，說清楚密碼放在哪裡、誰能看到、加密到底防住了什麼、又防不住什麼。如果你是資安稽核人員，或只是想在把這套工具用在正式環境前搞清楚風險，請讀完這一節。

### 這套工具有「兩套」連線設定，彼此互不相通

- **介面連線設定**：在 WPF 桌面應用程式的「設定」分頁裡建立的連線設定，只有這個桌面程式自己會用到。
- **命令列連線設定**：用 `pgbackup profile set` 建立的連線設定，只有 `pgbackup.exe` 這個命令列工具、以及排程任務會用到。

這兩套設定是刻意分開的：你在介面裡建立的設定，不會出現在 `pgbackup profile list` 裡；反過來也一樣。如果你的排程失敗，第一件要檢查的事就是「我是不是只在介面裡建立過設定，卻沒有用 `pgbackup profile set` 另外建一份」。

### 密碼存放在哪裡、誰讀得到

| | 介面連線設定 | 命令列連線設定 |
|---|---|---|
| 存放位置 | 你自己 Windows 帳號底下的資料夾與 Windows 認證管理員（Credential Manager） | 這台電腦共用的系統資料夾（不屬於任何一個使用者帳號） |
| 誰讀得到 | 只有「你」這個 Windows 帳號登入後，這個桌面程式能讀到 | 這台電腦上以系統管理員身分執行的人，以及 `SYSTEM`（Windows 排程任務常用的內建身分） |
| 適合用在 | 你自己手動操作、平常用滑鼠點的場景 | 不需要有人登入也要天天自動執行的排程備份 |

之所以要分成兩套，是因為排程任務通常是以 `SYSTEM` 這個身分在背景執行的，而 `SYSTEM` 沒有辦法去讀某個特定使用者帳號底下的資料。所以命令列連線設定必須換一個「屬於整台電腦，而不是屬於某個人」的地方存放，`SYSTEM` 才讀得到。

### 加密實際防住了什麼

命令列連線設定裡的密碼，會先加密再存到磁碟上的檔案裡（檔名是 `cli-credentials.dat`，跟連線的其他資訊如主機、資料庫名稱分開存放）。這個加密有兩個特性：

1. **加密結果跟這台電腦綁定。** 就算有人把這個檔案整份複製到另一台電腦上，在那台電腦上也**打不開**——加密與解密用的鑰匙不是存在檔案裡，而是跟這台電腦本身綁在一起。所以檔案被複製走、被備份工具意外收進雲端硬碟，並不等於密碼外洩。
2. **這台電腦上的一般使用者打不開它。** 存放這份檔案的資料夾，建立時就設定成只有「系統管理員群組」與 `SYSTEM` 才有讀取權限，同一台電腦上的其他一般使用者帳號連檔案都打不開，更別說解密。

### 存放區資料夾若已存在且權限不符，工具會拒絕儲存

上面第 2 點的保護，完全建立在「存放資料夾的權限確實限定於系統管理員與 `SYSTEM`」這件事上。而 Windows 的機器層級共用資料夾預設允許一般使用者建立子資料夾——也就是說，一般使用者有可能**搶先建立**這個存放資料夾，讓它保留寬鬆的繼承權限，等系統管理員之後存入密碼時，密碼就落在一個同機任何人都讀得到的位置。

因此 `pgbackup profile set` 在寫入任何資料**之前**會先檢查存放資料夾：

- **資料夾還不存在**：由本工具建立，建立當下即套用限制性權限，然後才寫入。
- **資料夾已存在且權限正確**：直接寫入。
- **資料夾已存在但權限不符，或讀不到權限**：**拒絕儲存**，這次不會寫入任何資料。

第三種情況之所以拒絕而不是自動把權限改回來，是因為在 Windows 上資料夾的建立者始終保有變更權限的能力：如果這個資料夾是被別人搶先建立的，我們就算把權限收緊，對方仍然可以事後再改回去——收緊之後就當作安全，是一個假的保證。

遇到這種情況時，請以系統管理員身分確認該資料夾的來歷。若其中沒有你需要保留的連線設定，最乾淨的做法是直接刪除整個 `%ProgramData%\PostgresBackup` 資料夾，再重新執行一次 `pgbackup profile set`，由本工具重新建立。

### 明確不防護的對象：系統管理員

**任何人只要在這台電腦上取得系統管理員權限，就能解開命令列連線設定裡的密碼。** 這不是我們沒注意到的漏洞，而是刻意畫下的界線，理由很直接：在 Windows 上，不管用什麼方式把密碼「安全地」存在本機（不管是這裡用的加密方式，還是 Windows 認證管理員），系統管理員本來就有辦法解開——這是 Windows 本身的設計，不是這個工具能改變的事。真正能防住系統管理員的做法，需要額外的專用加密硬體或外部的雲端保管服務，這已經超出一套備份工具該負責的範圍，也不是大多數使用情境會需要的等級。

換句話說：這套機制防的是「同一台電腦上，沒有系統管理員權限的其他人」，防不住「這台電腦的系統管理員」。如果你的威脅模型是要防住連你自己公司的系統管理員都不能信任的情境，這個工具不是為那種情境設計的。

### `-p` 與 `--connection-string`：只適合手動除錯，不要寫進排程腳本

CLI 的 `-p`（密碼）與 `-s` / `--connection-string`（完整連線字串）這兩個參數，會讓密碼出現在 `pgbackup.exe` 這個程式自己的啟動指令上。在同一台電腦上，任何人打開「工作管理員」看處理序詳細資料，或用 PowerShell 執行 `Get-CimInstance Win32_Process`，都能看到這個啟動指令、連同裡面的密碼。密碼也會留在 PowerShell 的操作歷史紀錄檔裡。

這兩個參數之所以還留著，是因為手動在終端機打指令除錯時確實方便，而且拿掉它們算是破壞性變更，本身也是有正當用途的。CLI 使用 `-p` 時會印出一行執行時警告提醒這個風險。但**排程腳本永遠不應該使用這兩個參數**——排程一律改用 `--profile`，指向一筆用 `pgbackup profile set` 建立好的命令列連線設定，腳本檔案裡就完全不會出現密碼。

### 一句話總結

介面連線設定跟命令列連線設定各自存放、互不相通；命令列連線設定的密碼用跟這台電腦綁定的方式加密，並由存放資料夾的權限擋住同機的一般使用者（權限不符時工具寧可拒絕儲存也不會硬寫），但同機的系統管理員看得到——這一點是刻意接受的風險，不是疏漏；`-p` 跟 `--connection-string` 這兩個參數本身就會讓密碼露在外面，只能用來手動除錯，排程請一律用 `--profile`。

---

## 7. 常見問題排查 (FAQ)

### Q1: 出現 `pg_dump: error: server version: 18.6; pg_dump version: 16.x`
- **原因**：PostgreSQL 官方規範要求客戶端 `pg_dump` 主版本不得低於資料庫伺服器版本。
- **排除方式**：請依本手冊 [第 2 節](#2-postgresql-官方客戶端工具下載與安裝-sop) 下載 PostgreSQL 18.x 官方工具，並於設定中指定該 bin 目錄。

### Q2: 密碼中包含 `@`、`$` 等特殊字元是否會造成備份異常？
- **解答**：不會，密碼本身支援任何高強度複雜字元。但兩種傳遞方式的暴露面不同，請分清楚：
  - 使用 `--profile` 時，密碼取自命令列連線設定的加密存放區，完全不會出現在任何命令列或腳本裡，是排程應該用的方式。
  - 使用 `-p` 直接在命令列帶入密碼時，密碼確實**會**出現在 `pgbackup.exe` 自己的啟動指令上（例如可被工作管理員或 `Get-CimInstance Win32_Process` 讀到）；只是接下來 PostgresBackup 呼叫 `pg_dump` / `pg_restore` 這些官方工具時，密碼不會再被串接進「它們的」命令列參數，而是透過子處理序的環境變數 `PGPASSWORD` 傳遞。也就是說 `-p` 對 `pgbackup.exe` 本身仍是暴露的，只是不會再往下一層擴散——這正是 `-p` 只適合手動除錯、不適合排程腳本的原因，詳見[第 6 節：安全性說明](#6-安全性說明)。

### Q3: 還原作業時提示「請確認受影響之目標資料庫名稱」無法點擊？
- **原因**：此為破壞性覆寫防呆安全機制。
- **排除方式**：請於「確認受影響之目標資料庫名稱」輸入方塊中，輸入與上方「目標資料庫名稱」完全相同之字串，並勾選風險確認，按鈕即會解鎖為可點擊狀態。

### Q4: 如何還原至不同名稱的資料庫進行測試？
- **解答**：在還原頁面中，將「目標資料庫名稱」修改為測試庫名稱（例如 `my_database_test`），並勾選「強制建立安全快照」，即可安全還原至新資料庫進行驗證，不影響正式環境資料。
