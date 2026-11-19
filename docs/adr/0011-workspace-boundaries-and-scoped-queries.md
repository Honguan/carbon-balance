# ADR 0011：Workspace 應用邊界與分區、專案範圍化查詢

- 狀態：工程決策已採用。
- 關聯：Issue #48、#49、ADR 0009、ADR 0010。

## 決策

### 應用邊界（#48）

狀態轉換不留在 Razor handler：PCR 與係數治理（`IPcrGovernance`、`IFactorGovernance`）、盤查提交與審查（`IInventoryWorkflow`）、可提交性與計算時效判斷（`IInventoryReadiness`）、建立計算版本（`IInventoryCalculation`）、活動資料新增／更正／移除與佐證上傳（`IActivityData`，以 `ActivityInput` 作為請求 DTO）、成員邀請／撤銷與 SMTP 設定（`IOrganizationAdministration`）、廠場與產品建立（`IProductCatalogue`）、盤查版本建立與階段適用性（`IInventoryProjects`）、Excel 匯出（`IInventoryExport`，唯讀）、PCR 草稿建立（`IPcrGovernance.CreateAsync`）、係數草稿建立與更新草稿（`IFactorGovernance.CreateAsync`／`SupersedeAsync`）都在 Application 定義介面、Infrastructure 實作。每個操作自行負責授權（`IOperationAuthorizer`）、驗證、稽核與 xmin 衝突處理，PageModel 只轉換 HTTP 輸入與結果。`tests/Architecture/RazorHandlerBoundaryTests` 以棘輪限制：已抽出的轉換不得回到 PageModel，`Workspace.cshtml.cs` 行數只可下降。

尚留在 PageModel 的功能（組織／產品／盤查建立、活動、證據、計算、匯出、SMTP 設定）有稽核與並行測試保護，但尚未抽出，列為後續切片。

### 查詢範圍化（#49）

`WorkspaceModel.LoadAsync` 依 `Section` 與選定專案載入資料，不再一次讀取整個組織：

| 區段 | 讀取 |
| --- | --- |
| governance | 成員、邀請 |
| product／setup | 廠場、產品版本、單位 |
| pcr／setup／inventory | PCR 與階段規則、階段宣告、單位；受影響專案以 SQL 計數 |
| factors | 係數第 N 頁（每頁 50 筆）與伺服器端搜尋 |
| lifecycle | 選定專案、選定階段的活動；只讀這些活動引用的證據與係數；可選係數上限 1000 筆 |
| calculation | 就緒度報告與計算時效（單次評估）；最新兩次計算、最多 200 筆明細 |
| 全部（settings 除外） | 專案挑選器（最新 100 筆，必含目前專案）；已存階段數與是否有計算的 SQL 旗標 |

`WorkspaceQueries` 集中這些有上限的唯讀查詢。`tests/Integration/WorkspaceQueryScopeTests` 以 120 個專案、10,080 筆活動、10,000 筆係數驗證各區段的資料列範圍、查詢次數（governance ≤ 14、factors ≤ 20、lifecycle ≤ 25、calculation ≤ 40）與單次延遲（< 3 秒）。

索引檢視：活動、計算、證據與階段宣告的專案外鍵皆已有索引，目前資料量下查詢走 index scan，不需新增 migration。係數以名稱排序分頁在 10,000 筆時約 20 ms；規模再大一個量級時再評估 `(organization_id, name, version_number)` 索引。

## 限制

- 專案挑選器只列最新 100 筆；較舊專案可由連結直接開啟，但尚無搜尋或分頁。
- 生命週期頁的係數下拉僅列前 1000 筆已發布係數，其餘須先到係數資料庫查詢；尚無伺服器端搜尋式選取器。
- 計算明細畫面最多 200 筆，完整明細請匯出 Excel。
