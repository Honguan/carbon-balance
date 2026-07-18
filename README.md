# 產品碳足跡盤查與計算系統

本系統提供產品碳足跡盤查、排放係數與 PCR 版本管理、五階段生命週期資料、證據附件、碳排計算、審核及報表匯出功能。

目前為 **P0 Release Candidate**：Phase 0～8 的工程實作與自動驗證已完成，適合在個人電腦或封閉測試環境進行試用與人工 UAT。

> [!WARNING]
> 目前的 Docker Compose 使用 Development 環境、Mailpit 測試信箱與本機 HTTP，尚未配置正式網域、TLS、正式 SMTP、外部物件儲存、集中式 Secrets 或監控告警。**請勿直接暴露到公開網路或作為正式生產環境。**
>
> 本 repository 可公開讀取，但目前未提供 `LICENSE`。在授權方式確定前，僅供專案評估與測試；不代表授予散布、再授權或商業使用權利。

## 一般使用者快速部署

### 需要準備

只需要以下工具，不必另外安裝 .NET 或 PostgreSQL：

- Windows 10／11、macOS 或 Linux
- Docker Desktop，或 Docker Engine 加上 Docker Compose
- 建議至少 4 GB 可用記憶體；ClamAV 首次啟動會需要較多時間與記憶體
- 約 5 GB 以上可用磁碟空間

確認 Docker 可正常使用：

```bash
docker --version
docker compose version
```

### 1. 取得專案

使用 Git：

```bash
git clone https://github.com/Honguan/carbon-balance.git
cd carbon-balance
```

不熟悉 Git 的使用者也可以在 GitHub 頁面選擇 **Code → Download ZIP**，解壓縮後在終端機進入該資料夾。

### 2. 建立本機設定

Windows PowerShell：

```powershell
Copy-Item .env.example .env
notepad .env
```

macOS／Linux：

```bash
cp .env.example .env
nano .env
```

至少修改以下兩個預設密碼，請使用不同且足夠長的隨機密碼：

```dotenv
POSTGRES_PASSWORD=請替換成資料庫密碼
MINIO_ROOT_PASSWORD=請替換成物件儲存密碼
```

`docker-compose.yml` 會依 `POSTGRES_PASSWORD` 自動組合容器內的資料庫連線字串。`.env` 裡的 `CONNECTIONSTRINGS__DATABASE` 主要供不透過 Docker 啟動 Web 時使用；一般 Docker 使用者不需要修改它。

請勿將包含真實密碼的 `.env` 上傳到 GitHub 或傳給其他人。

### 3. 啟動系統

```bash
docker compose config
docker compose up -d --build
docker compose ps
```

第一次啟動時會自動：

1. 下載並啟動 PostgreSQL、MinIO、Mailpit 與 ClamAV。
2. 建置 Web 應用程式映像。
3. 等待 PostgreSQL 健康後自動執行資料庫 migration。
4. 等待 MinIO、ClamAV 與 Mailpit 可用後啟動 Web。

ClamAV 第一次初始化可能較慢。可重複執行以下指令，直到 `web` 顯示為 `healthy`：

```bash
docker compose ps
```

若啟動失敗，先查看完整記錄：

```bash
docker compose logs --tail=200
```

## 開啟系統

| 服務 | 網址 | 用途 |
|---|---|---|
| 碳足跡系統 | http://localhost:8088 | 主要操作介面 |
| Mailpit | http://localhost:8025 | 查看註冊確認信與測試郵件 |
| MinIO Console | http://localhost:9001 | 查看證據附件物件儲存 |
| Liveness | http://localhost:8088/health/live | 確認 Web 程序存活 |
| Readiness | http://localhost:8088/health/ready | 確認 Web 與資料庫可用 |

MinIO Console 的帳號與密碼分別為 `.env` 中的 `MINIO_ROOT_USER` 與 `MINIO_ROOT_PASSWORD`。

## 第一次使用

1. 開啟 `http://localhost:8088`，選擇註冊帳號。
2. 完成註冊後，開啟 `http://localhost:8025`。
3. 在 Mailpit 找到系統寄出的確認信，點擊信中的確認連結。
4. 回到系統登入。
5. 在帳號管理頁啟用驗證器 MFA；發布係數、PCR 與其他敏感操作會要求 MFA。
6. 依序建立組織、廠場、產品、盤查版本，再輸入各生命週期階段資料。

建議的基本操作順序：

```text
建立帳號並確認 Email
→ 啟用 MFA
→ 建立組織與成員角色
→ 建立廠場與產品
→ 建立或匯入 PCR、單位與排放係數
→ 建立盤查版本
→ 輸入原物料、製造、運輸、使用、廢棄回收資料
→ 上傳佐證文件
→ 執行計算
→ 送審、核准並匯出報表
```

測試環境中的 PCR 與排放係數不代表主管機關、查驗機構或資料權利人的正式認可資料。正式盤查前仍須由領域人員確認資料來源、版本、有效期間與授權。

## 常用管理指令

查看服務狀態：

```bash
docker compose ps
```

持續查看 Web 記錄：

```bash
docker compose logs -f web
```

查看 migration 記錄：

```bash
docker compose logs migrate
```

停止服務但保留資料：

```bash
docker compose down
```

再次啟動：

```bash
docker compose up -d
```

重新建置並套用新版程式：

```bash
git pull
docker compose up -d --build
```

### 清除全部本機資料

> [!CAUTION]
> 以下指令會刪除 PostgreSQL 資料、MinIO 附件、Mailpit 郵件、ClamAV 資料與 Data Protection Keys，無法復原。

```bash
docker compose down -v
```

清除後重新啟動：

```bash
docker compose up -d --build
```

## 常見問題

### 網頁無法開啟

```bash
docker compose ps
docker compose logs web --tail=200
docker compose logs migrate --tail=200
```

確認 `migrate` 已正常結束，且 `web` 已進入 `healthy`。

### ClamAV 長時間無法健康

- 確認 Docker 至少有約 4 GB 可用記憶體。
- 查看 `docker compose logs clamav --tail=200`。
- 首次啟動需要建立或更新病毒碼資料，通常會比後續啟動慢。

### 收不到註冊確認信

本機環境不會寄到真實信箱。請開啟 Mailpit：

```text
http://localhost:8025
```

確認信會直接出現在 Mailpit 收件匣。

### 連接埠被占用

預設會使用以下連接埠：

- `8088`：Web
- `5432`：PostgreSQL
- `9000`、`9001`：MinIO
- `1025`、`8025`：Mailpit

可以先停止占用連接埠的程式，或修改 `docker-compose.yml` 左側的主機連接埠。例如：

```yaml
ports:
  - "8090:8080"
```

修改後 Web 網址會變成 `http://localhost:8090`。

### 修改 `.env` 後沒有生效

重新建立容器：

```bash
docker compose down
docker compose up -d --build
```

若已建立 PostgreSQL volume，事後只改 `POSTGRES_PASSWORD` 不會自動修改資料庫內既有帳號密碼。純測試環境可先備份必要資料，再使用 `docker compose down -v` 重建。

## 開發者環境

需要直接建置、測試或修改程式碼時，另外安裝：

- .NET SDK 10.0.302，或相容的 10.0.3xx patch
- Git
- Docker Desktop／Docker Compose

Windows PowerShell：

```powershell
Copy-Item .env.example .env
dotnet restore --locked-mode
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
docker compose config
docker compose up -d --build
```

本機 integration test 必須明確提供測試資料庫連線字串，且不得指向正式資料庫：

```powershell
$env:CARBON_TEST_DB_CONNECTION='Host=127.0.0.1;Port=5432;Database=carbon_footprint;Username=carbon_app;Password=你的本機資料庫密碼;SSL Mode=Disable;GSS Encryption Mode=Disable'
dotnet test tests/Integration --configuration Release --no-build
```

完整本機 Golden E2E：

```powershell
$smokePassword = Read-Host '輸入符合 Identity 規則的一次性 E2E 密碼'
.\scripts\smoke-e2e.ps1 -Password $smokePassword
```

## 技術架構

- .NET 10／ASP.NET Core Razor Pages
- PostgreSQL 18／Entity Framework Core migrations
- MinIO 證據附件儲存
- ClamAV 上傳檔案掃描
- Mailpit 本機郵件測試
- OpenTelemetry tracing 與 metrics 基礎
- Docker Compose 本機整合環境

## 專案原則

- 舊 PHP 專案只作歷史需求、資料映射、UI 與測試案例來源。
- 係數、PCR、單位、公式與 GWP 規則必須版本化。
- `CalculationRun` 建立後不可修改；修正輸入會建立新的 run。
- 所有碳排權威數值使用 `decimal`／PostgreSQL `numeric`。
- 系統工作流狀態不等同查驗、主管機關核定或產品碳標籤狀態。
- 不提交 secrets、客戶資料、完整 ISO 文件或授權不明的係數資料。

## 專案文件

- [實作狀態](docs/IMPLEMENTATION_STATUS.md)
- [P0 RC 檢核表](docs/release/P0_RC_CHECKLIST.md)
- [P0 RC 驗證紀錄](docs/release/P0_RC_EVIDENCE.md)
- [P0 UAT 計畫](docs/release/P0_UAT_PLAN.md)
- [P0 UAT 簽核紀錄](docs/release/P0_UAT_SIGNOFF.md)
- [待決事項](docs/DECISIONS_NEEDED.md)
- [規劃基線](docs/planning-baseline/README.md)
- [Legacy staging 匯入](docs/migration/README.md)
- [安全政策](SECURITY.md)
- [貢獻指南](CONTRIBUTING.md)
