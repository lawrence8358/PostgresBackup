# 13: 密碼寫入失敗會留下無密碼的孤兒連線設定

**What to build:** 讓「儲存連線設定」成為一個全有或全無的操作，不要在密碼寫入失敗後留下一筆看起來存在、實際上不能用的設定。

**Blocked by:** 無

**Status:** resolved

## 問題

`JsonConnectionProfileRepository.SaveProfileAsync` 先寫 JSON metadata、後寫密碼：

```csharp
await SaveInternalAsync(list, ct);          // 先寫非機密欄位
if (password != null)
{
    _credentialStorage.SetPassword(...);     // 後寫密碼，票 02 後會拋例外
}
```

票 02 把憑證寫入失敗改為明確拋例外之後，這個順序會留下一筆**已寫入 JSON、但沒有密碼**的連線設定。下次載入時它會出現在清單中，`profile list` 顯示密碼狀態「遺失」，圖形介面也會顯示它。

票 02 已讓介面明確報錯，所以**沒有「誤以為成功」的問題**；這純粹是殘留的半完成狀態需要清理。

於審查中發現，當時判斷不屬票 02 範圍而未擅自更動，決定另立本票。

## 驗收

- [x] 密碼寫入失敗時，不留下該筆連線設定 — 改為**先寫密碼、後寫 JSON**。JSON 檔是一筆設定「存在」的唯一依據，因此最後才落地；密碼寫入失敗時例外直接往外拋，磁碟上不會留下任何痕跡
- [x] 反向的同一問題 — JSON 寫入失敗時回滾憑證：原本有密碼就還原原值，原本沒有就刪除。回滾本身失敗會被吞掉，不得掩蓋原始的儲存失敗（呼叫端要看到的是「為什麼存不起來」）
- [x] 兩套存放區行為一致 — 修改在 `JsonConnectionProfileRepository.SaveProfileAsync`，`MachineScopedConnectionProfileRepository` 由它衍生，自動一致
- [x] 補上測試 — `ConnectionProfileRepositoryAtomicSaveTests`（4 項）
- [x] 既有的「持久化檔案不含密碼」不變條件測試仍然通過

## Comments

### 2026-09-19 — 實作完成

以 TDD 進行：先寫 4 個測試（2 紅 2 綠），再改實作，全數轉綠。

- `SaveProfileAsync_WhenPasswordWriteFails_DoesNotPersistProfile`
- `SaveProfileAsync_WhenPasswordWriteFails_LeavesExistingProfileUntouched` — 更新既有設定時失敗，舊的主機與舊的密碼都必須原封不動
- `SaveProfileAsync_WhenProfileFileWriteFails_DoesNotLeaveOrphanCredential`
- `SaveProfileAsync_WhenProfileFileWriteFails_RestoresPreviousPassword`

取原密碼用的 `TryGetPassword` 刻意吞掉例外：回滾只需要「不留下這次新寫入的密碼」，不值得為了取原值而讓整個儲存流程失敗。

### 2026-09-19 — 程式碼審查後續：刪除路徑有同一個問題

`/code-review` 的規格軸指出本票只修了一半：`SaveProfileAsync` 已是全有或全無，但 `DeleteProfileAsync` 仍是**先寫 JSON、後清密碼**——正是本票所否定的那個順序。密碼清除失敗時，設定會從清單中消失、而加密密碼留在磁碟上，成了一份無主但仍可解開、而且從此沒有任何介面看得到的憑證。這直接違反 spec User Story 9「刪除連線設定，並確保其加密密碼一併被清除，這樣不會留下孤兒憑證」。

已改為**先清密碼、後寫設定檔**。兩步之間失敗時的取捨與儲存時相反但同理：設定檔是這筆設定唯一看得見的入口，因此寧可留下一筆密碼狀態為「遺失」的設定（看得見、重建得回來），也不要留下一份看不見的密碼。

同時修正 `pgbackup profile remove`：原本無視回傳值、無條件印出「其加密密碼已一併清除」。現在會檢查回傳值並攔截例外，失敗時明說「設定與密碼都沒有被移除」並以非零退出碼結束。

新增測試：
- `DeleteProfileAsync_ClearsThePasswordBeforeTheProfileFile`
- `DeleteProfileAsync_WhenPasswordDeletionFails_DoesNotRemoveTheProfile`
