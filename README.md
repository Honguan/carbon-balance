# 產品碳足跡盤查與計算系統

本專案是以 .NET 10、ASP.NET Core 與 PostgreSQL 18 建立的模組化單體，目標是提供可維護、可稽核、可重現的產品碳足跡盤查、計算與查驗準備流程。

目前狀態：Phase 0 repository 基線建置中。實際完成範圍、驗證證據與阻塞項目請見 [實作狀態](docs/IMPLEMENTATION_STATUS.md)。

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
```

Docker 服務與應用程式啟動指令會在 Phase 1 完成後補入；未完成前不得將本文件視為可發布證據。

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
