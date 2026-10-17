# 桌面版備份與還原

先依[練習環境說明](README.md)建立測試庫，再照下面流程練習一次。

## 1. 準備程式與連線

- 桌面程式：向交接人取得 `PostgresBackup.Wpf.exe`。若只有[原始碼](https://github.com/lawrence8358/PostgresBackup)，安裝 [.NET 10 SDK（Windows x64）](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)，開啟 PowerShell，切到解壓後含有 `PostgresBackup.sln` 的資料夾，再執行下方指令。
- PostgreSQL 工具：下載 [EDB Windows x86-64 ZIP](https://www.enterprisedb.com/download-postgresql-binaries)，將 ZIP 裡的整個 `pgsql` 資料夾解壓到 `C:\Tools`，全部檔案與子目錄原樣保留（包含 DLL 與授權檔），不要挑檔或刪除。完成後應有 `C:\Tools\pgsql\bin\pg_dump.exe`、`pg_restore.exe`、`psql.exe`；不能是 `C:\Tools\pgsql\pgsql\bin`。本次使用 18.6 版，工具版本不能比伺服器舊。

```powershell
# 先切到原始碼資料夾；若放在別處，請換成自己的路徑。
Set-Location D:\Project\PostgreTools\PostgresBackup
dotnet publish .\src\PostgresBackup.Wpf\PostgresBackup.Wpf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\dist\wpf
.\dist\wpf\PostgresBackup.Wpf.exe
```

已有完整發行程式的人可直接開啟，不用安裝 SDK。

1. 開啟程式，左下角切換「繁體中文」。
2. 點「設定」，自訂工具目錄填 `C:\Tools\pgsql\bin`（選到 `bin` 這一層），按「重新偵測」。如果解壓在別處，換成該位置的完整 `bin` 路徑。
3. 按「新增連線設定檔」，填入下表，按「測試連線並檢查相容性」。
4. 看到連線成功後按「儲存設定檔」。

| 欄位 | 練習值 |
| --- | --- |
| 設定檔名稱 | SOP 桌面練習 |
| 主機／連接埠 | `127.0.0.1`／`55432` |
| 資料庫 | `sop_desktop` |
| 帳號／密碼 | `postgres`／`SopDemo2026!`（只用於練習） |

![設定工具路徑並測試連線](images/desktop-01-settings.png)

## 2. 備份

1. 點「備份」，選「SOP 桌面練習」。
2. 格式選「自訂二進位格式（Custom）」；模式選「結構與資料」；範圍選「完整資料庫」。
3. 輸出目錄填 `D:\SopBackups\Desktop`，按「開始執行備份」。沒有 D 槽可改用 C 槽。
4. 等上方顯示「完成」，確認目錄中有 `.dump` 檔，記下檔名。

![備份完成](images/desktop-02-backup.png)

## 3. 故意刪改測試資料

備份完成後，在 PowerShell 執行這一行。只對練習庫 `sop_desktop` 操作：

```powershell
docker exec sop-postgres psql -U postgres -d sop_desktop -c "UPDATE customers SET name='CHANGED' WHERE id=1; DELETE FROM orders WHERE id=5; SELECT id,name FROM customers ORDER BY id; SELECT count(*) AS orders,sum(amount) AS total FROM orders;"
```

第一位客戶應變成 `CHANGED`，訂單剩 4 筆、合計 `4580.00`。接著用剛才的備份還原。

## 4. 還原

正式還原前，先停止使用這個資料庫的服務寫入資料。

1. 點「還原」，選剛才的 `.dump` 檔。
2. 連線選「SOP 桌面練習」，確認目標資料庫是 `sop_desktop`。
3. 模式選「清除並重建物件（覆寫模式）」。
4. 保留「還原前強制建立安全快照」。程式會先備份目前資料；快照失敗就不會繼續。
5. 核對目標名稱，勾選「我已確認目標資料庫無誤，並了解覆寫風險，同意執行還原」。
6. 按「確認並開始執行還原」，等上方顯示「完成」。

![選擇備份檔並確認還原目標](images/desktop-03-confirm.png)

![還原完成](images/desktop-04-restored.png)

快照存在備份目錄下的 `snapshots` 資料夾。若要回到這次還原之前，再照上面步驟操作，改選含 `_snapshot_` 的快照檔。

## 5. 驗證

點「歷史紀錄」，輸入 `sop_desktop`，確認備份、安全快照、還原都成功。

![查看備份與還原紀錄](images/desktop-05-history.png)

再到 PowerShell 查資料：

```powershell
docker exec sop-postgres psql -U postgres -d sop_desktop -c 'SELECT id,name FROM customers ORDER BY id; SELECT count(*) AS customers FROM customers; SELECT count(*) AS orders,sum(amount) AS total FROM orders;'
```

應看到客戶 `Alice`、`Bob`、`Carol` 共 3 位，訂單回到 **5 筆、合計 5200.00**。

![查詢確認資料已恢復](images/desktop-06-verified.png)

若失敗，先看畫面上的錯誤詳細資料或「複製日誌」：找不到工具就檢查 `bin` 路徑；連線失敗就檢查 Docker、連接埠和帳密；快照失敗就檢查磁碟空間與目錄權限。保留備份與快照，處理完再重試。

要改用排程備份，接著看 [Windows CLI 手冊](windows-cli.md)；CLI 的連線設定要另外建立。

