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
4. [CLI 命令列工具操作指南](#4-cli-命令列工具操作指南)
   - [4.1 工具狀態診斷 (`check-tools`)](#41-工具狀態診斷-check-tools)
   - [4.2 備份作業 (`backup`)](#42-備份作業-backup)
   - [4.3 還原作業 (`restore`)](#43-還原作業-restore)
   - [4.4 命令列連線設定管理 (`profile`)](#44-命令列連線設定管理-profile)
5. [CLI 自動化排程備份實戰指南 (SOP)](#5-cli-自動化排程備份實戰指南-sop)
   - [5.1 現成腳本的位置與用途](#51-現成腳本的位置與用途)
   - [5.2 以 SYSTEM 身分註冊至 Windows 工作排程器 (Task Scheduler)](#52-以-system-身分註冊至-windows-工作排程器-task-scheduler)
   - [5.3 驗證排程與日誌檢視](#53-驗證排程與日誌檢視)
   - [5.4 檔案與路徑一覽](#54-檔案與路徑一覽)
   - [5.5 移除排程備份](#55-移除排程備份)
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
7. 點擊「**開始執行備份**」，備份頁面的終端會即時顯示 `pg_dump` 輸出串流。

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

> **備份失敗時，輸出目錄會留下一個 0 位元組的空檔案。** 這是 `pg_dump` 的行為——它會先把輸出檔建起來，再去連資料庫；連線失敗（密碼錯誤、資料庫不存在等）時，那個空檔案就留在原地。工具會以非零結束代碼與錯誤訊息明確告知失敗，**請以結束代碼判斷成敗，不要以「目錄裡有沒有檔案」判斷**。排程腳本可在偵測到非零結束代碼後，順手清掉當次那個 0 位元組的檔案。

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

#### 前置快照存放在哪裡？

前置快照會寫到**來源備份檔所在目錄底下的 `snapshots` 子目錄**，檔名為 `{資料庫名稱}_snapshot_{yyyyMMddHHmmss}.dump`。例如以 `-f "D:\Backups\my_database_20260917.dump"` 還原時，快照會產生在：

```text
D:\Backups\snapshots\my_database_snapshot_20260920210217.dump
```

還原成功後畫面會印出快照的完整路徑。CLI 目前沒有參數可以改這個位置，因此請確認**備份檔所在的磁碟，還有足夠空間再放一份目標資料庫的完整備份**；若目標資料庫很大、而來源備份檔放在空間吃緊的磁碟上，請先把備份檔搬到空間充足的位置再還原。這些快照不會自動清除，請一併納入你的保留政策清理。

#### 沒加 `-y` 時，在排程等非互動環境會「安全取消」

未加 `-y` / `--yes` 時，`restore` 會要求你**手動輸入一次目標資料庫名稱**才肯往下走：

```text
[⚠️ 高危險操作警告]
您即將對資料庫 'my_database' 執行還原作業，既有資料可能被覆蓋或刪除！
若確定執行，請輸入目標資料庫名稱 'my_database':
```

在工作排程器、CI 這類沒有人在鍵盤前面的環境下，這個提示讀不到任何輸入，工具會印出「輸入不符，還原作業已安全取消」並以**非零結束代碼**結束，**完全不會動到資料庫**。這是刻意的防呆：無人值守的還原腳本必須明確加上 `-y`，等於要你親手寫下「我知道這會覆寫資料」。

#### 還原模式說明：
| 模式 | 對應官方參數 | 說明 |
| :--- | :--- | :--- |
| `normal` | `--list`、`--use-list`、`--no-data-for-failed-tables` | 先比對 Custom 備份目錄與目標資料庫，只還原遺漏物件及其資料；既有物件與既有資料不會重新寫入 |
| `clean` | `--clean --if-exists` | 清除並重建目標資料庫中的物件；不改用備份檔內的資料庫名稱 |
| `data` | `--list`、`TRUNCATE ... RESTART IDENTITY`、`--data-only --use-list --exit-on-error` | 保留既有結構，先清空備份涵蓋的資料表，再依目標外鍵關係以父表優先順序完整寫回；不使用 `CASCADE`，若範圍外資料表有外鍵依賴或範圍內形成循環外鍵則安全停止 |

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

> **命令列連線設定只記住「連哪一台資料庫、用什麼帳號密碼」，不記錄 `pg_dump` 放在哪裡。** 所以排程腳本除了 `--profile`，通常還是要帶上 `--pg-bin-path`——除非官方客戶端工具已經裝進系統 PATH（[第 2 節](#2-postgresql-官方客戶端工具下載與安裝-sop)的方法 B）。若你用的是免安裝可攜版（方法 A），`--pg-bin-path` 就是必要的，漏掉會得到「未偵測到 PostgreSQL 客戶端工具 (pg_dump)」而備份失敗。

> **工具目錄與 `pgbackup.exe` 都要放在 `SYSTEM` 讀得到的地方。** 排程任務以 `SYSTEM` 身分執行，它讀不到你個人帳號底下的資料夾（桌面、下載、`C:\Users\你的帳號\...`）。請把 `pgbackup.exe` 與 `pgsql\bin` 放在 `C:\Tools\`、`C:\Program Files\` 或某個資料碟的共用目錄下。

### 5.1 現成腳本的位置與用途

**你不需要自己寫腳本，也不需要從這份文件複製貼上。** 專案的 `scripts\` 目錄裡已經放好四支
可以直接使用的 PowerShell 腳本：

| 檔案 | 用途 | 誰執行、何時執行 | 需要管理員權限？ |
| :--- | :--- | :--- | :---: |
| `scripts\register-backup-task.ps1` | **一次性設定。** 建立連線設定 → 註冊 SYSTEM 排程 → 立即試跑驗證 | 你，只跑一次 | **是** |
| `scripts\backup_task.ps1` | **每天實際執行的備份。** 備份、判斷成敗、依保留天數清理 | Windows 工作排程器，以 `SYSTEM` 身分自動執行 | 由排程處理 |
| `scripts\check-backup-status.ps1` | **查看排程跑得好不好。** 排程狀態、上次退出碼、最近備份、日誌、磁碟空間 | 你，隨時 | **否** |
| `scripts\unregister-backup-task.ps1` | **移除排程。** 預設只移除排程任務，備份檔與連線設定一律保留 | 你，要收掉的時候 | 是（`-DryRun` 不用） |

四支腳本都**不含任何資料庫密碼**，可以安全地放在共用位置，也可以簽入版控。密碼由
`pgbackup profile set` 事先以機器範圍加密存放，腳本只用 `--profile` 參照那筆設定。
各腳本的完整參數表見 [`scripts/README.md`](../scripts/README.md)。

#### 一般情況下你只需要做這兩件事

```powershell
# 1. 設定（以系統管理員身分，只做一次）
cd <專案目錄>\scripts
.\register-backup-task.ps1

# 2. 日後檢查（一般身分即可，隨時）
.\check-backup-status.ps1 -BackupDir "D:\DatabaseBackups\my_database"
```

第一支會逐項詢問（主機、資料庫、帳號、備份目錄…），密碼是遮蔽輸入，畫面上不會顯示任何字元。
跑完會印出一份「這些東西放在哪裡」的總結，並立刻試跑一次確認整條路真的通。

也可以把答案都用參數帶進去省掉互動：

```powershell
.\register-backup-task.ps1 `
    -ProfileName "正式環境" `
    -Database my_database -Username postgres `
    -BackupDir "D:\DatabaseBackups\my_database" `
    -PgBinPath "C:\Tools\pgsql\bin" `
    -CliPath "C:\Tools\PostgresBackup\pgbackup.exe" `
    -At 02:00 -RetentionDays 7 -SnapshotRetentionDays 30
```

**只有密碼永遠不能用參數傳**——那等於把密碼寫進 PowerShell 的操作歷史紀錄。

#### `backup_task.ps1` 的保留政策

三類檔案分開設定，因為它們的性質不同：

| 參數 | 預設 | 管的是什麼 |
| :--- | :---: | :--- |
| `-RetentionDays` | `7` | 例行備份檔 |
| `-SnapshotRetentionDays` | `30` | `snapshots\` 裡的還原前快照。留得比備份久，因為那是「還原前的救命繩」 |
| `-LogRetentionDays` | `30` | `logs\` 裡的執行日誌 |

任一項設為 `0` 表示該類不清理；加上 `-SkipCleanup` 則三類都不清理。

**退出碼**：`0` 成功 ／ `1` 備份失敗 ／ `2` 設定或環境有問題（備份根本沒開始）。

> **如果你要自己寫保留政策，請避開這個坑。** 網路上常見的寫法
> `Get-ChildItem -Path $BackupDir -Include "*.dump","*.sql" -File` 在**沒有 `-Recurse`、
> 且路徑結尾沒有 `\*`** 的情況下，`-Include` 會被**完全忽略**——一個檔案都不會刪，卻也不會
> 報任何錯誤。結果是備份目錄無聲無息地一直長大，直到某天磁碟滿了、備份開始失敗才發現。
> `backup_task.ps1` 改用「取回全部檔案再以副檔名過濾」，行為明確，不會踩到這個坑。

> **另一個常見誤判：用「目錄裡有沒有檔案」判斷備份成功。** 備份失敗時 `pg_dump` 仍會留下一個
> 0 位元組的空檔（它先建立輸出檔，才去連資料庫）。**請一律以退出碼判斷成敗。**
> `backup_task.ps1` 會在失敗時清掉**這一輪**產生的空檔，且只清這一輪的，不會誤刪既有檔案。

### 5.2 以 SYSTEM 身分註冊至 Windows 工作排程器 (Task Scheduler)

#### 為什麼一定要以 `SYSTEM` 身分執行，不能用個人帳號？

命令列連線設定是以「機器範圍加密」保管密碼，任何在**同一台機器上**執行的處理序都能解開它，不限於某個特定使用者帳號——`SYSTEM` 正是符合這個條件、且不需要密碼登入即可執行排程的內建身分。改用個人帳號執行排程，會多出兩個實際會發生的問題：

- **密碼到期會讓任務「靜默停止」。** 多數組織的系統管理員帳號依規定每三個月要強制更換 Windows 登入密碼。工作排程器若設定以個人帳號執行，一旦該帳號密碼到期或更換，排程任務會開始失敗——但工作排程器本身通常不會用明顯的方式通知你，你很可能要等到「發現最近幾天都沒有新備份」才察覺，已經斷了一段時間。
- **需要額外維護「儲存的認證」。** 以個人帳號執行的排程任務通常要求在工作排程器中儲存該帳號的登入密碼，這是另一份需要保護與更新的憑證，換密碼時還得同步更新兩個地方（Windows 登入密碼與工作排程器裡儲存的密碼）。

以 `SYSTEM` 身分執行則完全沒有這兩個問題：`SYSTEM` 沒有「密碼」需要到期或更換，工作排程器也不需要儲存任何帳號密碼。這是本工具在使用者無法建立專用服務帳號的環境下，唯一不會隨時間推移而悄悄失效的做法。

#### 方法 A：用現成腳本註冊（建議）

以**系統管理員身分**開啟 PowerShell，執行 [5.1 節](#51-現成腳本的位置與用途)介紹的設定腳本：

```powershell
cd <專案目錄>\scripts
.\register-backup-task.ps1
```

它會把連線設定、排程註冊與一次試跑一併完成，並在過程中擋掉幾個實際會出事的情況：
執行身分不是系統管理員、`pgbackup.exe` 或備份腳本被放在 `SYSTEM` 讀不到的個人資料夾底下、
客戶端工具沒就緒、備份目錄沒開放給你平常的身分讀取。

#### 方法 B：手動註冊

想自己掌控每個步驟，或要把註冊動作納入既有的部署流程時使用：

```powershell
$scriptPath = "C:\Scripts\PostgresBackup\backup_task.ps1"
$cliPath    = "C:\Tools\PostgresBackup\pgbackup.exe"
$backupDir  = "D:\DatabaseBackups\my_database"
$pgBinPath  = "C:\Tools\pgsql\bin"

# 路徑含空白時會被拆成兩個參數，所以每個值都要自己帶上引號
$argument = @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass'
    '-File',         "`"$scriptPath`""
    '-ProfileName',  '"正式環境"'
    '-BackupDir',    "`"$backupDir`""
    '-CliPath',      "`"$cliPath`""
    '-PgBinPath',    "`"$pgBinPath`""
) -join ' '

$action    = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $argument
$trigger   = New-ScheduledTaskTrigger -Daily -At 02:00
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings  = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew

Register-ScheduledTask -TaskName "PostgresBackup_Daily" `
    -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force
```

> **這裡刻意用 `Register-ScheduledTask` 而不是 `schtasks /Create`。** `schtasks` 的 `/TR`
> 命令字串有長度上限（約 261 字元），路徑一長就會被**無聲截斷**，排程註冊看起來成功、實際上
> 執行的是一段殘缺的指令。`Register-ScheduledTask` 沒有這個限制。
>
> 若你的環境只能用 `schtasks`，請在註冊後務必用
> `(Get-ScheduledTask -TaskName "PostgresBackup_Daily").Actions` 確認參數沒有被截掉。

#### 參數說明：
- `-TaskName "PostgresBackup_Daily"`：排程任務名稱。
- `-Execute 'powershell.exe' -Argument ...`：要觸發的命令。`-ExecutionPolicy Bypass` 防止腳本被執行策略阻擋，`-NoProfile` 避免載入設定檔拖慢啟動或引入非預期的環境。
- `-Daily -At 02:00`：每日凌晨 2 點觸發。
- `-UserId 'SYSTEM' -LogonType ServiceAccount`：以 `SYSTEM` 身分於背景執行——不需要密碼、不會到期，無論是否有使用者登入 Windows 均會穩定觸發，也才能讀取以機器範圍加密存放的命令列連線設定。
- `-RunLevel Highest`：以最高權限層級執行。
- `-StartWhenAvailable`：機器在排定時間是關機或休眠的話，開機後補跑一次，不會整天沒有備份。

> **務必先以系統管理員身分執行過 `pgbackup profile set` 建立好連線設定，才註冊排程。** `SYSTEM` 讀不到 WPF 介面建立的介面連線設定——那套設定存放在使用者帳號範圍內。命令列連線設定存放於機器層級，任何在這台機器上以系統管理員權限執行 `pgbackup profile list` 的人都能看到同一份清單；註冊排程前，先確認清單中這筆設定的密碼狀態為「已設定」。

---

### 5.3 驗證排程與日誌檢視

> **檢查排程結果不需要系統管理員權限。** 需要提權的只有「讀取連線設定存放區」那一件事
> （`pgbackup profile list`，看的是主機／帳號／密碼有沒有設）。備份檔、執行日誌、排程的上次
> 執行時間與退出碼，一般身分都看得到。

#### 1. 一次看完所有狀態（建議）

```powershell
.\check-backup-status.ps1 -BackupDir "D:\DatabaseBackups\my_database"
```

它會報告四件事：排程任務還在不在、下次何時跑、上次的退出碼代表什麼；最近產出了哪些備份檔、
多大、多久以前；最新一次執行日誌的結尾；以及備份目錄所在磁碟還剩多少空間。

一切正常時退出碼為 `0`，有需要注意的事則為 `1`，可以直接接到監控系統。它也會主動抓出幾個
容易被忽略的狀況：最新備份已經超過門檻時數沒更新（排程可能早就停了）、備份檔是 0 位元組
（失敗的殘骸）、任務被停用、磁碟空間不足。

#### 2. 直接查詢排程狀態

```powershell
Get-ScheduledTask -TaskName "PostgresBackup_Daily"
Get-ScheduledTaskInfo -TaskName "PostgresBackup_Daily" |
    Select-Object LastRunTime, LastTaskResult, NextRunTime
```

> **請用上面這兩個指令，不要去剖析 `schtasks /Query /FO LIST /V` 的文字輸出。** 那份輸出的
> 欄位標籤會隨系統語系變化，值何時被寫入也不保證——本專案的驗證腳本就曾因此讀到空值，
> 把一次成功的備份誤判成失敗。`Get-ScheduledTaskInfo` 回傳的 `LastTaskResult` 是真正的數值。
>
> 常見的 `LastTaskResult`：`0` 成功、`1` 備份失敗、`2` 設定或環境有問題、
> `267009`（`0x41301`）目前仍在執行中、`267011`（`0x41303`）從來沒有執行過。

#### 3. 手動觸發排程測試

不必等到排定時間，隨時可以自己觸發一次確認排程正常（**不需要系統管理員權限**）：

```powershell
Start-ScheduledTask -TaskName "PostgresBackup_Daily"
```

> **排程備份不會出現在圖形介面的「備份歷史」頁面。** 稽核歷史存放於 `%LOCALAPPDATA%\PostgresBackup\history.db`，而 `%LOCALAPPDATA%` 會隨 Windows 帳號解析到不同位置：以 `SYSTEM` 身分執行的排程任務，其歷史寫在 `C:\Windows\System32\config\systemprofile\AppData\Local\PostgresBackup\history.db`，圖形介面讀的則是你目前登入帳號的那一份。備份檔案本身不受影響，只有稽核紀錄落在別處。排程執行的結果請以下方的日誌檔與命令列退出碼來確認。

#### 4. 檢查備份結果與日誌
進入備份輸出目錄底下的 `logs\` 子目錄（例如 `D:\DatabaseBackups\my_database\logs`），
開啟當次日誌檢視輸出。成功的一次長這樣：

```text
[2026-09-17 23:58:37] [INFO] === 開始執行 PostgreSQL 排程備份 ===
[2026-09-17 23:58:37] [INFO] 連線設定：正式環境
[2026-09-17 23:58:37] [INFO] 輸出目錄：D:\DatabaseBackups\my_database
[23:58:37] 啟動備份作業: 資料庫 'my_database'
[23:58:37] 格式: Custom, 模式: SchemaAndData, 範圍: FullDatabase
[23:58:40] [SUCCESS] 備份作業順利完成！
[2026-09-17 23:58:40] [INFO] 備份完成：my_database_20260917235837.dump（128.40 MB）
[2026-09-17 23:58:40] [INFO] 備份檔：已清除 my_database_20260910235836.dump（2026-09-10）
[2026-09-17 23:58:40] [INFO] 備份檔：共清除 1 個檔案，釋出 127.90 MB。
[2026-09-17 23:58:40] [INFO] 還原前快照：沒有超過 30 天的檔案需要清理。
[2026-09-17 23:58:40] [INFO] 日誌：已清除 0 個超過 30 天的日誌檔。
[2026-09-17 23:58:40] [INFO] === 排程作業順利結束（退出碼 0）===
```

失敗的一次會以 `[ERROR]` 結尾並帶出退出碼，而且會把該次留下的 0 位元組空檔清掉：

```text
[2026-09-18 02:00:03] [ERROR] 備份失敗，pgbackup 回傳退出碼 1。詳細錯誤見上方 pg_dump 的輸出。
[2026-09-18 02:00:03] [WARN] 已清除本次失敗留下的 0 位元組空檔：my_database_20260918020001.dump
[2026-09-18 02:00:03] [ERROR] === 排程作業結束（退出碼 1）===
```

---

### 5.4 檔案與路徑一覽

排程備份牽涉到的東西散在好幾個地方，這一節把它們集中列出來。表格裡的 `<備份目錄>`
指的是你在設定時指定的 `-BackupDir`，其餘位置則是固定的。

#### 你自己決定位置的東西

| 內容 | 位置 | 說明 |
| :--- | :--- | :--- |
| **備份檔** | `<備份目錄>\` | 檔名為 `{資料庫名稱}_{yyyyMMddHHmmss}.dump`（或 `.sql`）。由 `-RetentionDays` 控制保留天數 |
| **還原前快照** | `<備份目錄>\snapshots\` | 檔名為 `{資料庫名稱}_snapshot_{yyyyMMddHHmmss}.dump`。**由還原作業產生，不是備份產生的**；位置固定在「來源備份檔所在目錄」底下，CLI 無參數可改。由 `-SnapshotRetentionDays` 控制 |
| **執行日誌** | `<備份目錄>\logs\` | 檔名為 `backup_{yyyyMMdd_HHmmss}.log`，每次執行一個檔。由 `-LogRetentionDays` 控制 |
| **腳本** | `scripts\` | `register-backup-task.ps1`、`backup_task.ps1`、`check-backup-status.ps1` |
| **`pgbackup.exe`** | 你安裝的位置 | 例如 `C:\Tools\PostgresBackup\` |
| **官方客戶端工具** | 你安裝的位置 | 例如 `C:\Tools\pgsql\bin`（免安裝可攜版）或 `C:\Program Files\PostgreSQL\18\bin`（winget 安裝） |

> **備份目錄、腳本與 `pgbackup.exe` 都不要放在個人資料夾底下**（桌面、下載、
> `C:\Users\<你的帳號>\...`）。排程以 `SYSTEM` 身分執行，讀不到那些位置。
> 請放在 `C:\Tools\`、`C:\Program Files\` 或某個資料碟的共用目錄。

> **備份目錄的磁碟空間要抓兩倍。** 還原作業會在 `snapshots\` 再寫一份完整備份，
> 等於同一顆磁碟上要放得下兩份。

#### 位置固定的東西

| 內容 | 位置 | 誰讀得到 |
| :--- | :--- | :--- |
| **命令列連線設定**（非機密欄位） | `%ProgramData%\PostgresBackup\cli-connection-profiles.json` | 系統管理員與 `SYSTEM` |
| **命令列連線設定的加密密碼** | `%ProgramData%\PostgresBackup\cli-credentials.dat` | 系統管理員與 `SYSTEM` |
| **介面連線設定**（WPF 用，與上面完全獨立） | `%LOCALAPPDATA%\PostgresBackup\` 與 Windows 認證管理員 | 只有你這個 Windows 帳號 |
| **你手動操作的稽核歷史** | `%LOCALAPPDATA%\PostgresBackup\history.db` | 只有你這個 Windows 帳號 |
| **排程執行的稽核歷史** | `%SystemRoot%\System32\config\systemprofile\AppData\Local\PostgresBackup\history.db` | `SYSTEM` |

`%ProgramData%` 通常是 `C:\ProgramData`，`%LOCALAPPDATA%` 通常是
`C:\Users\<你的帳號>\AppData\Local`，`%SystemRoot%` 通常是 `C:\Windows`。

#### 哪些需要系統管理員權限？

| 你想做的事 | 需要提權？ | 指令 |
| :--- | :---: | :--- |
| 查排程跑得好不好、看備份檔與日誌 | **否** | `.\check-backup-status.ps1 -BackupDir "<備份目錄>"` |
| 查排程的上次執行結果 | **否** | `Get-ScheduledTaskInfo -TaskName "PostgresBackup_Daily"` |
| 手動觸發一次備份 | **否** | `Start-ScheduledTask -TaskName "PostgresBackup_Daily"` |
| 建立或更新連線設定 | **是** | `pgbackup profile set --name "..." ...` |
| 查看連線設定清單 | **是** | `pgbackup profile list` |
| 註冊或移除排程任務 | **是** | `.\register-backup-task.ps1` ／ `Unregister-ScheduledTask` |

> **為什麼看連線設定要提權，看備份結果卻不用？** 因為存放區裡有密碼，備份目錄裡沒有。
> 存放區刻意只開放給系統管理員與 `SYSTEM`——這道檔案權限就是保護密碼的那道防線
> （詳見[第 6 節](#6-安全性說明)）。備份檔與日誌沒有這個理由，不該、也不會擋你。
>
> **請不要為了免提權而把自己的帳號加進 `%ProgramData%\PostgresBackup` 的權限清單。**
> 那會讓任何以你身分執行、但沒有提權的程式（隨手跑的腳本、下載來的工具、中招的常駐程式）
> 都能讀走加密檔並直接解開密碼——機器範圍加密的解密**不需要**任何特殊權限，
> 檔案權限才是唯一擋住它的東西。要看連線設定時，開一個系統管理員的 PowerShell 就好。

#### 排程任務本身

| 項目 | 預設值 |
| :--- | :--- |
| 任務名稱 | `PostgresBackup_Daily`（可用 `-TaskName` 更改） |
| 執行身分 | `SYSTEM`（`ServiceAccount` 登入類型，最高權限層級） |
| 觸發時間 | 每日 `02:00`（可用 `-At` 更改） |

---

### 5.5 移除排程備份

不想再自動備份了，或要把這台機器上的設定收掉時，用
[`scripts\unregister-backup-task.ps1`](../scripts/unregister-backup-task.ps1)。

#### 先預演，看清楚會動到什麼

`-DryRun` 只顯示「會做什麼」，不會刪任何東西，而且**不需要系統管理員權限**：

```powershell
.\unregister-backup-task.ps1 -BackupDir "D:\DatabaseBackups\my_database" -DryRun
```

它會先盤點：排程任務還在不在、存放區有幾個檔案、備份目錄裡有幾個備份檔與快照、總共多大、
最新一份是什麼時候的。**看清楚再決定**。

#### 確認無誤後，以系統管理員身分執行

```powershell
# 只停排程（最常見。備份檔、連線設定都留著）
.\unregister-backup-task.ps1

# 停排程並清掉連線設定（含其加密密碼）
.\unregister-backup-task.ps1 -RemoveProfile -ProfileName "正式環境"

# 整套拆乾淨，連備份檔一起刪
.\unregister-backup-task.ps1 -RemoveProfile -ProfileName "正式環境" `
                             -RemoveStore -RemoveBackups -BackupDir "D:\DatabaseBackups\my_database"
```

#### 預設什麼都不刪，只移除排程任務

這是刻意的：**停掉排程，跟丟掉既有備份，是兩件完全不同的決定。** 很多人只是要暫停自動備份，
或把排程搬到別台機器，既有的備份檔還得留著。所以要刪東西都得明確指定：

| 參數 | 會刪掉什麼 |
| :--- | :--- |
| （不加任何參數） | 只移除排程任務 |
| `-RemoveProfile -ProfileName "..."` | 再加上那筆命令列連線設定與其加密密碼 |
| `-RemoveStore` | 再加上整個 `%ProgramData%\PostgresBackup` 目錄 |
| `-RemoveBackups -BackupDir "..."` | **再加上整個備份目錄**，含所有備份檔、還原前快照與日誌 |

破壞性的那兩項會要求你**輸入完整路徑**才執行，不是打個 `y` 就算數——比照本工具還原作業的
防呆設計。在工作排程器、CI 這類沒有人值守的環境下讀不到輸入，會安全取消而不是硬做；
自動化流程請明確加上 `-Yes`。

#### 幾個會擋住你的情況（都是刻意的）

- **存放區裡還有其他連線設定時，`-RemoveStore` 會拒絕執行。** 同一台機器可能有多組排程
  共用那個存放區，刪掉會一起弄壞。確定要刪請再加 `-Force`。
- **找不到排程任務不算失敗。** 它會提示你可能當初用了不同的 `-TaskName`，並給出列出所有
  相關任務的指令：`Get-ScheduledTask | Where-Object TaskName -like "*PostgresBackup*"`。

#### 這支腳本不會碰的東西

結尾會明講，免得你以為已經清乾淨了：

- 排程執行的稽核歷史：`%SystemRoot%\System32\config\systemprofile\AppData\Local\PostgresBackup\history.db`
- 你手動操作的稽核歷史：`%LOCALAPPDATA%\PostgresBackup\history.db`
- `pgbackup.exe` 與 PostgreSQL 官方客戶端工具本身
- WPF 圖形介面的**介面連線設定**（那套存在你的 Windows 帳號底下，與命令列連線設定完全獨立）

#### 不用腳本，手動移除

```powershell
# 移除排程任務
Unregister-ScheduledTask -TaskName "PostgresBackup_Daily" -Confirm:$false

# 移除命令列連線設定（含其加密密碼）
pgbackup profile remove --name "正式環境"
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
