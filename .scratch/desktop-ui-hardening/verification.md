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
