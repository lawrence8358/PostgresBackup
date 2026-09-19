# 11: 存放區目錄可被搶先建立以繞過檔案權限保護

**What to build:** 堵住一個真實的安全漏洞——低權限使用者可以搶先建立命令列連線設定的存放目錄，使其保留寬鬆的繼承權限，導致系統管理員之後寫入的加密密碼落在同機任何人都讀得到的位置。

**Blocked by:** 無

**Status:** resolved

**驗證狀態：** 已於 2026-09-19 完成建置與測試驗證，135 個測試全綠（Core 70、CLI 52、WPF 13）。

## 問題

`C:\ProgramData` 的預設 ACL 允許一般使用者建立子資料夾。原本的 `MachineScopedStore.EnsureRestrictedDirectory` **只在自己建立目錄時**才套用限制性 ACL，既有目錄直接 return（當時註解寫「既有目錄的權限交由使用者掌控」）。

因此低權限使用者可以搶先建立 `C:\ProgramData\PostgresBackup`，使其保留繼承而來的寬鬆權限；之後系統管理員執行 `profile set` 時，加密密碼就寫進一個同機一般使用者讀得到的資料夾。

DPAPI 機器範圍加密救不了這點：額外熵值是原始碼中的公開常數，同機任何使用者都能呼叫 `CryptUnprotectData` 解開。這直接推翻 spec 宣稱的兩大保護之一（「同機的一般使用者受檔案權限阻擋」），以及使用手冊 §6 安全性說明的對應敘述。

次要問題：原本 `profile set` 是**先 `SaveProfileAsync`（密碼落地）、後印權限警告**——使用者看到警告時密碼已經在不安全的位置了。

### 已實測確認（非管理員身分，`LCNB207\Lawrence`）

```
Non-admin CAN create dir under ProgramData: True
Inheritance enabled: True
BUILTIN\Users    ReadAndExecute, Synchronize    Allow
BUILTIN\Users    Write                          Allow
```

## 已完成的修改（待驗證）

- [x] `MachineScopedStore.EnsureRestrictedDirectory` 新增 `applyRestrictivePermissions` 參數；既有目錄也會套用權限（測試注入暫存路徑時關閉，否則非管理員的測試行程會失去自身目錄的存取權）
- [x] `MachineScopedConnectionProfileRepository` / `MachineScopedCredentialStorage` 建構子新增對應參數並往下傳
- [x] 新增 `MachineScopedStoreLocation` record（位置 + 權限政策），由相依注入提供，解決「檢查的目錄」與「實際寫入的目錄」脫鉤的問題
- [x] `profile set` 的權限檢查移到 `SaveProfileAsync` **之前**，不符即拒絕儲存
- [x] 對「目錄已存在但權限不符」採**拒絕**而非自動收緊。理由：Windows 的目錄擁有者隱含具備 `WRITE_DAC`，若目錄是被他人搶先建立的，即使我們把權限收緊，對方仍是擁有者、仍能事後改回來——收緊後就當作安全是假的保證，屬 TOCTOU 殘留漏洞。此狀態需人為介入釐清
- [x] 對「目錄已存在但讀不到權限」同樣拒絕（此前這個最可疑的狀態會被當成「無可評估狀態」而放行）
- [x] `profile list` 維持僅警告不拒絕（唯讀操作，不會讓密碼落地）
- [x] 測試更新：三個 CLI 測試檔註冊指向測試暫存目錄的 location；權限測試改用精確目錄比對的探測替身；`ProfileSet_WhenStoreDirectoryPermissionsAreLoose_Warns` 改寫為 `..._RefusesToSave`（斷言 `SaveProfileAsync` 從未被呼叫）
- [x] 新增 `MachineScopedStoreRestrictedDirectoryTests.EnsureRestrictedDirectory_WhenDirectoryAlreadyExists_StillAppliesRestrictivePermissions`

## 剩餘工作

- [x] **跑 `dotnet build` 與 `dotnet test` 確認全綠** — 建置 0 警告 0 錯誤，135 個測試全綠（Core 70、CLI 52、WPF 13）
- [x] 確認測試未建立真實的 `C:\ProgramData\PostgresBackup` — 測試前後皆確認不存在，未曾建立
- [x] 決定新增的 `MachineScopedStoreRestrictedDirectoryTests` 去留 — **決定保留並簽入**，並已在 spec 的「整合測試範圍」一節寫下例外條款。理由：此測試只在自建的暫存目錄上操作，從不觸及真實的機器層級存放區，也不需要系統管理員權限；spec 禁止的是「依賴特定機器狀態或系統管理員權限、會讓持續整合失敗」的測試，此測試兩者皆非。若不簽入，本批變更中最重要的安全修正將完全沒有防線。實測於本機（非管理員）**確實執行而非略過**並通過
- [ ] 考慮進一步強化：`EnsureRestrictedDirectory` 目前只設 DACL，不動 owner、也不移除既有檔案。若要更徹底，可在以管理員身分執行時一併將 owner 設為 Administrators。**未處理**：屬額外強化而非本票的漏洞修正，且需以管理員身分才能驗證，留待票 10 的實機驗證階段一併評估
- [x] 更新使用手冊 §6 安全性說明 — 已新增「存放區資料夾若已存在且權限不符，工具會拒絕儲存」一節，說明三種情形的處理方式、為何拒絕而非自動收緊，以及使用者該如何排除

## Comments

### 2026-09-19 — 驗證完成

- 建置：0 警告 0 錯誤。測試：135 個測試全綠（Core 70、CLI 52、WPF 13）。
- `C:\ProgramData\PostgresBackup` 在測試前後皆不存在，確認整批測試不觸碰真實的機器層級存放區。
- `MachineScopedStoreRestrictedDirectoryTests` 在本機（`LCNB207\Lawrence`，非管理員）**確實執行**，非略過——也就是說「搶先建立的既有目錄會被收緊繼承並限定於 Administrators 與 SYSTEM」這件事，在這台機器上是實測通過的，不只是編譯得過。
- 票 12 後續把 `applyRestrictivePermissions` 這個為測試而加在正式建構子上的布林參數移除了，改由 `MachineScopedStoreLocation` 同時攜帶位置與權限政策，兩者不再可能各說各話。

## 環境註記

實作 session 的 Bash / PowerShell 工具在修改途中全面故障（連 `echo ok` 都失敗）：

```
EEXIST: file already exists, mkdir '...\547d30ab-...\tasks'
```

子代理亦撞到同一錯誤，屬 session 層級的 harness 問題。推測該路徑成了殘留**檔案**而非目錄。清除／改名該路徑或開新 session 應可恢復。

### 2026-09-19 — 程式碼審查後續：建立與套用權限之間的空窗已堵上

`/code-review` 的規格軸指出：`EnsureRestrictedDirectory` 原本先 `Directory.CreateDirectory`（此時目錄帶著自 ProgramData 繼承而來的寬鬆權限）、再 `SetAccessControl`，兩者之間仍有一段空窗，搶先建立的對手依然有機會在那一瞬間放進東西。修正這個漏洞卻自己留一個同形狀的小洞，說不過去。

已改為在目錄不存在時以 `new DirectoryInfo(directory).Create(security)` **原子地連同存取控制清單一起建立**，權限在目錄誕生的同一刻就位。既有目錄仍走 `SetAccessControl`（語意不變）。非系統管理員身分無法設權限時，仍會退回單純建立目錄，權限狀態交由存放區檢查呈現。

新增測試 `EnsureRestrictedDirectory_WhenDirectoryIsNew_IsCreatedAlreadyRestricted`，與既有的那一項同樣在本機**實際執行**（非略過）並通過。
