# PostgresBackup — 產品規格與願景 (Product Context)

## Product Vision
PostgresBackup 是一套專為資料庫管理員、開發人員與維運工程師打造的高可靠性 PostgreSQL 官方備份與還原工具。提供現代化的桌面圖形介面（WPF）與完整的命令列介面（CLI），直接整合 PostgreSQL 官方原生 `pg_dump`、`pg_restore` 與 `psql` 工具，消除備份不一致性、避免災難性覆蓋，並具備完備的歷史稽核與憑證安全防護。

## Target Users
1. **資料庫管理員 (DBA)**：需要可靠、可驗證版本相容性，且能自訂 Custom (`-Fc`) / Plain SQL (`-Fp`) 格式的備份還原作業。
2. **開發人員 (Developer)**：需要快速備份特定 Schema 或 Table，並在開發測試環境間安全還原資料。
3. **維運與自動化工程師 (DevOps)**：需要藉由 CLI 與 Windows 工作排程器排程執行備份，並輸出標準稽核日誌。

## Key Principles & Guardrails
1. **官方工具優先**：堅持使用 PostgreSQL 官方 `pg_dump`、`pg_restore` 與 `psql`，杜絕自製解析器導致的資料遺失風險。
2. **安全防衛第一**：還原作業強制具備「還原前安全快照（Pre-Restore Snapshot）」機制，快照失敗立即終止還原；提供明確高危險操作警示。
3. **零明文憑證**：資料庫密碼使用 Windows Credential Manager 安全保護，禁止明文寫入設定檔。
4. **極致統一的工藝水準**：UI 介面遵循綠寶石（Emerald `#2E7D32`）主題與深色控制台終端，全系統提供繁體中文與英文在地化支援。
