# 備份與還原練習

先選手冊；想用測試資料練習，再依下方指令建立資料庫。

| 你要做什麼 | 手冊 |
| --- | --- |
| 用 Windows 桌面程式備份、還原 | [桌面版](desktop.md) |
| 用 Windows CLI 每天排程備份、需要時還原 | [Windows CLI](windows-cli.md) |
| 在 Linux 執行 CLI 備份、還原 | [Linux CLI](linux-cli.md) |
| 用 100 萬筆資料測備份與還原 | [大資料負載測試](stress-test.md) |

![桌面版備份完成](images/desktop-02-backup.png)

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

完成後回到上方手冊，依序備份、修改測試資料、還原及核對結果。練習完可用 `docker stop sop-postgres sop-linux-cli` 停止容器；資料仍保留。
