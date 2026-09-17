# PostgresBackup

PostgreSQL 資料庫備份與還原工具，提供圖形介面（WPF）與命令列介面（CLI），整合官方 pg_dump 與 pg_restore 工具進行具版本相容性檢驗之備份與還原作業。

## Language

### 備份與還原

**備份作業（Backup Job）**:
使用 `pg_dump` 自指定 PostgreSQL 資料庫產出備份檔案之單次操作。
_Avoid_: 備份排程, 轉儲 (Dump), 備份任務

**還原作業（Restore Job）**:
使用 `pg_restore` 或 `psql` 將備份檔案載入目標 PostgreSQL 資料庫之單次操作。
_Avoid_: 資料庫回復, 匯入作業, 還原程序

**備份檔案（Backup File）**:
備份作業所產出之實體檔案，包含自訂二進位檔（`.dump`）或純文字 SQL 腳本（`.sql`）。
_Avoid_: 備份快照, 轉儲檔, 備份封裝

**備份格式（Backup Format）**:
備份檔案之輸出組織形式，限定為自訂格式（Custom, `-Fc`）或純文字格式（Plain SQL, `-Fp`）。
_Avoid_: 檔案類型, 儲存編碼

**備份範圍（Backup Scope）**:
備份作業涵蓋之資料庫物件邊界，區分為完整資料庫、指定綱要（Schema）或指定資料表（Table）。
_Avoid_: 備份層級, 選取範圍

**備份模式（Backup Mode）**:
備份作業所萃取之內容範疇，限定為結構與資料（Schema + Data）、僅結構（Schema Only）或僅資料（Data Only）。
_Avoid_: 備份類型, 萃取深度

**還原模式（Restore Mode）**:
還原內容寫入目標資料庫時之物件覆寫策略，包含清除並重建、僅建立遺漏物件或僅寫入資料。
_Avoid_: 還原策略, 覆寫模式

**還原前安全快照（Pre-Restore Snapshot）**:
執行還原作業前，由系統自動對目標資料庫執行之防禦性備份，作為事故復原之還原點。
_Avoid_: 自動快照, 還原點, 安全備份

### 連線與環境

**連線設定（Connection Settings）**:
連線至 PostgreSQL 執行個體所需之伺服器端點與驗證資訊組合。
_Avoid_: 資料庫連線字串, 連線組態

**客戶端工具（Client Tools）**:
PostgreSQL 官方發行版隨附之 `pg_dump`、`pg_restore` 與 `psql` 執行檔。
_Avoid_: 外部工具, 第三方依賴, 備份引擎

**工具偵測狀態（Tool Detection Status）**:
本機環境中官方客戶端工具之存在性與版本相容性檢驗結果，包含就緒、版本不相容或未偵測到。
_Avoid_: 工具狀態, 軟體檢查

### 歷史稽核與資料倉儲

**備份紀錄（Backup Record）**:
單次備份或還原作業完成後所留存之不可變稽核項目，包含時間戳記、資料庫名稱、檔案路徑、執行時長與結果狀態。
_Avoid_: 操作日誌, 歷史條目, 執行紀錄

**備份歷史倉儲（Backup History Repository）**:
負責持久化與查詢備份紀錄之獨立資料存取層。
_Avoid_: 歷史資料庫, 紀錄管理器, SQLite 表
