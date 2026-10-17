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

## 取消執行中的作業

桌面按「取消作業」，CLI 按 Ctrl+C（退出碼 `130`）。取消的備份可能不完整，不能拿來還原。

還原取消不會復原已寫入的資料；先查目標狀態，再依日誌中的安全快照路徑決定是否還原。

## 1. 系統簡介與特色

PostgresBackup 提供桌面與 CLI，透過 PostgreSQL 官方工具備份及還原。第一次操作請直接看[圖文 SOP](sop/README.md)；本手冊保留完整命令參數與維護參考。

- 備份可選完整資料庫、綱要或資料表，以及 Custom / SQL 格式。
- 還原預設先建立安全快照；快照失敗便停止。
- 桌面與 CLI 連線設定分開存放，密碼保護方式見[第 6 節](#6-安全性說明)。

## 2. PostgreSQL 官方客戶端工具下載與安裝 SOP

PostgreSQL 工具採 [PostgreSQL License](https://www.postgresql.org/about/licence/)，可免費用於商業用途；各下載套件所附的相依元件授權請一併保留。

### 方法 A：官方 EDB 可攜式免安裝版 (最推薦)
免安裝版可放在 `C:\Tools\pgsql`，不需安裝完整伺服器。

#### 下載與解壓縮步驟：
1. 開啟 EnterpriseDB 官方二進位下載頁面：  
   👉 **[PostgreSQL Windows Binaries (EDB)](https://www.enterprisedb.com/download-postgresql-binaries)**
2. 選擇對應版本（例如 `18.6` 或 `17.x`）的 **Windows x86-64** ZIP 壓縮檔下載。
3. ZIP 右鍵選「全部解壓縮」，目的地選 `C:\Tools`，保留整個 `pgsql`，**全部檔案與子目錄都不要挑選或刪除**。若已有舊版，先解壓到另一個空目錄，不要混合不同版本。
4. 解壓後應為 `C:\Tools\pgsql\bin\pg_dump.exe`。`pgsql` 內包含 `bin`、`lib`、`share`、`include`、`doc`、`pgAdmin 4`、`StackBuilder` 與授權檔，全部保留；本手冊不提供挑 DLL 的最小清單。
5. 桌面「自訂工具目錄」與 CLI `--pg-bin-path` 均指定 **`C:\Tools\pgsql\bin`**，不是 `C:\Tools\pgsql`，也不是某一個 exe。ZIP 本身已有 `pgsql`，解壓目的地不要再填 `C:\Tools\pgsql`，以免多出 `pgsql\pgsql` 一層。
### 方法 B：Windows Winget 套件管理員安裝

```powershell
winget install PostgreSQL.PostgreSQL.18
```

安裝後確認工具目錄，通常為 `C:\Program Files\PostgreSQL\18\bin`。執行 CLI 時以 `--pg-bin-path` 指定該目錄，不必依賴 PATH。

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

逐步操作見[桌面圖文手冊](sop/desktop.md)。

![桌面備份完成](sop/images/desktop-02-backup.png)

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
| `--password` | `-p` | 資料庫密碼，僅供手動除錯；風險見[第 6 節](#6-安全性說明) | `-p "手動除錯用密碼"` |
| `--connection-string` | `-s` | 完整連線字串，用於相容性檢查；勿用於排程 | `-s "Host=localhost;Database=db;..."` |
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
| `--password` | `-p` | 資料庫密碼，僅供手動除錯；風險見[第 6 節](#6-安全性說明) | `-p "手動除錯用密碼"` |
| `--format` | `-f` | 備份格式 (`custom` 或 `plain`) | `-f custom` |
| `--mode` | `-m` | 模式 (`all`, `schema`, `data`) | `-m all` |
| `--schema` | `-n` | 指定綱要 (可重複使用) | `-n public -n hangfire` |
| `--table` | `-t` | 指定資料表 (可重複使用) | `-t "public.Quote"` |
| `--output-dir` | `-o` | 輸出存放目錄 | `-o "D:\Backups"` |
| `--output-file` | | 直接指定輸出檔案完整路徑，覆寫自動產生的檔名 | `--output-file "D:\Backups\nightly.dump"` |
| `--pg-bin-path` | | 官方工具 bin 目錄路徑 | `--pg-bin-path "C:\Tools\pgsql\bin"` |
| `--log` | | 額外輸出詳細日誌檔路徑 | `--log "D:\Backups\run.log"` |

若指定了不存在的 `--profile` 名稱，`backup` 會立即以非零結束代碼中止，不會略過連線資訊繼續嘗試執行。

> 備份失敗可能留下空檔；請以退出碼與日誌判斷成功，不能只看檔案是否存在。

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
| `--password` | `-p` | 資料庫密碼，僅供手動除錯；風險見[第 6 節](#6-安全性說明) | `-p "手動除錯用密碼"` |
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

#### 無人值守還原

手動還原須輸入目標資料庫名稱；非互動環境若未加 `--yes`，會以非零退出碼取消，不執行還原。使用 `--yes` 前須先核對目標，它只略過確認，仍保留安全快照。

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

密碼預設遮蔽輸入；自動化可用 `--password-stdin` 從機密檔案或部署系統注入，不要直接寫密碼字面值。若用 `--force` 略過連線驗證，資料庫恢復後須重新驗證。
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

一般登入帳號的完整練習見 [Windows CLI 圖文手冊](sop/windows-cli.md)。以下是需要管理員設定、可在沒人登入時執行的 SYSTEM 方案。

### 5.1 現成腳本的位置與用途

| 腳本 | 用途 |
| --- | --- |
| `register-backup-task.ps1` | 建立 CLI 連線設定、註冊 SYSTEM 排程並試跑 |
| `backup_task.ps1` | 備份、寫日誌、清理過期檔案 |
| `check-backup-status.ps1` | 查看排程、日誌與備份結果 |
| `unregister-backup-task.ps1` | 移除排程，預設保留備份及連線設定 |

完整參數見 [scripts 手冊](../scripts/README.md)。腳本使用 `--profile`，不存放資料庫密碼。

### 5.2 以 SYSTEM 身分註冊至 Windows 工作排程器 (Task Scheduler)

以系統管理員身分開啟 PowerShell，在 `scripts` 目錄執行：

```powershell
.\register-backup-task.ps1 `
    -ProfileName "正式環境" `
    -Database my_database -Username postgres `
    -BackupDir "D:\DatabaseBackups\my_database" `
    -PgBinPath "C:\Tools\pgsql\bin" `
    -CliPath "C:\Tools\PostgresBackup\pgbackup.exe" `
    -At 02:00 -RetentionDays 7 -SnapshotRetentionDays 30
```

依提示輸入密碼。程式與腳本須放在 SYSTEM 可讀的固定位置，備份目錄須可寫。可攜式 PostgreSQL 工具必須指定 `-PgBinPath`；連線設定不保存工具路徑。

### 5.3 驗證排程與日誌檢視

```powershell
.\check-backup-status.ps1 -BackupDir "D:\DatabaseBackups\my_database"
Get-ScheduledTaskInfo -TaskName 'PostgresBackup_Daily' | Select-Object LastRunTime, LastTaskResult, NextRunTime
```

`LastTaskResult=0`，日誌有成功訊息，且本輪備份檔大小大於 0 才算完成。需要再試跑時執行：

```powershell
Start-ScheduledTask -TaskName 'PostgresBackup_Daily'
```

### 5.4 檔案與路徑一覽

| 內容 | 位置 |
| --- | --- |
| 備份檔 | 指定的 `BackupDir` |
| 日誌 | `BackupDir\logs` |
| 還原前安全快照 | 來源備份檔旁的 `snapshots` |
| CLI 加密設定 | `%ProgramData%\PostgresBackup` |
| 目前使用者歷史 | `%LOCALAPPDATA%\PostgresBackup\history.db` |
| SYSTEM 歷史 | `C:\Windows\System32\config\systemprofile\AppData\Local\PostgresBackup\history.db` |

SYSTEM 的歷史不會顯示在目前使用者的桌面歷史頁。例行備份預設保留 7 天，快照與日誌保留 30 天；參數設 `0` 代表不清理。

### 5.5 移除排程備份

以管理員執行，預設只移除任務：

```powershell
.\unregister-backup-task.ps1
```

先預覽可加 `-DryRun`。要刪除連線設定或備份，請先查看 [移除參數](../scripts/README.md#移除排程)，確認保留需求再操作。

## 6. 安全性說明

桌面與 CLI 的連線設定各自獨立；在桌面新增設定，不會出現在 `pgbackup profile list`。

| 設定 | 密碼存放方式 | 可存取者 |
| --- | --- | --- |
| 桌面 | Windows Credential Manager | 目前 Windows 帳號 |
| Windows CLI `--profile` | `%ProgramData%\PostgresBackup\cli-credentials.dat`，機器範圍加密 | 管理員與 SYSTEM |
| SOP 的 `.pgpass` / `PGPASSFILE` | 明文密碼檔 | 由檔案權限限制，請依 SOP 設定 |

Windows CLI 的加密檔無法直接搬到另一台電腦解密；同機一般使用者由資料夾權限阻擋，**同機管理員仍可取得密碼**。

`profile set` 寫入前會檢查存放區權限。既有資料夾權限過寬或無法檢查時會拒絕儲存，也不會自動收緊。請管理員確認資料夾來源與既有設定；若確定沒有要保留的設定，才移除存放區並由程式重建。

不要把密碼寫在腳本或 `-p` / `--connection-string` 參數中，這會出現在指令歷史或處理序資訊。Windows SYSTEM 排程使用 `--profile`；密碼檔方案則依平台 SOP 限制讀取權限。CLI 傳給 PostgreSQL 子程序的密碼透過環境變數提供。

## 7. 常見問題排查 (FAQ)

### Q1: 出現 `pg_dump: error: server version: 18.6; pg_dump version: 16.x`
- **原因**：PostgreSQL 官方規範要求客戶端 `pg_dump` 主版本不得低於資料庫伺服器版本。
- **排除方式**：請依本手冊 [第 2 節](#2-postgresql-官方客戶端工具下載與安裝-sop) 下載 PostgreSQL 18.x 官方工具，並於設定中指定該 bin 目錄。

### Q2: 密碼有特殊字元怎麼辦？

程式支援特殊字元。Windows CLI 優先使用 `--profile`；使用密碼檔時，依 SOP 跳脫 `:` 與 `\`。不要把密碼直接寫在命令列。

### Q3: 桌面還原按鈕不能按？

確認已選來源備份、目標連線與資料庫，並勾選風險確認。操作位置見[桌面手冊](sop/desktop.md#4-還原)。

### Q4: 怎麼還原到另一個資料庫？

先建立空的測試資料庫，再於還原頁指定該資料庫，保留安全快照。核對目標後才執行，避免覆蓋正式資料。
