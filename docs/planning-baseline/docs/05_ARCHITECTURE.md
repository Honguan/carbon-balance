# 技術架構

## 1. 技術選型

### 預設方案

| 層 | 技術 |
|---|---|
| Runtime | .NET 10 LTS，持續套用最新 10.0.x 安全修補 |
| Web | ASP.NET Core MVC／Razor Pages |
| Frontend | HTML、CSS、少量 TypeScript；複雜元件才引入套件 |
| Domain/Application | C# class libraries |
| Database | PostgreSQL 18，持續套用最新 18.x minor |
| ORM | Entity Framework Core 10 |
| Jobs | .NET BackgroundService 起步；需要可靠排程時再引入持久化 job library |
| File storage | S3 相容物件儲存；開發環境使用本機／MinIO |
| Cache | 預設不引入；有量測需求後才使用 Redis |
| Observability | OpenTelemetry + structured logging + metrics + traces |
| Deployment | Docker Compose；Reverse Proxy 由平台或 Caddy/Nginx 提供 |
| CI/CD | GitHub Actions + container image + SBOM + vulnerability scan |

.NET 10 為目前 active LTS，官方支援至 2028-11-14；PostgreSQL 18 官方支援至 2030-11-14。選擇兩者是因為支援週期、型別安全、交易、測試與企業維護能力，而非為了追逐最新版。

### 為何不沿用 raw PHP

技術語言不是唯一問題；真正成本在資料模型、驗證、授權、計算、測試與稽核。沿用現有 raw PHP 並不能避免重做這些核心。

### 為何不是微服務

目前團隊與領域需求尚未證明獨立部署、不同擴展曲線或組織邊界。模組化單體可保留清楚邊界、單一交易與較低運維成本；未來可依 telemetry 與組織需求拆分。

## 2. 模組邊界

```text
Identity & Organizations
Products & Facilities
PCR & Standards
Factor Registry
Inventory Projects
Lifecycle Data
Calculation Engine
Evidence & Review
Reports & Exports
Administration & Audit
Integrations
```

每個模組包含：

```text
Domain
Application
Infrastructure
Web/API endpoints
Tests
```

禁止模組直接跨資料表讀寫；跨模組透過 application contract 或 domain event（同程序）協作。

## 3. 建議 solution 結構

```text
src/
├─ CarbonFootprint.Web/
├─ CarbonFootprint.AppHost/              # optional local orchestration
├─ BuildingBlocks/
│  ├─ CarbonFootprint.SharedKernel/
│  ├─ CarbonFootprint.Infrastructure/
│  └─ CarbonFootprint.Contracts/
└─ Modules/
   ├─ Identity/
   ├─ Organizations/
   ├─ Products/
   ├─ Standards/
   ├─ Factors/
   ├─ Inventories/
   ├─ Calculations/
   ├─ Evidence/
   ├─ Reporting/
   └─ Audit/

tests/
├─ Unit/
├─ Integration/
├─ Architecture/
├─ Security/
├─ Contract/
└─ GoldenCases/

docs/
.github/
scripts/
infra/
```

## 4. 計算架構

### 輸入

- Inventory Project Version。
- PCR/Rule Set Version。
- Unit Catalogue Version。
- Factor Version Set。
- Formula Engine Version。
- GWP Set Version。

### 執行

1. Freeze 所有輸入參照。
2. 驗證必填、單位維度、係數適用性、期間與邊界。
3. 將原始活動值轉為 canonical unit。
4. 套用分配、運輸、使用與廢棄情境規則。
5. 產生逐項 calculation line。
6. 聚合至 process、stage、product。
7. 產生 warnings、assumptions、exclusions 與 data quality summary。
8. 寫入不可變 calculation run 與 input snapshot hash。

### 輸出

```text
CalculationRun
CalculationLineItem[]
StageSummary[]
ProductSummary
Warning[]
Assumption[]
EvidenceReference[]
InputSnapshotHash
EngineVersion
```

計算模組不直接產生「已查驗」狀態。

## 5. 安全架構

- ASP.NET Core Identity；密碼政策、MFA、email confirmation、lockout。
- 組織 membership 與 resource-level authorization。
- Cookie 介面啟用 CSRF；API 使用 OAuth/OIDC token。
- 所有資料存取帶 `organization_id` 範圍並有 integration test。
- 檔案上傳執行 MIME／大小／副檔名／惡意內容掃描與隔離。
- secrets 使用 GitHub Environments／雲端 secret store，不寫入 repo 或 image。
- 管理員操作使用重新驗證、MFA 與 audit。
- 匯出檔使用短期簽名 URL 或受權限保護的串流。
- 安全 header、CSP、rate limit、日誌去敏感化。

## 6. 資料與檔案

PostgreSQL 保存交易資料與 metadata；原始 PCR、係數匯入檔、證據與報告保存在 object storage。資料庫只保存：

- object key。
- filename／content type／size。
- SHA-256。
- uploader／時間／組織。
- retention class。
- malware scan 狀態。

## 7. 排程與外部同步

係數／PCR 同步屬 side-effecting batch：

1. 下載至 quarantined import batch。
2. 保存原始檔、來源、HTTP metadata 與 checksum。
3. parse 到 staging table。
4. validation 與差異報告。
5. 人工審核或受控自動核准。
6. publish 新版本，不覆寫舊版本。

任何外部來源抓取失敗不得刪除目前有效資料。

## 8. 部署拓撲

### 開發／測試

```text
web
postgres
object-storage
mail-catcher
otel-collector (optional)
```

### 正式起步

```text
reverse proxy / load balancer
2 x web containers
managed PostgreSQL / HA PostgreSQL
object storage
email provider
central logs, metrics, traces
backup storage
```

## 9. 升級政策

- .NET：保持同一 LTS 最新 patch；下一 LTS 上線後規劃升級，不停留 EOL。
- PostgreSQL：保持 major 最新 minor；每年至少一次 upgrade rehearsal。
- NuGet／npm：Dependabot，每週建立 PR；security update 依嚴重度 SLA。
- Container base image：使用 digest pinning，至少每月重建。
- 法規／PCR／係數：與程式版本分離，採資料版本與 rule set version。
