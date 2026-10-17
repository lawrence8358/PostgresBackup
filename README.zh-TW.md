[English](README.md) | [繁體中文](README.zh-TW.md)

# PostgresBackup v1.0.0

PostgreSQL 備份與還原工具，提供 Windows 桌面介面與 Windows / Linux 命令列。備份使用官方 `pg_dump`；還原預設先建立安全快照，再由 `pg_restore` 或 `psql` 執行。

## DB 備份交接包

每份資料夾都自帶 SOP、CLI、所需軟體與腳本，可獨立操作：

- [Windows CLI 排程與還原](docs/db-backup/windows/README.md)
- [Linux CLI 排程與還原](docs/db-backup/linux/README.md)

## 開始操作

第一次使用，先選一份手冊照著做。手冊包含下載、設定、操作截圖及還原後的資料核對。

| 你要做什麼 | 手冊 |
| --- | --- |
| 用滑鼠備份、還原 | [桌面版](docs/sop/desktop.md) |
| 每天自動備份，需要時還原 | [Windows CLI 排程](docs/sop/windows-cli.md) |
| 在 Linux 執行備份、還原 | [Linux CLI](docs/sop/linux-cli.md) |
| 用測試資料先練習 | [建立練習資料庫](docs/sop/README.md) |
| 用 100 萬筆資料測備份與還原 | [大資料負載測試](docs/sop/stress-test.md) |

![桌面版完成備份](docs/sop/images/desktop-02-backup.png)

## 準備程式

- 下載完整 [Windows](https://github.com/lawrence8358/PostgresBackup/releases/latest/download/PostgresBackup-Windows-x64.zip) 或 [Linux](https://github.com/lawrence8358/PostgresBackup/releases/latest/download/PostgresBackup-Linux-x64.zip) CLI 包，或下載[原始碼](https://github.com/lawrence8358/PostgresBackup)自行建置。
- Windows 自行建置時需另外準備 [PostgreSQL 客戶端工具](https://www.enterprisedb.com/download-postgresql-binaries)，將 ZIP 全部解壓到 `C:\Tools`，保留包內整個 `pgsql`（全部檔案與子目錄），工具設定填 `C:\Tools\pgsql\bin`。客戶端主版本不可低於伺服器版本。
- 自行建置需 [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)。在專案目錄執行：

```powershell
.\build.ps1
```

Windows 桌面程式在 `dist\wpf\PostgresBackup.Wpf.exe`，CLI 在 `dist\cli\pgbackup.exe`。Linux 建置方式見 Linux 手冊。

## 功能與使用提醒

- 支援 Custom (`.dump`) 與 SQL (`.sql`) 備份；可選完整資料庫、指定綱要或資料表。
- 支援結構與資料、僅結構、僅資料備份，以及一般、清除重建、僅資料還原。
- 桌面提供繁體中文 / 英文、工具版本檢查、連線設定及作業歷史。
- 還原前先停止應用程式寫入，保留安全快照，還原後用 SQL 核對資料。
- 桌面與 CLI 的連線設定各自獨立；Windows `--profile` 使用加密存放區，需管理員或 SYSTEM 權限。密碼檔做法與登入限制見各平台手冊。

## 參考文件

- [CLI 命令、安全性與疑難排解](docs/USER_MANUAL.md)
- [SYSTEM 排程腳本與參數](scripts/README.md)
- [產品規格](PRODUCT.md) · [領域模型](CONTEXT.md) · [設計規範](DESIGN.md)

開發檢查：`dotnet build PostgresBackup.sln`、`dotnet test PostgresBackup.sln`。

本專案採 [MIT License](LICENSE)。
