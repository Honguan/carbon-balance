# 產品碳足跡盤查與計算系統

本專案是以 .NET 10、ASP.NET Core 與 PostgreSQL 18 建立的模組化單體，目標是提供可維護、可稽核、可重現的產品碳足跡盤查、計算與查驗準備流程。

目前狀態：Phase 0 已完成，Phase 1 Golden Vertical Slice 建置中。實際完成範圍、驗證證據與阻塞項目請見 [實作狀態](docs/IMPLEMENTATION_STATUS.md)。

## 開發前置需求

- .NET SDK 10.0.302 或相容的 10.0.3xx patch
- Docker Desktop 及 Docker Compose
- Git

## 快速開始

```powershell
Copy-Item .env.example .env
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
docker compose config
docker compose up -d --build
```

服務啟動後：

- Web：`http://localhost:8088`
- Mailpit：`http://localhost:8025`
- MinIO Console：`http://localhost:9001`
- Liveness：`http://localhost:8088/health/live`
- Readiness：`http://localhost:8088/health/ready`

本機 integration test 需明確提供測試資料庫連線字串；不得指向正式資料庫：

```powershell
$env:CARBON_TEST_DB_CONNECTION='Host=127.0.0.1;Port=5432;Database=carbon_footprint;Username=carbon_app;Password=change-this-local-password;SSL Mode=Disable;GSS Encryption Mode=Disable'
dotnet test --configuration Release --no-build
```

## 專案原則

- 舊 PHP 專案只作歷史需求、資料映射、UI 與測試案例來源。
- 係數、PCR、單位、公式與 GWP 規則必須版本化。
- `CalculationRun` 建立後不可修改；修正輸入會建立新 run。
- 所有碳排權威數值使用 `decimal`／PostgreSQL `numeric`。
- 系統工作流狀態不等同查驗或主管機關核定狀態。
- 不提交 secrets、客戶資料、完整 ISO 文件或授權不明係數。

## 文件

- [規劃基線](docs/planning-baseline/README.md)
- [實作狀態](docs/IMPLEMENTATION_STATUS.md)
- [待決事項](docs/DECISIONS_NEEDED.md)
- [安全政策](SECURITY.md)
- [貢獻指南](CONTRIBUTING.md)

本 repository 尚未取得開源授權，視為 private/unlicensed；請勿自行散布或再授權。
