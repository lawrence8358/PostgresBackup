# 備份與還原 SOP 操演入口

三份手冊都已用真實資料庫操作，截圖也來自本次執行：

| 你要做什麼 | 手冊 |
| --- | --- |
| 用 Windows 桌面程式備份、還原 | [桌面版](desktop.md) |
| 用 Windows CLI 每天排程備份、需要時還原 | [Windows CLI](windows-cli.md) |
| 在 Linux 執行 CLI 備份、還原 | [Linux CLI](linux-cli.md) |

## 先建立練習資料庫

先啟動 Docker Desktop，等引擎就緒。尚未安裝時可用 [Docker Desktop 官方安裝頁](https://docs.docker.com/desktop/setup/install/windows-install/)與 [WSL 官方安裝說明](https://learn.microsoft.com/windows/wsl/install)。本次沒有使用既有正式資料庫。

在 Windows PowerShell 執行下列指令，建立獨立 PostgreSQL 18.6 容器，僅供本機練習。測試密碼 `SopDemo2026!` 請只用在這個練習庫。

```powershell
docker run -d --name sop-postgres -p 127.0.0.1:55432:5432 -e POSTGRES_PASSWORD=SopDemo2026! postgres:18.6
docker exec sop-postgres pg_isready -U postgres
```

若尚未顯示 `accepting connections`，等幾秒再執行 `pg_isready`。若提示名稱重複，先用 `docker ps -a --filter name=sop-postgres` 確認是否就是已建立的練習環境；已存在且停止時執行 `docker start sop-postgres`，不用刪除重建。

接著建立三個獨立測試資料庫。**這段只在第一次建立時執行**；各平台使用自己的庫，避免互相影響。

```powershell
$seed = @'
CREATE TABLE customers(id integer PRIMARY KEY, name text NOT NULL);
CREATE TABLE orders(id integer PRIMARY KEY, customer_id integer REFERENCES customers(id), amount numeric(10,2) NOT NULL);
INSERT INTO customers VALUES (1,'Alice'),(2,'Bob'),(3,'Carol');
INSERT INTO orders VALUES (1,1,1200.00),(2,2,850.00),(3,1,430.00),(4,3,2100.00),(5,2,620.00);
'@
foreach ($db in @('sop_desktop','sop_windows','sop_linux')) {
    docker exec sop-postgres createdb -U postgres $db
    if ($LASTEXITCODE -ne 0) { throw "建立 $db 失敗，先檢查是否已存在" }
    $seed | docker exec -i sop-postgres psql -v ON_ERROR_STOP=1 -U postgres -d $db
    if ($LASTEXITCODE -ne 0) { throw "建立 $db 測試資料失敗" }
}
```

執行以下查詢，應看到 3 位客戶、5 筆訂單與金額 `5200.00`：

```powershell
docker exec sop-postgres psql -U postgres -d sop_windows -c 'SELECT count(*) AS customers FROM customers; SELECT count(*) AS orders, sum(amount) AS total FROM orders;'
```

Windows 連線使用 `127.0.0.1:55432`、帳號 `postgres`；Linux 手冊的操作容器共用資料庫容器網路，連線使用 `localhost:5432`。Docker 已包含伺服器，但 Windows 端仍須安裝 Windows PostgreSQL 客戶端工具，三份手冊均有下載連結。

## 怎麼確認還原真的有效

先備份，再只對測試庫故意改資料，最後還原和查詢。桌面手冊有對應練習步驟；Windows CLI 可在備份成功後、還原前執行：

```powershell
docker exec sop-postgres psql -U postgres -d sop_windows -c 'DELETE FROM orders WHERE id IN (4,5); SELECT count(*) AS orders, sum(amount) AS total FROM orders;'
```

預期變成 3 筆 / `2480.00`。接著依 Windows CLI 手冊還原，應回到 5 筆 / `5200.00`。這些刪除指令只對練習庫執行。

## 本次實測紀錄

| 路徑 | 備份後故意修改 | 還原後核對 | 證據 |
| --- | --- | --- | --- |
| Windows 桌面 WPF | Alice 改為 CHANGED，刪 1 筆訂單至 4 筆 / 4580 | Alice、3 位客戶、5 筆 / 5200 | 真實視窗中以 UI Automation 觸發備份與還原按鈕，5 張 WPF 圖與 SQL 核對圖 |
| Windows CLI | 刪 2 筆訂單至 3 筆 / 2480 | 3 位客戶、5 筆 / 5200 | 工作排程器實際觸發，LastTaskResult=0；備份 2927 bytes、快照 2913 bytes |
| Linux CLI | 刪 2 筆訂單至 3 筆 / 2480 | 3 位客戶、5 筆 / 5200 | WSL 2 → Debian 13 Linux 容器，linux-x64 CLI，快照 2930 bytes |

實測工具皆為 PostgreSQL 18.6。Windows 11 使用 .NET SDK 10.0.101 建置；Linux 發行檔自帶執行階段，實際執行的是 Linux 二進位檔。Ubuntu 20.04 僅作 WSL 入口，CLI 與 PostgreSQL 工具在 Debian 13 容器內執行。Windows 排程實測使用目前登入帳號；SYSTEM 方案需要管理員，另列為選項，未冒稱已測。

未發現影響上述流程的產品 bug。完整測試：Core 124、CLI 56、WPF 49，合計 229 項通過。模擬腳本、執行檔、資料庫及備份檔沒有簽入；本目錄保存手冊與真實截圖。

練習完想釋放資源，可以 `docker stop sop-postgres sop-linux-cli`，資料仍留在容器中。備份檔另存到適當儲存空間後，再由接手者決定是否刪除容器。
