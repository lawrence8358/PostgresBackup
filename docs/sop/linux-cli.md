# Linux CLI 備份與還原手冊

本手冊已在 WSL 2 → Debian 13 容器實際操作，使用 Linux 版 `pgbackup`、PostgreSQL 18.6。

## 1. 下載與建置

已安裝的工具可跳過：[Docker Desktop](https://docs.docker.com/desktop/setup/install/windows-install/)、[WSL 2](https://learn.microsoft.com/windows/wsl/install)、[.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)。下載[專案原始碼](https://github.com/lawrence8358/PostgresBackup)，在 Windows PowerShell 用 `cd` 進入解壓後含有 `src` 的專案根目錄，再執行：

```powershell
dotnet publish src/PostgresBackup.Cli/PostgresBackup.Cli.csproj `
  -c Release -r linux-x64 --self-contained true `
  -p:PublishSingleFile=true -o C:\Tools\PostgresBackupLinux
```

保留整個輸出資料夾，包含 `pgbackup` 和 `libe_sqlite3.so`。

## 2. 進入 Linux

先依[操演入口](README.md)準備 `sop-postgres` 容器與 `sop_linux` 資料庫，內有 3 位客戶、5 筆訂單，總額 5200。

在 PowerShell 建立操作容器；PostgreSQL 工具由[官方映像檔](https://hub.docker.com/_/postgres)提供：

```powershell
New-Item -ItemType Directory -Force C:\Backups\LinuxSop
docker run -d --name sop-linux-cli --network container:sop-postgres `
  --mount "type=bind,source=C:\Tools\PostgresBackupLinux,target=/opt/pgbackup" `
  --mount "type=bind,source=C:\Backups\LinuxSop,target=/backups" `
  --entrypoint bash postgres:18.6 -lc "sleep infinity"
wsl -l -q
```

已有 `sop-linux-cli` 就跳過建立指令。先用下面的 `docker inspect` 查看掛載：`/opt/pgbackup` 的來源必須有剛建置的整包檔案；若 `/backups` 沒有掛載，備份只在容器內，請依第 7 節複製出來。

把 `$WslDistro` 改成 `wsl -l -q` 清單中的完整名稱；本次是 `Ubuntu-20.04`。在 Docker Desktop 的 **Settings → Resources → WSL Integration** 啟用該名稱，按 **Apply & restart**，再執行：

```powershell
docker inspect sop-linux-cli --format '{{json .Mounts}}'
$WslDistro = 'Ubuntu-20.04'
docker start sop-postgres sop-linux-cli
wsl -d $WslDistro -- docker exec -it sop-linux-cli bash
```

**接下來都在 Linux shell 執行**，先切到 `/opt/pgbackup`。這裡連資料庫用 `localhost:5432`；Windows 使用的 `55432` 不適用。客戶端工具目錄是版本 **18** 的 **bin 層**，不是 `/usr/lib/postgresql`：

```bash
cd /opt/pgbackup
chmod +x /opt/pgbackup/pgbackup
ls /usr/lib/postgresql/18/bin/{pg_dump,pg_restore,psql}
export PATH=/opt/pgbackup:/usr/lib/postgresql/18/bin:$PATH
pgbackup check-tools --pg-bin-path /usr/lib/postgresql/18/bin
```

看到 `READY` 就可以繼續。

## 3. 設定密碼

Linux 不使用 Windows 的 `profile set`，改用 [`.pgpass` 密碼檔](https://www.postgresql.org/docs/current/libpq-pgpass.html)。提示時輸入操演入口提供的資料庫密碼，畫面不會顯示輸入內容。

```bash
umask 077
read -rsp '資料庫密碼：' DB_PASSWORD; echo
DB_PASSWORD=${DB_PASSWORD//\\/\\\\}
DB_PASSWORD=${DB_PASSWORD//:/\\:}
printf 'localhost:5432:sop_linux:postgres:%s\n' "$DB_PASSWORD" > ~/.pgpass
unset DB_PASSWORD
chmod 600 ~/.pgpass
export PGHOST=localhost PGPORT=5432 PGUSER=postgres PGDATABASE=sop_linux
psql -X -c 'SELECT count(*) AS orders, sum(amount) AS total FROM orders;'
```

應看到 `5` 筆、`5200.00`。密碼檔是明文，權限須維持 `600`，不要簽入 Git。若登入失敗，核對檔案中的主機、連接埠、資料庫、帳號與密碼。

## 4. 備份

```bash
mkdir -p /backups
pgbackup backup -H localhost -P 5432 -d sop_linux -u postgres \
  -f custom -o /backups --output-file sop_linux.dump \
  --pg-bin-path /usr/lib/postgresql/18/bin --log /backups/backup.log
echo "backup exit=$?"
ls -lh /backups/sop_linux.dump
```

看到「備份成功完成」、`backup exit=0`，檔案大小大於 0 就成功。本次約 2.9 KB。正式備份可省略 `--output-file`，讓工具依時間命名，避免覆蓋舊檔。

![Linux CLI 備份完成，核對 5 筆訂單、總額 5200](images/linux-backup.png)

## 5. 模擬誤刪

**只對測試資料庫執行。** 第一行查詢必須顯示 `sop_linux`，再執行刪除：

```bash
psql -X -c 'SELECT current_database();'
psql -X -v ON_ERROR_STOP=1 -c 'DELETE FROM orders WHERE id IN (4,5);'
psql -X -c 'SELECT count(*) AS orders, sum(amount) AS total FROM orders;'
```

應剩下 `3` 筆、`2480.00`。此時不要重新備份到原檔，以免覆蓋正確資料。

## 6. 還原與核對

先暫停其他程式寫入資料庫，再執行。`-m clean` 會清除備份涵蓋的物件後重建，`-y` 代表確認執行；先核對目標資料庫名稱。

```bash
pgbackup restore -f /backups/sop_linux.dump \
  -H localhost -P 5432 -d sop_linux -u postgres \
  -m clean -y --pg-bin-path /usr/lib/postgresql/18/bin \
  --log /backups/restore.log
echo "restore exit=$?"
psql -X -c 'SELECT count(*) AS customers FROM customers;'
psql -X -c 'SELECT count(*) AS orders, sum(amount) AS total FROM orders;'
ls -lh /backups/snapshots/
```

工具會先建立還原前安全快照，再還原資料。成功時應看到 `restore exit=0`、`3` 位客戶、`5` 筆訂單、總額 `5200.00`，以及 `snapshots` 裡的快照檔。失敗時先看 `/backups/restore.log`，保留快照再處理。

![誤刪後完成安全快照與還原，恢復 5 筆訂單、總額 5200](images/linux-restore.png)

## 7. 取得備份檔

若 `/backups` 掛載為本手冊的 `C:\Backups\LinuxSop`，就到該 Windows 目錄取 `.dump`、兩份 `.log` 和 `snapshots`；若掛載到其他來源，就去 `docker inspect` 顯示的來源目錄。

若沿用本次實測容器，其備份尚在容器內；回到 PowerShell 複製出來：

```powershell
New-Item -ItemType Directory -Force C:\Backups\LinuxSop
docker cp sop-linux-cli:/backups/. C:\Backups\LinuxSop
```
