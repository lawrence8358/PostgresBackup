# 12: 程式碼審查後續項目

**What to build:** 處理 `/code-review` 對票 01–09 提出、但不屬於任何既有票面範圍的品質與正確性問題。

**Blocked by:** 11（避免與其安全修正在同一批檔案上衝突）

**Status:** resolved

審查結論：九張票的勾選項目逐條核對**無一未實作**，使用者拍板的十條前提全數遵守。以下為額外發現，依價值排序。前四項有實質影響，建議優先。

## 建議優先處理

- [x] **`VerifyConnectionAsync` 的記錄器繞過 `Redact`**
      `src/PostgresBackup.Core/Services/ToolDetectionService.cs`（`VerifyConnectionAsync` 的 catch）
      `ProfileCommand` 用 `Redact(verification.Message, password)` 避免回吐密碼，但此處的 `_logger.LogError(ex, ...)` 會讓完整例外（含訊息與堆疊）先被印到主控台（CLI 記錄器最低層級為 `Warning`），`Redact` 管不到。目前 `ToConnectionString()` 使用 `NpgsqlConnectionStringBuilder`（會正確跳脫），實際回吐密碼機率低，但這讓 `Redact` 成為有缺口的防線；且使用者打錯密碼這種家常錯誤會看到一整段堆疊，體驗也差。
      → 改為 `LogDebug` 或只記錄 `ex.GetType().Name`，並確認 `CheckCompatibilityAsync` 同一路徑不會回吐連線字串。

- [x] **存取被拒被折疊成「沒有設定」「找不到設定」的誤導診斷**
      `JsonConnectionProfileRepository.LoadInternalAsync` 與 `MachineScopedCredentialStorage` 讀取加密檔處的 catch-all
      新存放區刻意只有 Administrators 與 SYSTEM 可讀，所以「非管理員執行」是**常態**情境。目前一般使用者執行 `pgbackup profile list` 會看到「目前沒有任何命令列連線設定」，執行 `backup --profile prod` 會看到「找不到名為 'prod' 的連線設定檔」——兩者都是錯誤診斷，會讓人去重建其實已經存在的設定。
      → 讓 `UnauthorizedAccessException` 穿透（或轉為專屬例外），並在 CLI 各 profile 查找處顯示「權限不足，請以系統管理員身分執行」，退出碼非零。注意 WPF 也用 `JsonConnectionProfileRepository`，須確認 GUI 不會因此炸掉。

- [x] **憑證檔非原子寫入，損毀時所有密碼靜默變成「遺失」**
      `MachineScopedCredentialStorage`：整份憑證為單一 JSON，以 `File.WriteAllText` 寫入；寫到一半斷電會留下截斷檔案，下次讀取時 `JsonException` 被吞掉 → 所有排程密碼同時變「遺失」且無任何說明。
      → 寫入暫存檔後以 `File.Replace` / `File.Move(overwrite: true)` 原子替換；`JsonException` 時明確讓使用者知道檔案已損毀。

- [x] **非受管記憶體中的明文未清零即釋放**
      `WindowsCredentialStorage.WriteToCredentialManager`：`finally` 有 `Array.Clear(passwordBytes)`（受管副本），但 `passwordPtr` 指向的**非受管**副本直接 `Marshal.FreeHGlobal`，內容不會被清除；註解卻宣稱明文不殘留於非受管記憶體。對應 spec User Story 20（降低記憶體被傾印時的暴露風險）。
      → `FreeHGlobal` 前先清零（`NativeMemory.Clear` 或寫入零值），並讓註解與實際行為相符。

## 其餘項目

- [x] **README 資料位置表的稽核歷史標示錯誤**
      兩份 README 的「Where Data Is Stored」／「資料儲存位置」表把備份歷史標為 "Both"／「兩者」，但 `SqliteBackupHistoryRepository` 使用 `SpecialFolder.LocalApplicationData`——以 SYSTEM 身分執行的排程備份會寫到 `C:\Windows\System32\config\systemprofile\AppData\Local\PostgresBackup\`，**不會**出現在圖形介面的歷史頁。這是本批變更推薦 SYSTEM 排程後帶來的新使用者意外。
      → 修正兩份 README 的表格，並在 `docs/USER_MANUAL.md` §5 補一句說明。下筆前請先確認 `SqliteBackupHistoryRepository` 的實際路徑。

- [x] **`check-tools --profile` 在密碼狀態為「遺失」時無提示**，會帶著 null 密碼去連線並得到較難解讀的驅動程式錯誤。

- [x] **DPAPI 額外熵值的註解高估其作用**
      `MachineScopedCredentialStorage`：熵值是公開原始碼中的常數，任何人都讀得到，因此「使此存放區的加密結果無法被其他程式以相同機器金鑰直接解開」只在「不知道這個常數的程式」成立。實際保護來自檔案 ACL（文件寫得正確）。
      → 註解語氣調整為「避免與其他使用機器金鑰的資料混淆」。

- [x] **`applyRestrictivePermissions` 是為測試而加在正式建構子上的參數**（票 11 引入），預設值讓它容易被忘記傳。`MachineScopedStoreLocation` 已是更好的表達方式。
      → 考慮讓 repository / credential storage 直接吃 `MachineScopedStoreLocation`，讓「位置 + 權限政策」永遠成對出現。

- [x] **沒有測試釘住「備份／還原不做權限檢查」**（票 06 最後一條）。行為是對的，且 backup/restore 的測試根本沒在 DI 註冊 `IEnvironmentProbe`（一旦有人加上檢查，`GetRequiredService` 會直接炸掉），算是隱含覆蓋——但這是巧合而非明示的防線。

## 註記

- 票 02 新增的真實認證管理員測試（`WindowsCredentialStorageTests` 的 `[WindowsOnlyFact]`）會實際寫入執行機器的認證管理員。非 Windows 自動略過、金鑰用 GUID、`finally` 有清理，屬可接受做法；僅提醒 Windows CI 上這幾個測試會變更機器狀態。
- 跨處理序沒有檔案鎖：`JsonConnectionProfileRepository` 的 `SemaphoreSlim` 與 `MachineScopedCredentialStorage` 的 `lock` 都只在行程內生效，而此存放區的設計前提就是「管理員的互動式行程」與「SYSTEM 排程行程」會同時存在。目前排程只讀不寫，風險低，暫不處理。

## Comments

### 2026-09-19 — 全數處理完成

九項全部處理。全案 135 個測試全綠（Core 70、CLI 52、WPF 13），建置 0 警告 0 錯誤。

#### 前四項（有實質影響）

1. **`VerifyConnectionAsync` 的記錄器繞過 `Redact`** — `LogError(ex, ...)` 改為 `LogDebug`，且只記錄 `ex.GetType().Name`，不記錄訊息與堆疊。失敗原因仍由回傳值帶給呼叫端，由呼叫端遮蔽後呈現。`CheckCompatibilityAsync` 走的是同一個方法，一併涵蓋。與票 14 合併處理，因此遮蔽同時覆蓋**顯示**與**記錄**兩條路徑。

2. **存取被拒被折疊成「沒有設定」「找不到設定」** — 新增 `ProfileStoreAccessDeniedException`，由 `JsonConnectionProfileRepository.LoadInternalAsync` 與 `MachineScopedCredentialStorage` 的讀取路徑拋出（其餘例外維持原本的寬容行為）。命令列端新增 `ProfileStoreAccess` 共用處理，接上 `profile set/list/remove`、`backup`、`restore`、`check-tools` 六個讀取點，一律印出「權限不足，請以系統管理員身分執行」並以非零退出碼結束。測試 `ProfileStoreAccessDiagnosticsTests` 針對五個指令逐一斷言「訊息含權限不足」且「不含『找不到名為』或『目前沒有任何命令列連線設定』」。
   WPF 讀的是使用者範圍的 `%APPDATA%`，該路徑本來就屬於執行者自己，實務上不會拋出此例外；`SettingsViewModel` / `BackupViewModel` / `RestoreViewModel` 的既有行為未受影響（WPF 13 項測試全綠）。

3. **憑證檔非原子寫入** — `SaveEntries` 改為先寫 `.tmp` 再以 `File.Move(overwrite: true)` 原子替換，替換失敗會清掉暫存檔。`LoadEntries` 的 `JsonException` 不再被吞成空字典，改拋 `CredentialStoreCorruptedException`；`SetPassword` 也不再在檔案損毀時覆蓋寫入（那會連救回來的機會都沒有）。測試 `MachineScopedCredentialStorageFileTests` 以真實的機器範圍加解密（不需管理員權限）驗證往返、只存密文、不留暫存檔、截斷檔案時讀寫皆明確報損毀。

4. **非受管記憶體中的明文未清零即釋放** — `WriteToCredentialManager` 在 `Marshal.FreeHGlobal(passwordPtr)` **之前**先以零值覆寫該區塊，並修正原本與行為不符的註解。未使用 `NativeMemory.Clear`（需 `unsafe`），改以 `Marshal.Copy` 寫入零陣列，避免為此開啟整個專案的 unsafe。

#### 其餘五項

5. **README 稽核歷史標示錯誤** — 已先確認 `SqliteBackupHistoryRepository` 確實使用 `SpecialFolder.LocalApplicationData`。兩份 README 的表格由「Both／兩者」改為「依 Windows 帳號各自獨立」，並補上說明區塊點出 SYSTEM 排程會寫到 `C:\Windows\System32\config\systemprofile\AppData\Local\`、不會出現在圖形介面歷史頁、備份檔本身不受影響。`docs/USER_MANUAL.md` §5.3 也補了同一段警示。

6. **`check-tools --profile` 密碼遺失時無提示** — `ProfileStoreAccess.WarnIfPasswordMissing` 在連線前印出警告並指出補救指令。`backup` 與 `restore` 的 `--profile` 路徑一併加上（原票只提 `check-tools`，但三者症狀相同）。

7. **DPAPI 額外熵值的註解高估其作用** — 註解改為「避免與同機其他使用機器金鑰的資料相互混淆」，並明寫「這不是一道安全防線：此常數就在公開的原始碼裡」，真正的防線是檔案權限。

8. **`applyRestrictivePermissions` 是為測試而加在正式建構子上的參數** — 已移除。`MachineScopedConnectionProfileRepository` 與 `MachineScopedCredentialStorage` 現在直接吃 `MachineScopedStoreLocation`，位置與權限政策永遠成對出現、不可能各說各話。檔名常數上移至 `MachineScopedStore`，路徑由 `location.ProfilesFilePath` / `location.CredentialsFilePath` 導出。測試改以 `MachineScopedStoreLocation.CreateUnrestrictedForTesting(暫存目錄)` 注入，意圖寫在工廠方法名稱上而非一個容易忘記傳的布林值。

9. **沒有測試釘住「備份／還原不做權限檢查」** — 新增 `BackupRestoreSkipStorePermissionCheckTests`。刻意注入一個回報「繼承開啟、`BUILTIN\Users` 可存取」的探測替身：若備份或還原真的檢查了權限，就一定會印出警告，測試就會紅。這把原本只靠「相依注入沒註冊 `IEnvironmentProbe`」的巧合，換成明示的防線。

#### 未處理

「跨處理序沒有檔案鎖」依票面註記暫不處理（排程只讀不寫，風險低）。票 02 的認證管理員測試會變更執行機器狀態一事亦僅為提醒，未動。

### 2026-09-19 — 本票完成後再跑一次 `/code-review` 的所得

兩軸各有斬獲，均已修正，全案 **144 個測試全綠**（Core 75、CLI 52、WPF 17）。

#### 規格軸

- **第 3 項的原子性當時只做到改名，沒做到落盤。** `File.WriteAllText` + `File.Move` 之間沒有任何 flush，所謂「斷電也不會留下截斷內容」只是作業系統快取的善意。已改為 `FileStream` 搭配 `FileOptions.WriteThrough` 與 `Flush(flushToDisk: true)`，資料在改名之前確實到達磁碟，票面的宣稱這才成立。
- **暫存檔名固定為 `.tmp` 會讓兩個行程互相踩踏。** 此存放區的設計前提就是「管理員的互動式行程」與「SYSTEM 排程行程」會同時存在。已改為帶隨機值的檔名。
- **第 2 項只做了命令列、沒做圖形介面。** 票面原文要求「須確認 WPF 不會因此炸掉」，當時是以論證回答（「實務上不會拋出」）而非以程式碼或測試回答。這三條路徑都由啟動流程觸發，逸出的例外會讓整個視窗開不起來；`LoadProfilePasswordAsync` 更是以 `_ = ` 呼叫，例外根本不會有人觀察到。已在 `SettingsViewModel` / `BackupViewModel` / `RestoreViewModel` 的載入路徑加上處理，顯示原因而非當掉，並新增 `ProfileStoreUnreadableTests`（4 項）釘住。
- **連線設定檔的損毀仍被吞掉。** 第 2、3 項禁止把讀不到折疊成「沒有設定」，但當時只處理了憑證檔，`JsonConnectionProfileRepository.LoadInternalAsync` 的 `catch { return []; }` 依然會把截斷的 `cli-connection-profiles.json` 讀成空清單——而且下一次儲存就會整份覆蓋掉它。已新增 `ProfileStoreCorruptedException` 並補上兩項測試，其中一項明確斷言損毀的檔案不會被覆寫。

#### 標準軸

- **重複程式碼**：`ProfileCommand` 與 `ProfileStoreAccess` 各有一份逐字相同的主控台輸出。已抽出 `ConsoleMessage`。
- **中間人**：`ProfileCommand.Redact` 只是轉呼叫 `SensitiveText.Redact`。已移除，呼叫端直接用。
- **臆測性通用化**：`JsonConnectionProfileRepository.FilePath`（無任何取用者）、`MachineScopedStore.DefaultProfilesFilePath` / `DefaultCredentialsFilePath`（被本票第 8 項的重構做成死碼，且與 `MachineScopedStoreLocation` 重複了同一條路徑的算法）。三者皆已刪除。
- **`MachineScopedStoreLocation.EnforceRestrictivePermissions` 的預設值已移除。** 這是安全決策，每個建立位置的地方都必須明說，不該有人因為少傳一個參數就意外放寬了存放區。
- **`JsonConnectionProfileRepository` 上的註解原本在替命令列存放區說話**，但這是介面也共用的基底類別。已改寫為與存放區無關的說法（行為不變）。
