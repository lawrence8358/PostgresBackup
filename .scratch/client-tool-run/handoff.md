# 交接：客戶端工具作業重構

寫給接手實作的人。**先讀這份，再讀 `spec.md`，然後從 `issues/01` 開始。**

本文件只寫「規格與票裡沒有、但你需要知道」的事。設計決策與理由一律在 `spec.md`，不在這裡重複。

---

## 1. 這批工作在做什麼（三句話）

`BackupService` 與 `RestoreService` 各自實作了同一套「執行一次客戶端工具」的流程，五件事各有一份複本，而且複本已經不一致地分岔。

我們要把那五件事抽成一個叫「客戶端工具作業（Client Tool Run）」的模組，兩個服務共用。

分三步做，每步結束時建置與測試都必須是綠的。

## 2. 現在的狀態

### 已完成並通過驗證

**票 06 的擋板部分**已經做完（`Status: resolved`）。內容是：備份與還原收到帶有 `ConnectionString` 的連線設定時直接回傳失敗。

這件事本來是排在後面的，但討論中發現分歧存在於單次還原作業**內部**（檢查與清空目標資料庫走連線字串、`pg_restore` 走分開欄位），涉及清空資料表這個破壞性動作，所以提前處理。詳見 `issues/06`。

### 尚未開始

票 01、02、03 —— 也就是這批工作的主體。全部 `Status: ready-for-agent`。

### 工作區有未提交的變更（重要）

接手時請先跑 `git status`。截至交接時，工作區有兩類未提交的東西：

1. **本批工作產生的** —— `.scratch/client-tool-run/` 整個目錄、`CoreStrings.resx` 與 `CoreStrings.zh-TW.resx` 各兩則新字串、`BackupService.cs` 與 `RestoreService.cs` 的擋板、新增的 `ConnectionStringRejectionTests.cs`、`docs/agents/issue-tracker.md` 的 slug 索引。
2. **不是本批工作產生的** —— `src/PostgresBackup.Wpf/Views/` 底下三個 XAML 檔（`BackupView`、`RestoreView`、`SettingsView`）各有一處版面微調（`VerticalAlignment="Center"` 之類），在本批工作開始之前就已經在工作區裡。**不是我們改的，提交時要自行決定去留。**

## 3. 怎麼跑

```
dotnet build     # 必須 0 警告 0 錯誤
dotnet test      # 截至交接時 192 個測試全綠（Core 96、CLI 52、WPF 44）
```

**0 警告是硬標準**，不是建議。這個專案先前維持在 0 警告，本批工作也維持住了。

若只想跑某個測試類別：

```
dotnet test test/PostgresBackup.Core.Tests --filter "FullyQualifiedName~<類別名>" --logger "console;verbosity=normal"
```

加 `--logger` 是為了確認測試**真的執行**而不是被略過 —— 這個專案有過測試在特定環境下被靜默略過的前例（見 `.scratch/secure-scheduled-credentials/issues/11`），養成確認的習慣。

## 4. 做事順序

| 票 | 內容 | 相依 |
| --- | --- | --- |
| `issues/01` | 建立客戶端工具作業模組 + 測試 + `CONTEXT.md` 詞彙 + `IProcessRunner` 註解 | 無 |
| `issues/02` | `BackupService` 改用它，`BackupArgumentsBuilder` 改回傳 argv 清單 | 01 |
| `issues/03` | `RestoreService` 改用它，`RestoreArgumentsBuilder` 同上 | 01, 02 |

票 04（作業無法中止）、05（`ProcessRunner` 零測試）是延後項目，標 `needs-triage`，**不在這批範圍內**，需要先跟使用者討論才能動（票裡列了待決定的問題）。

票 06 的「剩餘工作」（型別重整）刻意排在 01–03 之後，原因寫在票裡。

## 5. 容易踩的坑

### 5.1 不要順手改隔壁

三張票裡都寫了「不要順手做的事」。最容易被順手改掉的是這四樣，**全部屬於另一個候選方案（候選 2）的範圍，請原樣保留**：

- `RestoreArgumentsBuilder` 裡兩個 `InvalidOperationException`（它們是靠 `RestoreService` 保證永不觸發的，而 `RestoreService` 並沒有攔它 —— 這是真問題，但不是這批要解的）
- `RestoreAsync` 那個 340 行的方法本身
- `RestoreService` 直接改寫呼叫端傳入的 `options.Format`
- 暫存 `.list` 檔的生命週期

順手改會讓 diff 裡的行為變化分不清是重構造成的還是額外改動造成的。

### 5.2 既有測試不准調斷言硬過

21 個既有測試會被改寫，但它們驗證的行為在改動後**應該全部依然成立** —— 改的是提問方式（比字串 → 比 argv 清單），不是提問內容。

**任一既有測試若改寫後無法成立，代表引入了行為變更。停下來回報，不要調整斷言讓它通過。** 哪些測試、各自驗證什麼、怎麼改，`spec.md` 的「Testing Decisions」與各張票裡都列了表。

### 5.3 唯一預期的行為變化

argv 拼接規則改變後，不含空白的值不再加引號（`-h "localhost"` → `-h localhost`）。命令列語意相同，但 `BackupRecord.Arguments` 存下的字串外觀會變，歷史紀錄詳情頁顯示的內容因此略有不同。

這是 `spec.md` 已明確接受的結果，不是缺陷。既有歷史紀錄不需要遷移。

### 5.4 `RestoreServiceTests.TestProcessRunner` 會壞

它目前靠 `arguments.IndexOf("-f \"")` 反解出快照檔路徑。拼接規則改了以後，不含空白的路徑不再帶引號，這段會失效。**兩種情形都要能處理**（測試用的暫存路徑可能含空白也可能不含），不要只針對其中一種修補。

### 5.5 `?.` 會引入 nullable 警告

`BackupOptions.Connection` 與 `RestoreOptions.Connection` 都是非可空且有預設值。在它們身上用 `?.` 會讓編譯器從該處起把整條路徑視為可能為 null，後面每個取值都冒一個 CS8602。本批工作已經踩過一次，別再踩。

## 6. 已驗證 vs 未驗證的事實

這批工作的前期調查有部分來自子代理，**不是全部都經過第一手確認**。動手前請自行驗證你要依賴的部分。

### 已親自逐行看過程式碼確認

- 五項重複（環境變數、連線參數、`Escape` 三份、計時與結果判定、四段歷史紀錄寫入）
- `ProcessResult.ErrorMessage` 的死碼問題
- `RestoreService` 的安全快照會透過 `IBackupService` 留下 `PreRestoreSnapshot` 紀錄，因此一次還原留兩筆紀錄（刻意行為）
- 兩個 Npgsql 還原轉接器確實透過 `ToConnectionString()` 開連線
- `ConnectionProfile` 沒有連線字串欄位
- `--connection-string` 只存在於 `check-tools`，且手冊明載僅供互動式除錯
- 21 個受影響測試的檔案與數量

### 尚未第一手驗證（來自子代理回報，請自行確認）

- WPF `_isStatusIdle` 從不設回 `true`，導致作業跑過一次之後切換語系狀態面板不再重新在地化。**這是被報告為真 bug 的項目，屬候選 4，不在本批範圍，但若你要據此做任何事請先確認。**
- CLI 三個指令之間的連線參數優先序分岔（`--host ""` / `--port 0` 在 `check-tools` 行為不同）
- `RestoreCommand` 的 log 開檔用 `catch { }` 靜默吞掉、`BackupCommand` 卻會警告

### 已確認為錯誤的說法

子代理曾回報 `RestoreOptions.DetectFormatFromFilePath` 是無人呼叫的死碼。**這是錯的** —— `RestoreCommand` 與 `RestoreViewModel` 都會呼叫它。真正的問題是 `RestoreService` 用 `EndsWith` 重新實作了同一條規則，並直接改寫呼叫端傳入的 `options.Format`。此項屬候選 2，不在本批範圍。

## 7. 專案慣例

不要另外發明流程，這個 repo 已經有了：

- `AGENTS.md` —— 三項慣例的入口
- `docs/agents/issue-tracker.md` —— 議題以 markdown 檔存在 `.scratch/<feature-slug>/`，**不使用 GitHub Issues**；完成的票標 `resolved` 並保留打勾的檢查清單，讓它繼續作為「做了什麼」的紀錄
- `docs/agents/triage-labels.md` —— 五個標準分流標籤
- `CONTEXT.md` —— 領域詞彙表。票 01 要在這裡新增「客戶端工具作業」詞條，內容已寫在 `spec.md` 裡
- `docs/adr/0001-cli-machine-scoped-credential-store.md` —— 唯一一份 ADR。本批工作不牴觸它，但若你的改動碰到連線設定的存放或密碼處理，先讀它

**語言**：這個 repo 的規格、票、註解、使用者可見訊息全部是繁體中文。新增的資源字串要中英各一份（`CoreStrings.resx` 與 `CoreStrings.zh-TW.resx`）。

## 8. 建議使用的 skills

若接手的是 AI 代理，呼叫 Skill 工具：

1. **`codebase-design`** —— 先載入。這批工作全程使用它的詞彙（模組、介面、深度、接縫、轉接器、槓桿、區域性）與原則（刪除測試、「介面就是測試面」、「一個轉接器是想像的接縫，兩個才是真的」）。`spec.md` 裡「不為模組設介面」那個決定，直接建立在最後那條原則上 —— 不懂那條原則就會覺得那個決定很怪。
2. **`tdd`** —— 票 01 很適合先寫測試。`spec.md` 的「新增測試」一節已經把該覆蓋的十項列出來了，等於測試清單現成。
3. **`code-review`** —— 每張票做完跑一次，對照本專案標準與 `spec.md` 檢查。
4. **`domain-modeling`** —— 票 01 要動 `CONTEXT.md` 時。

**不需要**重跑 `improve-codebase-architecture` —— 架構檢視已經做完，結論就是這份規格。

## 9. 這批工作之外還有什麼

同一次架構檢視提出六個候選，本批只做候選 3。其餘五個**未被否決，只是未排程**，清單與各自的問題摘要在 `spec.md` 的「Out of Scope」末段。

若之後要接著做，建議順序是候選 2（還原計畫模組）→ 候選 1（CLI 外殼）→ 候選 6（破壞性操作的確認關卡）。候選 2 排第一是因為本批完成後 `RestoreService` 會變薄，拆解它的難度下降。
