# 盤查提交前檢查（Readiness）規則

`InventoryReadinessValidator`（`CarbonFootprint.Application.Readiness`）是純函式：輸入不可變盤查快照、PCR 違規清單、
佐證檔案資訊、最新計算版本狀態與基準日，輸出固定排序的 `ReadinessReport`。UI、提交 handler、JSON 端點
（`GET /Workspace?handler=Readiness&projectVersionId=…`）與測試共用同一個驗證器。

## 嚴重度

| 嚴重度 | 行為 |
|---|---|
| `BlockingError` | 阻擋提交。 |
| `RequiredExplanation` | 提交者須勾選確認並填寫說明；說明空白視為未確認。確認內容寫入 `inventory.submitted` 稽核事件。 |
| `Warning` | 顯示，不阻擋。 |
| `Recommendation` | 建議，不阻擋。 |

`CanSubmit` ＝ 無阻擋項目，且所有 `RequiredExplanation` 皆已確認。每筆結果含穩定規則代碼、嚴重度、實體類型與識別、
所屬階段、資料負責人（活動資料提供者）、訊息與處理方式；同一快照與基準日必得相同結果與 `Sha256()`。

## 規則代碼

| 代碼 | 嚴重度 | 條件 |
|---|---|---|
| `PCR-*` | Blocking | 轉載 `PcrRuleEngine` 違規（狀態、產品範圍、功能／標示單位、系統邊界、分配、截斷揭露、階段與活動規則）。 |
| `RDY-CALC-MISSING` / `-PENDING-RULES` / `-PROVENANCE` / `-STALE` | Blocking | 無計算版本、公式待審、缺 build provenance、輸入已與計算 manifest 不符。 |
| `RDY-PERIOD-INVALID` | Blocking | 盤查起日晚於迄日。 |
| `RDY-STAGE-DECLARATION` / `-REASON` / `-ACTIVITY` | Blocking | 階段未宣告一次、不適用未填原因、適用階段無活動。 |
| `RDY-STAGE-EXCLUDED` | Warning | 階段已標示不適用。 |
| `RDY-ACTIVITY-KIND` / `-NEGATIVE` / `-PERIOD` | Blocking | 活動類型不屬於階段、負值、期間不在盤查期間內。 |
| `RDY-ACTIVITY-AGE` | RequiredExplanation | 活動期間結束日早於基準日超過 `MaxDataAgeMonths`。 |
| `RDY-DATA-QUALITY` | Blocking | 未標示資料品質。 |
| `RDY-FACTOR-STATUS` / `-VALIDITY` / `-UNIT` | Blocking | 係數未發布／未審查／缺適用性、不在有效期、單位與係數分母不符。 |
| `RDY-ALLOCATION-RANGE` / `-REASON` | Blocking | 分配比例不在 (0,1]、比例小於 1 卻無分配依據。 |
| `RDY-ESTIMATE-REASON` | Blocking | 估算資料未填理由。 |
| `RDY-ESTIMATE-RATIO` | RequiredExplanation | 估算活動占比超過 `MaxEstimatedActivityRatio`。 |
| `RDY-EVIDENCE-MISSING` / `-INTEGRITY` | Blocking | 無佐證，或沒有「掃描 Clean 且 SHA-256 與活動記錄相符」的佐證檔。 |
| `RDY-EXCLUSIONS-BLANK` / `-ASSUMPTIONS-BLANK` | Recommendation | 未記錄排除項目或假設。 |

## 設定與限制

- `MaxDataAgeMonths`（預設 36）與 `MaxEstimatedActivityRatio`（預設 0.30）是工程暫定值，尚無領域核准，見 DEC-008；不可視為 PCR 或查驗要求。
- 尚無分配池與共同產品模型（#24）、結構化資料品質評分（#23），因此未涵蓋「分配池總和」等檢查；這些規則待對應模型存在後再加入。
- 稽核事件 `inventory.submitted` 的 metadata 記錄 `readinessReportSha256`、`readinessAsOf`、`readinessFindingCounts` 與使用者確認內容。
  驗證封存包匯出（#30）尚未實作，因此報告尚未納入匯出檔。
- 核准（Review）仍使用原有的最新計算與 PCR 重新驗證；本驗證器目前只守住提交。
