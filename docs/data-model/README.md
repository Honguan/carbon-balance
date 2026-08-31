# 資料模型規則

- 主要實體使用穩定 ID，名稱與地址不得作關聯鍵。
- 多租戶資料有明確 `organization_id` 所有權。
- 活動量、係數、比例與碳排使用 `decimal`／PostgreSQL `numeric`。
- 原始值、canonical 值、單位、換算規則版本與資料期間必須同時保存。
- 係數、PCR、公式、GWP 與單位目錄版本化。
- [PCR 規則版本模型](PCR_RULES.md) 定義機器可執行規則、來源完整性、發布與取代流程。
- `CalculationRun` 及其 canonical manifest、SHA-256、明細與摘要建立後不可修改；manifest 固定保存應用程式版本、完整 Git commit SHA、引擎 build、schema 與封存格式版本。
- Audit event append-only，且不包含秘密或完整敏感內容。

正式 ER model 與 migration 會由 Phase 1 的 Golden Vertical Slice 驗證後補入。
