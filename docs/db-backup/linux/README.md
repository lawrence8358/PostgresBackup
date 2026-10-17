# Linux：DB 備份與還原 v1.0.0

[下載 Linux v1.0.0 完整離線包](https://github.com/lawrence8358/PostgresBackup/releases/latest/download/PostgresBackup-Linux-x64.zip)

Linux x86_64、kernel 5.15+、cgroups／namespaces、Bash／coreutils、root／sudo、systemd，預留 8 GB 加備份空間。Linux／WSL 須保持運行才會備份。

## 1. 啟動

整包放在固定目錄 `/opt/db-backup`，路徑勿含空白或 `%`，設定後勿搬動。軟體已附：[Docker](software/docker-29.9.1.tgz)、[PostgreSQL](software/postgres-18.6.tar)、[CLI](software/cli/)、[BusyBox](software/busybox)，不用另裝 Docker 或 .NET。

若帶走 ZIP，把包內 `software/busybox` 一起放到 ZIP 旁；在 Linux 執行：

```bash
chmod +x ./busybox
sudo ./busybox unzip PostgresBackup-Linux-x64.zip -d /opt/db-backup
cd /opt/db-backup
sudo bash scripts/ops.sh bootstrap
```

已複製整個資料夾到上述位置者，直接執行最後兩行。後續指令都在這個目錄執行。

## 2. 正式排程

向 DBA 取得正式 `DB_HOST`、`DB_PORT`、`DB_NAME`、`DB_USER`、`DB_PASSWORD`，確認帳號有備份／還原權限；`BACKUP_DIR` 填 Linux 備份目錄絕對路徑。以下互動輸入，密碼不顯示；不要填 測試值或占位字串。

```bash
sudo bash scripts/ops.sh configure-production
sudo bash scripts/ops.sh backup production
sudo bash scripts/ops.sh cron-start production '0 2 * * *'
sudo bash scripts/ops.sh install-startup
sudo bash scripts/ops.sh status production
```

每日 **Asia/Taipei 02:00** 備份；密碼存在 `runtime/production.pgpass`（權限 600），勿分享。`install-startup` 設定開機恢復；若單獨重啟容器，執行 `sudo bash scripts/ops.sh boot` 恢復排程。

## 3. 查結果

```bash
sudo bash scripts/ops.sh status production
```

確認 `BACKUP_DIR` 有最新非空 `.dump`，`completed.log` 顯示 `backup_exit=0`；失敗查看同目錄 `backup.log`、`cron-job.log`。手動補備份用上方 `backup production`。

## 4. 還原

先停止應用程式寫入與排程；輸入備份檔名（不含目錄），指令使用容器內的 `/backups/` 路徑：

```bash
sudo bash scripts/ops.sh cron-stop production
read -r -p '輸入 BACKUP_DIR 中選定的 .dump 檔名：' BACKUP_FILE
sudo bash scripts/ops.sh restore production "/backups/$BACKUP_FILE"
```

核對畫面目標，輸入 `DB_NAME` 確認；程式先存快照，再清除並還原備份涵蓋的物件。確認 `completed.log` 的 `restore_exit=0`，請 DBA 核對資料後恢復服務及排程：

```bash
sudo bash scripts/ops.sh cron-start production '0 2 * * *'
```

快照在 `BACKUP_DIR/snapshots/`；要回復還原前狀態，同一還原指令改用 `/backups/snapshots/實際快照檔名`。

## 可選：先練習（須在設定正式排程前完成）

```bash
sudo bash scripts/ops.sh demo-create
sudo bash scripts/ops.sh demo-run
```

只動本包測試庫；十萬筆資料經真 cron 備份、刪改、還原後顯示 `PASS` 即完成。測試 cron 自動停止，勿拿測試帳密當正式值。
