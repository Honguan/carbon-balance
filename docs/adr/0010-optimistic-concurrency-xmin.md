# ADR 0010：以 PostgreSQL xmin 作為可變草稿的樂觀並行控制

- 狀態：工程決策已採用；正式領域／查驗核准另行辦理。
- 關聯：Issue #55、ADR 0007、ADR 0009。

## 決策

可變且會被工作流程轉換的資料列（盤查專案版本、PCR 版本、係數版本、階段適用性宣告、組織 SMTP 設定、成員與邀請）對應 `uint RowVersion` 到 PostgreSQL 系統欄位 `xmin`，並設為 EF Core concurrency token。xmin 為系統欄位，故對應 migration 不含 DDL，也不新增欄位。

表單帶 `rowVersion` 隱藏欄位；Workspace handler 在載入資料列後比對，不符即在寫入前回 409。比對通過後，EF 的 `UPDATE ... WHERE xmin = @original` 仍會攔截請求之間的真實並行寫入（`DbUpdateConcurrencyException`）。兩條路徑都不套用任何變更，並以 `concurrency.rejected` attempt 事件記錄被拒的操作（`operation=attempt`、無 before／after hash、metadata 記錄 `attemptedOperation`），與成功的 mutation 事件可區分。

## 範圍與限制

- Append-only 的版本資料（活動資料版本、計算執行、稽核事件）不加 token；活動資料維持既有 `RetiredAt` token。
- `xmin` 在 VACUUM FREEZE 後的環繞，以及資料列被任何欄位更新（含無實質變更）都會改變 token；這只會造成保守的衝突，不會漏檢。
- 衝突頁面只提示重新載入；欄位層級合併 UI 不在本決策內。
- 還原備份或 `pg_dump` 匯入會產生新的 xmin；開啟中的舊表單會被判定過期，須重新載入。
