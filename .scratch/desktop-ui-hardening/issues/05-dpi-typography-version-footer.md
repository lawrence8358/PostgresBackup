# 05: 支援每螢幕 DPI 並改善版本頁尾

**What to build:** 讓桌面介面在不同 Windows 顯示縮放與混合 DPI 螢幕上保持清晰可讀，並以準確版本資訊完成側邊欄頁尾。

**Blocked by:** None (can start immediately).

**Status:** resolved

- [x] 桌面執行檔明確宣告 Per-Monitor V2 DPI awareness。
- [x] 一般內文與導覽基礎字級提高一級，終端字級維持資訊密度。
- [x] 版本文字取自組件 informational version，保留 prerelease 並移除 `+` 後的 build metadata。
- [x] 版本標籤可在地化，並以分隔線及淡綠底條與語系選擇器區隔。
- [x] 純測試涵蓋穩定版、prerelease 及含 build metadata 的版本格式化。
