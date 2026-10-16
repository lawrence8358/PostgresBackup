# 備份與還原 SOP

Status: resolved

## 使用者需求

提供繁體中文、白話且新手可操作的三份手冊：桌面版備份還原、Windows CLI 排程備份還原、Linux CLI 備份還原。所需安裝檔提供官方下載連結。

## 驗收

- [x] Docker PostgreSQL 建立測試資料，桌面、Windows、Linux 使用獨立資料庫。
- [x] 三條路徑均真正備份、修改資料、還原並驗證內容；Windows 工作排程器實際觸發。
- [x] Linux 使用 Linux CLI 與 Linux PostgreSQL 工具，記錄真實環境。
- [x] 桌面實際 UI 操作，保留真實截圖；文件與圖片置 docs/sop。
- [x] 模擬與操演腳本不簽入。
- [x] 發現 bug 則修正並重跑，完成測試與雙軸 code review。

## 固定點

開始 HEAD: `86bb8f79d8a032a18cc7e298dc097e9cac9c47c5`；起始工作樹乾淨。

## 驗證

2026-10-16 完成三平台真實備份還原，資料均恢復 3 客戶、5 訂單、5200；10 張實際截圖。dotnet test PostgresBackup.sln --no-restore --verbosity minimal：Core 124、CLI 56、WPF 49，合計229項通過。未發現產品bug。Windows實測一般登入使用者排程，SYSTEM需管理員故未宣稱實測。已完成 Standards / Spec 雙軸 code review；修正桌面圖說的領域用語，並補上 WSL 發行版名稱查詢與 Docker Integration 設定。文件連結與 git diff --check 通過，Linux check-tools 實機重驗 READY / 18.6。

