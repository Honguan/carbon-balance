# 資料模型規則

- 主要實體使用穩定 ID，名稱與地址不得作關聯鍵。
- 多租戶資料有明確 `organization_id` 所有權。
- 活動量、係數、比例與碳排使用 `decimal`／PostgreSQL `numeric`。
- 原始值、canonical 值、單位、換算規則版本與資料期間必須同時保存。
- 係數、PCR、公式、GWP 與單位目錄版本化。
- [PCR 規則版本模型](PCR_RULES.md) 定義機器可執行規則、來源完整性、發布與取代流程。
- `CalculationRun` 及其 canonical manifest、SHA-256、明細與摘要建立後不可修改；manifest 固定保存應用程式版本、完整 Git commit SHA、引擎 build、schema 與封存格式版本。
- Audit event append-only，且不包含秘密或完整敏感內容。

## 目前持久化關係

實際欄位與資料庫約束以 `src/CarbonFootprint.Infrastructure/Persistence/Records.cs`、`CarbonFootprintDbContext.cs` 及既有 migrations 為準。以下是主要 ID 關係，不代表每個概念已有獨立資料表。

```text
Organization
  ├─ Product → ProductVersion → InventoryProjectVersion
  │                              ├─ PcrVersion → PcrStageRule
  │                              ├─ LifecycleStageDeclaration
  │                              ├─ ActivityData → EmissionFactorVersion
  │                              └─ CalculationRun
  │                                  ├─ CalculationLine
  │                                  ├─ CalculationStageSummary
  │                                  └─ CalculationWarning
  ├─ Facility / Membership / Invitation
  └─ EvidenceFile / AuditEvent / LegacyImportBatch（各自保留組織範圍）
```

`InventoryProjectVersion` 綁定產品與 PCR 版本；`ActivityData` 保存階段、種類、原始／標準值、來源、分配比例、情境文字與公式輸入。`CalculationRun` 保存 canonical manifest 及版本／來源雜湊，另有明細與摘要 records；不是每次查詢從可變活動列重算歷史報告。

本次只調整快照讀取責任，未變更 schema 或 manifest 格式。BOM 結構、全廠分配分子／分母與產量、可重用使用模式、廢棄處理占比尚未有完整結構化模型；設計與驗收依[產品需求](../04_PRODUCT_REQUIREMENTS.md)，不可把共用活動列視為已涵蓋全部原始需求。
