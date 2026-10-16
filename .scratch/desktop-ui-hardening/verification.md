# Desktop UI hardening verification

Date: 2026-09-19 (Asia/Taipei)

## Automated verification

- `dotnet build PostgresBackup.sln --no-restore -v:minimal`
  - Passed with 0 warnings and 0 errors.
- `dotnet test PostgresBackup.sln --no-build --no-restore -v:minimal`
  - Core: 76 passed.
  - CLI: 52 passed.
  - WPF: 44 passed.
  - Total: 172 passed, 0 failed, 0 skipped.
- WPF tests contain no construction of `App` and no calls to `Run()`, `Show()`, or `ShowDialog()`.
- `git diff --check`
  - Passed; only the repository's existing LF-to-CRLF notices were emitted.

## Visual verification

The explicit capture mode was run with localized demo data at these logical window sizes:

- Large: 1600 × 900
- Default: 960 × 600
- Minimum: 720 × 480

Verified results:

- Settings, Backup, and Restore cards stretch across the available content region.
- Backup and Restore terminals absorb available height up to a 420-DIP cap, retaining an internal scrollbar for longer output.
- Backup and Restore now coalesce terminal publication to approximately 225-ms batches and force a final flush before completion. The regression loop dropped a 1,000-line burst from 1,001 UI-bound notifications to 1–2 while retaining every line.
- At 720 × 480, task content remains reachable through vertical scrolling and controls do not wrap into unusable shapes.
- History's database column absorbs wide-screen space and actions remain at the far right.
- History cells explicitly center their text, badges, and actions vertically within each fixed-height row.
- A user-provided follow-up screenshot exposed that HandyControl's explicit `RowStyle` and `CellStyle` assignments bypassed the initial implicit overrides. The correction now assigns the pale selection styles directly to those two `DataGrid` properties; visual reconfirmation remains pending because the application was not launched for this follow-up.
- User-provided failure output exposed an unbounded status message. Backup and Restore now keep a short summary above a 150-DIP selectable detail area with vertical/horizontal scrollbars and a copy action; visual reconfirmation remains pending because the application was not launched for this follow-up.
- History retains one-line cells and horizontal overflow at the minimum width.
- The language selector and localized assembly version occupy visually separated footer regions.

## DPI matrix

Capture mode records the DPI WPF actually receives through `VisualTreeHelper.GetDpi`, rather than relying on a DPI-virtualized shell process.

| Windows scale | Result | Evidence |
| --- | --- | --- |
| 100% | Not run | Requires changing the active Windows display scale or moving the window to a 96-DPI monitor. |
| 125% | Not run | Requires a 120-DPI display context unavailable in this session. |
| 150% | Passed | WPF reported `DpiScaleX=1.5`, `DpiScaleY=1.5`, `PixelsPerInchX=144`, and `PixelsPerInchY=144`; all three capture sizes passed visual inspection. |
| 200% | Not run | Requires a 192-DPI display context unavailable in this session. |

To finish the physical-display matrix, set each remaining Windows display scale, run:

`dotnet run --project src/PostgresBackup.Wpf/PostgresBackup.Wpf.csproj -- --capture <output-directory> --lang zh-TW --demo-data`

Confirm `capture-metadata.txt` reports the requested DPI and inspect the generated large/default/minimum captures. Changing the host display scale automatically was deliberately avoided because it can disrupt the user's desktop session and may require sign-out.

## 2026-10-16 重新驗證（Asia/Taipei）

本節為最新結果；前面的 2026-09-19 紀錄保留作為歷史。

- `dotnet build PostgresBackup.sln --no-incremental -v:minimal`：成功，0 警告、0 錯誤。
- 建置完成後執行 `dotnet test PostgresBackup.sln --no-build --no-restore -v:minimal`：Core 114、CLI 54、WPF 44，共 212 通過、0 失敗、0 略過。
- 搜尋 WPF 測試，未發現建立 `App` 或呼叫 `Show()`、`ShowDialog()`、`Run()` 的程式碼。GUI 截圖另以明確啟動的 capture 模式執行。
- 本次未執行備份或還原，未連線至資料庫，也不需要 Docker。

以既有 `--capture <output-directory> --lang zh-TW --demo-data` 模式執行兩次，每次產生 13 張 PNG（啟動畫面加上四頁各三種尺寸）。第二次透過 `SetWindowPos` 將本次啟動的視窗移至第二螢幕，再開始截圖；未變更 Windows 顯示設定。

| Windows scale | 本次結果 | 證據（本機，不簽入） |
| --- | --- | --- |
| 100% | 已完成靜態畫面檢查 | `.scratch/test-reports/desktop-ui-20261016/secondary/capture-metadata.txt`：X/Y 均為 1，PPI 均為 96；相同目錄下 13 張 PNG。 |
| 125% | 依使用者決定免驗 | 2026-10-16 使用者表示不需要調整顯示縮放；未執行，未記為通過。 |
| 150% | 已完成靜態畫面檢查；大型高度受限 | `.scratch/test-reports/desktop-ui-20261016/primary/capture-metadata.txt`：X/Y 均為 1.5，PPI 均為 144；相同目錄下 13 張 PNG。 |
| 200% | 依使用者決定免驗 | 2026-10-16 使用者表示不需要調整顯示縮放；未執行，未記為通過。 |

逐頁檢視上述兩組的大型、預設與最小版面：初始設定頁有內容，導覽與頁尾可見，歷史列有淡色選取背景，四頁未見控制項互相重疊。小型設定、備份與還原頁的下方內容超出目前視窗，須捲動；本次截圖未涵蓋捲動操作。作業錯誤詳情的實際顯示亦未由本次靜態截圖覆蓋，其自動化測試通過。

尺寸限制：100% 的 PNG 尺寸符合 1600×900、960×600、720×480；150% 的大型檔名雖為 `large_1600x900_*`，實際 PNG 為 **1600×737**，其餘兩種尺寸符合要求。既有 capture 將 WPF 視窗以固定 96 DPI 輸出 PNG，這些是視覺樹的版面截圖，並非螢幕原生像素截圖，不據此宣稱完成字型銳利度或所有實際互動驗證。

使用者於本次驗證後回覆「不需要調整那麼大」，因此維持目前 Windows 顯示縮放，免除剩餘 125%、200% 驗證。票 06 依調整後的驗收範圍標記為 `resolved`；免驗項目不代表測試通過，上述截圖與互動驗證限制仍適用。
