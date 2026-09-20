# Spec：以「客戶端工具作業」收攏備份與還原的重複執行流程

Status: resolved

本規格源自 2026-09-20 的架構檢視。檢視報告列出六個深化候選，本規格只實作其中的候選 3；其餘候選未被否決，僅是尚未排程。

## Problem Statement

`BackupService` 與 `RestoreService` 各自實作了同一套「執行一次客戶端工具」的流程。五件事在兩個檔案中各有一份複本：

1. **環境變數政策** —— `PGPASSWORD`、`PGCLIENTENCODING=UTF8`、`LC_ALL` / `LC_MESSAGES` / `LANG` 設為 `C`、`LANGUAGE` 設為 `null`。`RestoreService` 把它收進 `BuildEnvironmentVariables`，`BackupService` 則直接寫在 `BackupAsync` 中段。兩份內容逐字相同。
2. **連線參數組裝** —— `-h "..." -p N -U "..." -d "..."`，含 `localhost` / `5432` / `postgres` 三個後備值。`BackupArgumentsBuilder` 與 `RestoreArgumentsBuilder` 各一份，差別只在還原會優先採用 `TargetDatabase`。
3. **引號跳脫** —— `value.Replace("\"", "\\\"")` 在 `BackupArgumentsBuilder`、`RestoreArgumentsBuilder`、`RestoreService` 三處各有一份私有實作。
4. **計時與結果判定** —— `Stopwatch.StartNew()` 開頭、`Stop()` 於分支前、`ExitCode == 0` 判斷、錯誤訊息擷取。
5. **備份紀錄寫入** —— 四段結構相同的 `_historyRepo?.AddRecordAsync(new BackupRecord { ... })`，包在 try/catch 中。

這套重複造成三個具體後果：

**後果一：複本已經不一致地分岔。** 四段紀錄寫入的例外處理有兩種：`BackupService` 成功分支會輸出 `[WARNING]` 記錄，另外三段（`BackupService` 失敗分支、`RestoreService` 兩個分支）都是 `catch { }` 靜默吞掉。稽核紀錄寫不進去時，使用者有一半機率完全不會知道。

**後果二：`IProcessRunner` 的隱形約定被迫在每個呼叫端重新推導。** 該介面只有 24 行，但呼叫端必須另外知道：環境變數字典中 `null` 值代表「刪除該變數」（`ProcessRunner` 的行為，兩個服務都依賴它處理 `LANGUAGE`，但介面上沒有任何說明）；`arguments` 參數是**已經跳脫完成的原始命令列**而非參數陣列，所以每個呼叫端都得自備跳脫函式。第 3 項的三份複本正是這個約定的直接產物。

**後果三：錯誤訊息擷取是死碼，而且兩邊都錯得一樣。** 兩個服務都寫了：

```csharp
var err = !string.IsNullOrWhiteSpace(processResult.StandardError)
    ? processResult.StandardError
    : processResult.ErrorMessage ?? CoreStrings.Get("..._Error_NonZeroExit");
```

但 `ProcessResult.ErrorMessage` 的定義就是 `!string.IsNullOrWhiteSpace(StandardError) ? StandardError : null`。因此當 `StandardError` 為空時 `ErrorMessage` 必然為 `null`，中間那個運算元永遠不可能貢獻任何值。兩個服務都重新實作了模型已經做過的判斷，而且都判斷錯了。

## Solution

建立一個名為**客戶端工具作業（Client Tool Run）**的模組，由它負責「執行一次客戶端工具並留下備份紀錄」的完整流程。`BackupService` 與 `RestoreService` 改為向它下單，各自只保留自己專屬的部分（備份的輸出目錄處理與檔案存在判定、還原的計畫產出與安全快照協調）。

關鍵設計是**參數以 argv 元素清單傳遞，而非預先跳脫的字串**。兩個參數構建器不再產生 `-Fc -n "public"` 這種字串，改為產生 `["-Fc", "-n", "public"]`。引號處理由客戶端工具作業在最後一刻統一執行，三份 `Escape` 複本因此收斂為一份，而 `IProcessRunner` 那條「呼叫端自行跳脫」的隱形約定在這一層之上完全消失。

客戶端工具作業**不設介面**。正式環境只有一種實作，而測試所需的抽換點是它底下既有的 `IProcessRunner`（該接縫已由 `ToolDetectionService` 與現有測試共同使用，是真實存在的變異點）。為此模組新增介面只會產生一個僅供測試使用的空殼，與本次檢視同時提出的「撤除假接縫」方向矛盾。

## User Stories

1. As a maintainer, I want the environment-variable policy for client tools defined in exactly one place, so that a change to locale or encoding handling cannot apply to backup but miss restore.
2. As a maintainer, I want argument escaping implemented once, so that a path containing a quote character behaves identically on every code path.
3. As a maintainer, I want argument builders to declare *what they are asking for* rather than *how the command line should look*, so that quoting bugs cannot originate in them.
4. As a maintainer, I want the four history-record write sites collapsed into one, so that the inconsistency between warning and silent swallow cannot recur.
5. As a maintainer, I want a single failure result shape from tool execution, so that a caller does not have to handle both a thrown exception and a sentinel exit code for the same class of failure.
6. As an operator, I want to be told when an audit record fails to persist, so that I do not later discover a gap in the backup history without explanation.
7. As an operator, I want a backup whose file was written successfully to still be reported as successful even if the history database is unavailable, so that I do not repeat an expensive backup unnecessarily.
8. As a maintainer, I want the concept of "one client-tool run" named in `CONTEXT.md`, so that the codebase and the glossary agree on what this module is.
9. As a maintainer, I want the migration split into independently verifiable steps, so that a failing test after a step identifies which service introduced it.
10. As a maintainer, I want the existing behavioural assertions preserved through the migration, so that the refactor is verifiable rather than merely plausible.

## Implementation Decisions

### 模組範圍

客戶端工具作業吃下的範圍包含：環境變數組裝、連線參數組裝、argv 跳脫與拼接、處理序執行、計時、成功失敗判定、錯誤訊息擷取、備份紀錄寫入。

不包含：客戶端工具偵測（仍由呼叫端先行取得工具路徑）、輸出目錄建立、產出檔案存在判定、還原計畫產出、安全快照協調。這些是備份或還原各自專屬的責任。

決定納入備份紀錄寫入的理由：若不納入，四段 `AddRecordAsync` 區塊仍會留在兩個服務中繼續分岔。複雜度被搬動而非集中，不構成深化。

### 參數傳遞採 argv 元素清單

兩個參數構建器的回傳型別由 `string` 改為 `IReadOnlyList<string>`，每個元素對應一個 argv 元素：

```csharp
// 之前
"-h \"localhost\" -p 5432 -U \"postgres\" -d \"mydb\" -Fc -n \"public\" -v -f \"C:\\out\\x.dump\""

// 之後（構建器只回傳作業專屬部分，連線前綴由模組補上）
["-Fc", "-n", "public", "-v", "-f", @"C:\out\x.dump"]
```

客戶端工具作業負責在前方接上連線參數（`-h`、`-p`、`-U`、`-d` 及其值），再將完整清單拼接為 `ProcessStartInfo` 所需的單一字串。

**拼接規則：** 元素為空字串、或包含空白字元或雙引號時加上雙引號並跳脫內部雙引號；否則原樣輸出。

**已知的外觀變化：** 此規則會讓 `-h localhost`、`-d mydb` 這類不含空白的值不再帶引號（現行實作一律加引號）。命令列語意完全相同，但 `BackupRecord.Arguments` 欄位存下的字串外觀會改變，歷史紀錄詳情頁顯示的內容因此略有不同。此為刻意接受的結果：拼接規則應由「這個值需不需要引號」決定，而非由呼叫端的習慣決定。既有歷史紀錄不受影響，不需遷移。

### 實作後補記的行為變化

除了上述引號外觀之外，實作過程中另有兩項行為變化，於此登記：

**一、計時範圍收斂為單次工具執行。** 計時移入模組後，`BackupResult.Duration` 不再包含客戶端工具偵測的時間，`RestoreResult.Duration` 不再包含計畫產出、還原前安全快照與清空資料表的時間。還原的影響較大 —— 安全快照動輒數分鐘。

判定為可接受且較正確：快照本身已經留下自己的一筆紀錄與自己的耗時，舊行為等於把同一段時間同時計入兩筆紀錄；新行為讓兩筆紀錄的耗時可以相加。既有歷史紀錄不受影響。

計畫階段失敗的早期返回，耗時改為取自 `--list` 那次探查；快照之後、工具執行之前的失敗（暫存清單檔寫入、清空資料表）改回報 `TimeSpan.Zero`，與同類的前三個早期返回一致。

**二、紀錄寫入警告的輸出順序。** 紀錄寫入移入模組後發生在服務印出 `[SUCCESS]` / `[ERROR]` 之前，因此 `[WARNING]` 寫入失敗那一行的位置由其後移至其前。僅影響日誌閱讀順序。

### 不為模組設介面

理由見 Solution 一節。測試透過抽換 `IProcessRunner` 達成，`BackupServiceTests.FakeProcessRunner` 既有的環境變數擷取能力（`CapturedPassword`、`CapturedLanguage`、`LanguageVariableRemoved` 等）可直接沿用。

若未來出現第二種實作的真實需求（例如遠端執行客戶端工具），屆時再引入介面，此時才會有兩個轉接器支撐它。

### `IProcessRunner` 維持現狀

不改動其接縫位置，因為 `ToolDetectionService` 仍是它的第三個呼叫端，拆動會波及不相干的範圍。但需在介面上補齊文件，明確記載兩項既有約定：

- 環境變數字典中的 `null` 值代表「自子處理序環境中移除該變數」。
- `arguments` 為已完成跳脫的完整命令列字串，非參數陣列。

本次改動後，Core 中唯一直接面對這兩項約定的呼叫端將是客戶端工具作業與 `ToolDetectionService`。

### 失敗語意收斂

現行 `ProcessRunner` 的行為是：取消丟出 `OperationCanceledException`，其餘例外轉為 `ProcessResult(-1, ...)`。`RestoreService` 將 `RunAsync` 包在 `try/finally` 中但不含 `catch`，因此 runner 丟出例外時不會寫入任何紀錄，回傳 `-1` 時則會寫入一筆 Failed —— 同類失敗兩種結局。

客戶端工具作業統一為：**任何非取消的失敗都產生一致的失敗結果，且紀錄必定嘗試寫入**。`OperationCanceledException` 維持向上傳播，因為取消不是失敗。

### 錯誤訊息擷取

改為單一實作，並修正死碼：`StandardError` 有值即採用，否則採用對應的 `..._Error_NonZeroExit` 資源字串。不再經由 `ProcessResult.ErrorMessage`（該屬性的語意與 `StandardError` 重複，於此處無法貢獻任何值）。

資源鍵仍需區分備份與還原，故由呼叫端傳入。

### 備份紀錄寫入失敗的統一行為

統一為：**輸出一行 `[WARNING]` 記錄，但不改變作業結果**。

`BackupService` 成功分支現有的 `Backup_Log_HistoryWriteFailed` 資源字串可直接沿用於所有情形。全部靜默吞掉會讓稽核缺口無聲發生，而本工具的價值有相當部分建立在「事後查得到誰在何時動了哪個資料庫」；反之，將其視為作業失敗則會讓使用者為了一個已經成功落地的備份檔重跑一次備份。

### 結果型別維持分離

`BackupResult` 與 `RestoreResult` 不合併。兩者的消費端（`BackupCommand`、`RestoreCommand`、`BackupViewModel`、`RestoreViewModel`）實際使用的成員為 `IsSuccess`、`ErrorMessage`、`OutputFilePath`（備份）、`SnapshotCreated` / `SnapshotFilePath`（還原），形狀確實不同。合併會迫使呼叫端判斷哪些欄位在本次有意義，屬於把介面變寬。

客戶端工具作業的回傳結果為模組內部型別，不外露至 Core 之外。

### 還原前安全快照維持走 `IBackupService`

`RestoreService` 繼續呼叫 `IBackupService.BackupAsync` 產生快照，不改為直接使用客戶端工具作業。

理由：`CONTEXT.md` 將「還原前安全快照」定義為一次備份作業，`BackupOperationType` 亦為其保留了獨立的列舉值。走備份服務才能一併獲得輸出目錄建立、產出檔案存在判定，以及那筆 `PreRestoreSnapshot` 紀錄。現行「一次還原留下兩筆紀錄（快照一筆、還原一筆）」的行為為刻意設計，本次不變更。

### 同步修正的既有缺陷

本次一併修正兩項落在改動範圍內、且不改變對外可觀察行為的缺陷：

- 四段紀錄寫入的例外處理不一致（見上）。
- 錯誤訊息擷取的死碼（見上）。

不在本次修正、另立票追蹤的缺陷見 Out of Scope。

### `CONTEXT.md` 詞彙新增

於「備份與還原」一節新增：

> **客戶端工具作業（Client Tool Run）**：
> 執行一次客戶端工具的完整過程，涵蓋連線參數組裝、環境變數設定、處理序執行、計時，以及該次執行所留下的備份紀錄。備份作業與還原作業皆透過它抵達 `pg_dump`、`pg_restore` 與 `psql`。
> _Avoid_: 處理序執行, 工具呼叫, 執行器

### 實作切分

分三步，每一步結束時建置與測試皆須為綠：

1. **票 01** —— 建立客戶端工具作業與其測試，新增 `CONTEXT.md` 詞彙，補齊 `IProcessRunner` 的約定文件。此時尚無呼叫端，既有行為完全不變。
2. **票 02** —— `BackupService` 改用之，`BackupArgumentsBuilder` 同步改為回傳 argv 清單。
3. **票 03** —— `RestoreService` 改用之，`RestoreArgumentsBuilder` 同步改為回傳 argv 清單。

**與檢視當下口頭規劃的差異：** 逼問階段曾將「兩個構建器改為回傳片段」一併歸入步驟 1。實際上構建器的回傳型別一旦改變，其呼叫端（兩個服務）必須同步調整才能通過編譯，因此構建器的變更改為隨各自的服務一起進行。三步的切分與驗證意義不變。

## Testing Decisions

### 既有測試的處理

受影響的測試共 21 個，全部**改寫而非刪除** —— 它們驗證的行為在改動後依然成立，改變的是提問方式而非提問內容：

| 檔案 | 數量 | 改寫內容 | 票 |
| --- | --- | --- | --- |
| `BackupArgumentsBuilderTests` | 4 | 斷言對象由命令列字串改為 argv 清單 | 02 |
| `BackupServiceTests` | 3 | 假的 `IProcessRunner` 擷取到的參數形式改變 | 02 |
| `RestoreArgumentsBuilderTests` | 5 | 同上（含兩個 `InvalidOperationException` 斷言，行為不變） | 03 |
| `RestoreServiceTests` | 8 | 同上 | 03 |
| `RestoreTargetDatabaseTests` | 1 | `--clean --if-exists` 的斷言改為比對 argv 元素 | 03 |

任一既有測試若在改寫後無法成立，視為本次改動引入了行為變更，須停止並回報，不得調整斷言使其通過。

### 兩個手寫的處理序替身各自保留

`BackupServiceTests.FakeProcessRunner`（擷取環境變數）與 `RestoreServiceTests.TestProcessRunner`（模擬檔案產出）不合併。兩者擷取的目標不同，合併會產生一個什麼都要做的替身，且屬於測試專案的整理工作，與本規格的目標不同，混入會讓變更難以審閱。

`TestProcessRunner` 現行需從命令列字串中反解 `-f "..."` 才能得知要在何處產生假的快照檔：

```csharp
var fileArgIndex = arguments.IndexOf("-f \"", StringComparison.Ordinal);
```

改動後此處仍面對拼接完成的字串（替身實作的是 `IProcessRunner`，位於拼接之後），但因拼接規則不再對無空白的值加引號，此反解需相應調整。若替身改為在客戶端工具作業之上攔截則可完全免除反解 —— 本次不採用，因為那需要為模組設介面。

### 新增測試

客戶端工具作業本身須有直接測試，涵蓋：

- 密碼存在時 `PGPASSWORD` 被設定；密碼為空時不設定該變數。
- `PGCLIENTENCODING`、`LC_ALL`、`LC_MESSAGES`、`LANG` 的值，以及 `LANGUAGE` 以 `null` 傳遞（即要求移除）。
- 連線參數前綴的組裝，含 `localhost` / `5432` / `postgres` 三個後備值的生效條件。
- 還原情形下 `TargetDatabase` 優先於 `Connection.Database`。
- argv 拼接與引號規則：含空白的值加引號、不含空白的值不加、值內含雙引號時正確跳脫、空字串加引號。
- 成功時寫入 `BackupStatus.Success` 紀錄，失敗時寫入 `BackupStatus.Failed` 紀錄且帶有錯誤訊息。
- 歷史倉儲拋出例外時，輸出警告記錄且作業結果不受影響。
- `IBackupHistoryRepository` 為 `null` 時不拋例外。
- 處理序回傳非零離開碼時，錯誤訊息取自 `StandardError`；`StandardError` 為空時取自傳入的資源鍵。
- `OperationCanceledException` 向上傳播而非轉為失敗結果。

### 不新增整合測試

本規格不要求任何需要實際 PostgreSQL 伺服器或實際外部程序的測試。`ProcessRunner` 的測試缺口另立票追蹤（見 Out of Scope）。

## Out of Scope

以下三項於架構檢視中被發現，刻意排除於本次之外，各自立票：

- **票 04 —— 長時間備份與還原作業無法中止。** `IBackupService.BackupAsync` 與 `IRestoreService.RestoreAsync` 皆接受 `CancellationToken`，但四個呼叫端（兩個 ViewModel、兩個 CLI 指令）全部省略不傳。屬功能缺口而非架構摩擦，型別上的接縫早已存在。混入本次會讓同一批變更同時是重構與新功能。
- **票 05 —— `ProcessRunner` 沒有任何測試。** 它是 Core 中唯一實際操作 `Process`、串流讀取、`Kill(entireProcessTree)` 的位置，也是「環境變數傳 `null` 即移除」這項約定的唯一實作處。補測需要實際執行外部程序，性質上屬整合測試。
- **票 06 —— 連線字串在備份與還原路徑被完全忽略。** `ConnectionSettings.ConnectionString` 目前僅由 `check-tools` 指令設定，且只餵給 `CheckCompatibilityAsync`。兩個參數構建器完全不讀取它。目前不是活的缺陷（備份與還原沒有接受連線字串的入口），但屬設計層面的陷阱。

  **本項已先行處理。** 2026-09-20 的討論中發現分歧其實存在於單次還原作業**內部**（目標資料庫的檢查與清空走 `ToConnectionString()`、`pg_restore` 走分開欄位），涉及清空資料表這個破壞性動作，因此嚴重度上調並立即加上執行期守門：備份與還原收到帶有 `ConnectionString` 的連線設定時直接回傳失敗。詳見票 06。

  票 06 的**剩餘工作**（把 `ConnectionSettings` 改為兩種互斥的連線描述方式，使該狀態在型別上無法表示）刻意延後至票 01–03 之後 —— 屆時連線參數組裝已收攏進客戶端工具作業，`ToConnectionString()` 的呼叫端減少，重整範圍較小。

以下候選來自同一次架構檢視，未被否決，僅未排程，不在本規格範圍內：

- 候選 1：折疊命令列的作業外殼（三個 CLI 指令的重複與已發生的行為分岔）。
- 候選 2：把還原作業收成一個深模組（`RestoreAsync` 的 340 行、暫存清單檔生命週期、`RestoreArgumentsBuilder` 中兩個由呼叫端負責永不觸發的 `InvalidOperationException`）。
- 候選 4：桌面作業面板（兩個 ViewModel 的重複，含 `_isStatusIdle` 未重設導致作業後語系切換不再重新在地化的疑似缺陷，尚未經第一手驗證）。
- 候選 5：撤除 `IRestoreTargetCatalogReader` 與 `IRestoreDataPreparationService` 兩條僅供測試存在的接縫。
- 候選 6：為破壞性操作的確認關卡建立接縫（CLI 的 `Console.ReadLine` 與 WPF 的 `MessageBox.Show`）。

`RestoreArgumentsBuilder` 中的兩個 `InvalidOperationException` 於本次**原樣保留**，包含其既有測試。它們屬候選 2 的核心，在此順手改動會使兩件事混雜；且正確的修法是改變參數傳遞的方式，而非將 `throw` 換成回傳值。

## Further Notes

- 架構檢視報告為暫存產出，未簽入版控。本規格的 Problem Statement 已涵蓋其中與本次相關的全部論據。
- 本規格不牴觸 `docs/adr/0001-cli-machine-scoped-credential-store.md`。該 ADR 規範的是命令列與圖形介面兩套連線設定的存放位置與隔離方式，與客戶端工具的執行流程無涉。
- 檢視過程中曾誤判 `RestoreOptions.DetectFormatFromFilePath` 為無人呼叫的死碼。實際上 `RestoreCommand` 與 `RestoreViewModel` 皆會呼叫它。真正的問題是 `RestoreService` 以 `EndsWith` 重新實作了同一條規則，並直接改寫呼叫端傳入的 `options.Format`（`RestoreOptions` 為可變類別，`IRestoreService` 在兩個進入點皆註冊為 `AddSingleton`）。此問題屬候選 2 的範圍，本次不處理。
