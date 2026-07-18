# 資料模型規劃

## 1. 設計原則

1. 所有主要實體使用 UUID／ULID 或資料庫產生的穩定 ID。
2. 顯示名稱、產品名稱、地址與係數名稱不可作關聯鍵。
3. 業務歷史以版本與有效期間表達，不以覆寫代替。
4. 數值、單位、來源與資料期間不可拆散。
5. 計算輸入與結果形成不可變快照。
6. 允許中文顯示名稱，但資料表與欄位採一致英文命名。
7. 多租戶資料都具 `organization_id`，並以授權與測試保證隔離。

## 2. 核心實體

### Identity / Organization

- `organizations`
- `users`
- `organization_memberships`
- `roles`
- `invitations`
- `facilities`

### Product

- `products`
- `product_versions`
- `product_classifications`（CCC Code 等）
- `product_facilities`

### Standards / PCR

- `standards`
- `standard_versions`
- `pcr_documents`
- `pcr_versions`
- `pcr_classifications`
- `pcr_rule_sets`
- `pcr_rule_requirements`

### Factor Registry

- `factor_sources`
- `factor_datasets`
- `emission_factors`
- `emission_factor_versions`
- `factor_applicability`
- `factor_import_batches`
- `factor_import_rows`
- `factor_reviews`

### Inventory

- `inventory_projects`
- `inventory_project_versions`
- `project_boundaries`
- `functional_units`
- `declared_units`
- `lifecycle_processes`
- `activity_data`
- `activity_data_versions`
- `allocations`
- `transport_legs`
- `usage_scenarios`
- `waste_scenarios`
- `exclusions`
- `assumptions`

### Evidence / Review

- `evidence_files`
- `evidence_links`
- `review_tasks`
- `review_comments`
- `approvals`
- `verification_records`
- `approval_records`

### Calculation

- `formula_rule_sets`
- `formula_rules`
- `calculation_runs`
- `calculation_run_inputs`
- `calculation_line_items`
- `calculation_stage_summaries`
- `calculation_warnings`

### Audit / Operations

- `audit_events`
- `export_jobs`
- `outbox_messages`
- `retention_holds`

## 3. 數值模型

### 建議型別

| 用途 | PostgreSQL |
|---|---|
| 活動數據 | `numeric(30, 12)` |
| 排放係數 | `numeric(30, 15)` |
| 比例／分配 | `numeric(20, 15)` |
| 碳排結果 | `numeric(38, 15)` |
| 日期 | `date` |
| 時間點 | `timestamptz` |

精度需經 Golden Case 驗證後定案。禁止直接把原始科學記號與單位混在同一文字欄位。

### 原始值與標準值

活動數據保存：

```text
raw_value
raw_unit_id
canonical_value
canonical_unit_id
conversion_rule_version
measurement_period_start
measurement_period_end
```

這使系統可顯示使用者原始輸入，也能重現換算。

## 4. 單位模型

- `units`: code, symbol, dimension, scale, offset, reference_unit。
- `unit_aliases`: 來源中的中文／英文／變體。
- `unit_conversion_versions`: 換算規則版本。
- 只允許同 dimension 換算。
- 複合單位以結構化 numerator/denominator 表達，如 `kgCO2e / tonne-km`。

## 5. 係數版本模型

`emission_factor_versions` 至少包含：

```text
factor_id
version_no
value
numerator_unit_id
denominator_unit_id
geography
technology
valid_from
valid_to
reference_period_start
reference_period_end
lifecycle_scope
source_dataset_version
source_document_id
quality_grade
verification_status
license_code
published_at
withdrawn_at
supersedes_version_id
```

採用係數時，`activity_data` 不只保存 `factor_id`，還必須保存明確的 `factor_version_id`。

## 6. Calculation Run 不可變性

建立 run 時：

- 複製或引用不可變版本的所有輸入。
- 保存 serialized canonical input manifest。
- 計算 SHA-256。
- 保存 engine build/version、rule set version、unit catalogue version、GWP version。
- line item 保存公式識別、輸入、輸出、單位與來源。

修改活動數據後，舊 run 保持不變；新 run 以 `supersedes_run_id` 連結。

## 7. Audit Event

`audit_events` 為 append-only：

```text
id
timestamp
actor_type
actor_id
organization_id
action
resource_type
resource_id
before_hash
after_hash
correlation_id
ip_hash
metadata_json
```

敏感內容不直接寫入 log；保存必要識別與 hash。

## 8. Legacy 欄位映射示例

| 舊欄位 | 新模型 |
|---|---|
| `盤查基本資料表.Chinese` | `product_versions.name_zh_tw` |
| `盤查起始時間 varchar` | `inventory_project_versions.period_start date` |
| `原物料投入.投入量` | `activity_data.raw_value + raw_unit_id` |
| `碳足跡係數 varchar` | `emission_factor_version.value` + units |
| `係數來源 varchar` | `factor_source` + `source_document` |
| `碳排放量 varchar` | 不直接匯入為權威結果；重算後存 calculation line |
| `map算距離` | `transport_leg.distance` + method + provider + evidence |

## 9. 不採用任意公式執行

舊版 `mathexpressions`／使用情境允許文字公式。新版不應使用 `eval`、動態 C#、JavaScript 或 SQL 執行使用者公式。

可採：

- 受限 AST。
- 白名單運算子與函數。
- 強型別變數與單位維度檢查。
- rule set 版本與發布審核。
- 運算步數、深度與輸入大小限制。
