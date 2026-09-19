# 桌面介面狀態與響應式版面強化

Status: ready-for-agent

## Problem Statement

PostgresBackup 的圖形介面目前在工具尚未完成偵測時就顯示「pg_dump、pg_restore、psql 均已就緒」，讓使用者看到與真實狀態矛盾的訊息；成功使用過的自訂客戶端工具目錄也不會在重新啟動後保留。設定、備份與還原頁在大型螢幕仍被固定內容寬度限制，備份及還原的終端日誌無法利用剩餘高度；歷史紀錄表格則在寬螢幕留下空白，且選取列使用的深綠色會蓋掉作業類型、狀態與操作按鈕的語意色彩。

此外，全域系統日誌只重複聚合本次執行期間的備份與還原輸出，沒有持久化價值；字體與 DPI 行為缺少明確的多螢幕縮放設定；側邊欄版本為硬編碼文字且與語系切換器缺乏視覺區隔。現有導覽測試還會執行真正的 WPF 應用程式並顯示主視窗，不適合一般 CI 單元測試。

## Solution

讓工具偵測呈現完整且誠實的生命週期，並為每位 Windows 使用者保存最後一次成功偵測的自訂客戶端工具目錄。保留既有安裝途徑，再提供 EDB 官方免安裝 ZIP binaries 入口。

將設定、備份、還原與歷史紀錄頁改成能利用可用視窗空間的響應式 WPF 版面：大型視窗填滿內容區，小型視窗仍可捲動且不造成表格操作換行。備份與還原終端填滿剩餘高度；歷史紀錄以低干擾選取色保留徽章及按鈕辨識度，資料欄位彈性伸展而操作欄固定於最右側。

移除重複的全域系統日誌功能。加入明確的 Per-Monitor V2 DPI 支援、提高一般介面基礎字級，並以組件產品版本建立具有視覺分隔的版本資訊區。導覽行為改由不依賴 WPF `App` 執行生命週期的可測試狀態負責，單元測試不得啟動或顯示應用程式。

## User Stories

1. As a database administrator, I want the client-tool status to say that it has not been checked before detection starts, so that I am not falsely told the tools are ready.
2. As a database administrator, I want visible feedback while client-tool detection is running, so that I know the application is working.
3. As a database administrator, I want ready, incompatible, and not-found results to remain visually distinct, so that I can decide what corrective action is needed.
4. As a database administrator, I want the all-tools-ready message to appear only after all required client tools pass detection, so that the status is trustworthy.
5. As a returning user, I want my last successfully detected custom client-tool directory restored on startup, so that I do not need to browse to it every time.
6. As a returning user, I want a previously saved but now invalid tool directory to remain visible, so that I can understand and repair the configuration.
7. As a user without PostgreSQL installed, I want a link to EDB's portable Windows binaries, so that I can obtain the official tools without running an installer.
8. As a user who prefers the installer, I want the existing installation links to remain available, so that the portable option does not remove current workflows.
9. As a user on a large monitor, I want settings cards to use the available content width, so that the page does not appear stranded in the middle of the window.
10. As a user on a large monitor, I want backup and restore cards to use the available content width, so that important controls are easier to scan.
11. As a user on a small monitor, I want settings, backup, and restore pages to remain scrollable, so that every control remains reachable.
12. As a user monitoring a long backup, I want the terminal to grow with the window, so that I can see more output without wasting vertical space.
13. As a user monitoring a long restore, I want the terminal to grow with the window, so that I can see more output without wasting vertical space.
14. As a user on a short window, I want each terminal to retain a usable minimum height, so that operation output does not disappear.
15. As a user reviewing backup records, I want selected rows to remain legible, so that type, status, and action colors keep their meaning.
16. As a user reviewing backup records on a wide monitor, I want the record columns to consume the available width, so that the grid does not leave unused space.
17. As a user reviewing backup records, I want action buttons fixed at the far right, so that actions remain easy to locate.
18. As a user on a narrow window, I want history actions to remain on one line and the grid to scroll horizontally when necessary, so that button labels and actions are not distorted.
19. As a user, I want duplicate global runtime logs removed, so that navigation contains only distinct, useful destinations.
20. As a user, I want operation details to remain available on the backup and restore pages after the global log is removed, so that diagnostics are not lost.
21. As a user with Windows display scaling enabled, I want the interface to render crisply at the active monitor's scale, so that text and controls are readable across monitors.
22. As a user at 100% display scale, I want ordinary interface text to be slightly larger, so that the application is comfortable to read without changing Windows settings.
23. As a user, I want terminal text to remain compact, so that increasing general typography does not reduce useful log density.
24. As a user, I want the displayed version to match the installed build, so that support and diagnostics are based on accurate information.
25. As a user, I want language and version controls visually separated, so that the bottom of the navigation sidebar has a clear hierarchy.
26. As a maintainer, I want navigation behavior tested without constructing or running the WPF `App`, so that unit tests do not open GUI windows in CI.
27. As a maintainer, I want XAML compilation to remain part of the build, so that invalid resources and bindings that can be caught statically still fail verification.
28. As a maintainer, I want visual layout verification to be an explicit capture operation rather than a unit-test side effect, so that CI behavior remains predictable.

## Implementation Decisions

- Model the presentation lifecycle separately from the client-tool detection result. The presentation state covers not checked, checking, ready, incompatible, and not found; a completed core detection result continues to represent an actual probe outcome rather than an in-progress UI state.
- Show the all-tools-ready detail only in the ready presentation state. Not-checked and checking states use neutral and informational styling rather than success or failure styling.
- Persist only a custom client-tool directory that has completed a successful detection. Store this non-secret, machine-specific preference per Windows user in local application data rather than roaming it to other machines.
- Load the saved custom directory before the startup probe. If the directory later becomes invalid, retain it in the editor and present the new detection failure.
- Keep the existing PostgreSQL and EDB installer links and add the official EDB Windows binaries archive page as a separate portable-download action.
- Remove fixed maximum widths from the settings, backup, and restore surfaces. Cards stretch within the content margin, while internal grids retain useful multi-column groupings so individual controls do not become visually incoherent on wide displays.
- Replace stack-only backup and restore page composition with a grid-based layout in which operation configuration consumes its required height and the terminal consumes remaining height. Preserve a vertical scrolling fallback and a terminal minimum height for short windows.
- Use a subtle neutral or pale-green selected-row surface in the history grid without overriding the foreground or background of semantic badges and action buttons.
- Give the database column the flexible share of remaining history-grid width. Size the actions column to its content at the far right, retain minimum widths for non-wrapping cells, and use horizontal scrolling when the viewport is too narrow.
- Remove the global-log navigation destination, view, view model, dependency registration, localization strings, and duplicate forwarding from backup and restore operations. Per-operation terminal output and immutable backup records remain unchanged.
- Declare Per-Monitor V2 DPI awareness for the desktop executable and validate the surface at common Windows scale factors. Increase normal body and navigation typography by one step while preserving compact terminal typography.
- Read the visible application version from assembly informational version metadata, remove build metadata following `+`, preserve semantic prerelease identifiers, localize the version label, and render it in a lightly tinted footer region separated from the language selector.
- Replace application-driven navigation tests with a pure navigation state or routing seam whose initial destination is Settings and whose supported destinations match the remaining navigation entries.
- Unit tests must not construct the WPF `App`, call `Application.Run`, or display a `Window` through `Show` or `ShowDialog`.
- The original report remains source material; the normalized specification and tickets live in the repository's local Markdown issue tracker.

## Testing Decisions

- Test externally observable state transitions rather than private fields or XAML element names.
- At the settings view-model seam, verify not-checked initial presentation, checking presentation, each completed result, conditional ready copy, successful-path persistence, startup loading, and retained invalid saved paths.
- Exercise preference persistence through an injected store with an isolated temporary location or in-memory fake; tests must never depend on a developer's real application-data directory.
- At the navigation seam, verify the Settings initial destination and every supported transition without constructing or running the WPF application.
- Update backup and restore operation-feedback tests to assert their own live terminal output after global-log forwarding is removed.
- Keep history column behavior and selected-row styling in XAML and verify that XAML compiles. Use the existing explicit UI capture capability for bounded visual inspection at representative large and small window sizes; it is not invoked by unit tests.
- Verify version normalization as a pure value transformation, including stable, prerelease, and build-metadata forms.
- Run the complete solution build and automated test suite after implementation. A passing test run must not show an application window.
- Existing view-model tests using mocked services are the preferred prior art. The current application-running WPF fixture is explicitly not prior art and will be removed.

## Out of Scope

- Changes to the CLI interface or scheduled-task behavior.
- Changes to `pg_dump`, `pg_restore`, or `psql` argument construction and execution.
- Changes to connection-profile credential storage or its security boundary.
- Changes to backup-file formats, restore semantics, or the pre-restore snapshot requirement.
- Changes to the backup-history database schema or persistence policy.
- Persisting global logs or replacing them with a new logging subsystem.
- A complete visual redesign or replacement of the existing emerald visual identity.
- Automatic download, extraction, installation, or update of PostgreSQL client tools.

## Further Notes

- The source report is `issue/20260919.md` and its accompanying screenshots.
- The portable-download target is EDB's official PostgreSQL Windows binaries archive page.
- The glossary definition of 工具偵測狀態（Tool Detection Status）has been expanded to include 尚未檢查 and 檢查中.
- No ADR is required: the decisions are scoped UI and preference behaviors, are straightforward to reverse, and do not introduce a surprising long-lived architectural constraint.
