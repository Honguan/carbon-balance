# 碳衡｜產品碳足跡盤查與計算系統

碳衡（Carbon Balance）是一套聚焦產品碳足跡的盤查、計算與稽核準備系統。系統依產品生命週期管理原物料、製造、運輸、使用及廢棄回收資料，保存活動數據、排放係數版本、計算輸入快照與逐項 CO₂e 結果。

目前為 **P0 Release Candidate**，適合在個人電腦或封閉測試環境進行試用與人工 UAT。

> [!WARNING]
> Docker Compose 使用本機 HTTP、Development 環境與測試服務，尚未配置正式網域、TLS、集中式 Secrets、備份、監控告警及正式物件儲存。請勿直接暴露到公開網路。
>
> 本 repository 目前未提供 `LICENSE`。在授權方式確定前，僅供專案評估與測試。

## 系統重點

- 五階段產品生命週期盤查
- 活動數據、單位與資料來源管理
- 排放係數與 PCR 版本追溯
- 計算輸入快照與不可變更的 Calculation Run
- 碳排熱點、審核與報表匯出
- 證據附件、MinIO 儲存與 ClamAV 掃描
- 簡化角色登入，不使用公開會員網站流程
- 碳足跡、CO₂e、葉片、製程、運輸及回收視覺風格

## 登入與角色設計

登入只用於辨識使用者及套用角色權限，不要求額外認證流程：

- 不需要 Email 確認
- 不需要 MFA 或復原碼
- 不提供外部社群登入
- 不提供公開忘記密碼與重設密碼流程
- 密碼至少 6 個字元
- 不強制大小寫、數字或特殊符號

系統固定角色：

| 角色代碼 | 顯示名稱 | 建議用途 |
|---|---|---|
| `Administrator` | 系統管理者 | 系統設定、角色與完整資料管理 |
| `Manager` | 盤查主管 | 審核盤查內容與查看完整結果 |
| `Editor` | 盤查編輯者 | 建立及修改碳足跡盤查資料 |
| `Viewer` | 檢視者 | 唯讀查看盤查與計算結果 |

首次安裝只允許建立一個初始管理者。建立完成後，公開註冊頁會自動關閉。

## 一般使用者快速部署

### 需要準備

只需要以下工具，不必另外安裝 .NET 或 PostgreSQL：

- Windows 10／11、macOS 或 Linux
- Docker Desktop，或 Docker Engine 加 Docker Compose
- 建議至少 4 GB 可用記憶體
- 約 5 GB 以上可用磁碟空間

確認 Docker 已啟動：

```bash
docker --version
docker compose version
```

### 1. 取得專案

```bash
git clone https://github.com/Honguan/carbon-balance.git
cd carbon-balance
```

也可以在 GitHub 選擇 **Code → Download ZIP**，解壓縮後進入專案資料夾。

### 2. 自動建立服務密碼並啟動

設定腳本會建立 `.env`，自動產生不同的 PostgreSQL 與 MinIO 密碼，驗證 Compose 後啟動全部服務。

Windows PowerShell：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\setup-local.ps1
```

macOS／Linux：

```bash
bash scripts/setup-local.sh
```

> [!IMPORTANT]
> 密碼只會在第一次建立 `.env` 時顯示。之後腳本會沿用原本設定，不會自動覆寫。請勿上傳或公開 `.env`。

### 手動設定服務密碼

Windows：

```powershell
.\scripts\setup-local.ps1 -Manual
```

macOS／Linux：

```bash
bash scripts/setup-local.sh --manual
```

服務密碼規則：

- 16～128 字元
- PostgreSQL 與 MinIO 必須不同
- 使用英文字母、數字、句點、底線或連字號

也可以自行建立 `.env`：

```powershell
Copy-Item .env.example .env
notepad .env
```

至少設定：

```dotenv
POSTGRES_PASSWORD=你的資料庫密碼
MINIO_ROOT_PASSWORD=另一組物件儲存密碼
```

再啟動：

```bash
docker compose config --quiet
docker compose up -d --build
docker compose ps -a
```

## 第一次啟動

Compose 會依序：

1. 啟動 PostgreSQL、MinIO、Mailpit 與 ClamAV。
2. 建置 Web 映像。
3. 執行 Entity Framework Core migration。
4. 建立 `Administrator`、`Manager`、`Editor`、`Viewer` 角色。
5. 等待相依服務健康後啟動 Web。

ClamAV 第一次初始化可能較慢。重複執行：

```bash
docker compose ps -a
```

正常狀態包括：

```text
migrate    Exited (0)
postgres   Up (healthy)
minio      Up (healthy)
clamav     Up (healthy)
web        Up (healthy)
```

`migrate` 是一次性容器，`Exited (0)` 代表成功，不需要手動啟動。

## 開啟系統

| 服務 | 網址 | 用途 |
|---|---|---|
| 碳衡系統 | http://127.0.0.1:8088 | 主要盤查與計算介面 |
| Mailpit | http://127.0.0.1:8025 | 查看邀請及其他測試郵件 |
| MinIO Console | http://127.0.0.1:9001 | 查看證據附件物件儲存 |
| Liveness | http://127.0.0.1:8088/health/live | Web 程序存活狀態 |
| Readiness | http://127.0.0.1:8088/health/ready | Web 與資料庫可用狀態 |

MinIO 帳號與密碼位於 `.env`：

```dotenv
MINIO_ROOT_USER=carbon_minio
MINIO_ROOT_PASSWORD=...
```

## 第一次使用

1. 開啟 `http://127.0.0.1:8088`。
2. 選擇「登入系統」。
3. 選擇「首次使用？建立初始管理者」。
4. 輸入顯示名稱、Email 及至少 6 個字元的密碼。
5. 系統會直接建立 `Administrator` 角色並登入，不需要收取確認信。
6. 建立組織、廠場、產品與盤查版本。
7. 輸入生命週期資料並執行計算。

初始管理者建立後，再次開啟註冊網址會回到登入頁，無法公開建立第二個帳號。

建議操作順序：

```text
建立初始管理者
→ 建立組織與成員角色
→ 建立廠場與產品
→ 建立或匯入 PCR、單位與排放係數
→ 建立盤查版本
→ 輸入原物料、製造、運輸、使用、廢棄回收資料
→ 上傳佐證文件
→ 執行計算
→ 送審、核准並匯出報表
```

測試環境中的 PCR 與排放係數不代表主管機關、查驗機構或資料權利人的正式認可資料。正式盤查前仍須由領域人員確認資料來源、版本、有效期間及授權。

## 更新專案

```bash
git pull
docker compose up -d --build --force-recreate web
```

若本次更新包含 migration，Compose 會自動執行 `migrate`。

## 常用指令

查看全部服務：

```bash
docker compose ps -a
```

查看 Web 與 migration 日誌：

```bash
docker compose logs web --tail=200
docker compose logs migrate --tail=200
```

停止但保留資料：

```bash
docker compose down
```

再次啟動：

```bash
docker compose up -d
```

### 清除全部測試資料

> [!CAUTION]
> 以下操作會刪除 PostgreSQL、MinIO、Mailpit、ClamAV 及 Data Protection Keys 資料，無法復原。

Windows：

```powershell
.\scripts\setup-local.ps1 -ResetData
```

macOS／Linux：

```bash
bash scripts/setup-local.sh --reset-data
```

## 密碼與資料 Volume

PostgreSQL 官方映像只會在第一次建立資料目錄時套用 `POSTGRES_PASSWORD`。因此：

- 不要刪除仍在使用中的 `.env`。
- 不要在已有資料時任意修改 `POSTGRES_PASSWORD`。
- `.env` 遺失但 PostgreSQL Volume 仍存在時，設定腳本會停止。
- 測試資料可以刪除時，使用 `-ResetData` 或 `--reset-data` 重建。

### `password authentication failed for user "carbon_app"`

代表 PostgreSQL Volume 與目前 `.env` 的密碼不同。資料不需要保留時：

```powershell
.\scripts\setup-local.ps1 -ResetData
```

需要保留資料時，請恢復原本 `.env` 或由資料庫管理員修改帳號密碼。

### 網頁顯示 `ERR_EMPTY_RESPONSE`

使用完整 HTTP 網址：

```text
http://127.0.0.1:8088
```

再檢查：

```bash
docker compose ps -a
docker compose logs web --tail=200
docker compose logs migrate --tail=200
```

Windows 可測試：

```powershell
curl.exe -i http://127.0.0.1:8088/health/live
curl.exe -i http://127.0.0.1:8088/health/ready
```

### 連接埠被占用

預設連接埠：

- `8088`：Web
- `5432`：PostgreSQL
- `9000`、`9001`：MinIO
- `1025`、`8025`：Mailpit

可以修改 `docker-compose.yml` 左側的主機連接埠，例如：

```yaml
ports:
  - "8090:8080"
```

Web 網址會改為 `http://127.0.0.1:8090`。

## 開發者環境

另外安裝：

- .NET SDK 10.0.302 或相容的 10.0.3xx patch
- Git
- Docker Desktop／Docker Compose

```powershell
.\scripts\setup-local.ps1 -NoStart
dotnet restore --locked-mode
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
docker compose config --quiet
docker compose up -d --build
```

Integration test 必須使用非正式環境資料庫：

```powershell
$env:CARBON_TEST_DB_CONNECTION='Host=127.0.0.1;Port=5432;Database=carbon_footprint;Username=carbon_app;Password=你的本機資料庫密碼;SSL Mode=Disable;GSS Encryption Mode=Disable'
dotnet test tests/Integration --configuration Release --no-build
```

完整本機 Golden E2E：

```powershell
$smokePassword = Read-Host '輸入至少 6 個字元的一次性 E2E 密碼'
.\scripts\smoke-e2e.ps1 -Password $smokePassword
```

## 技術架構

- .NET 10／ASP.NET Core Razor Pages
- ASP.NET Core Identity 角色授權
- PostgreSQL 18／Entity Framework Core migrations
- MinIO 證據附件儲存
- ClamAV 上傳檔案掃描
- Mailpit 本機郵件測試
- OpenTelemetry tracing 與 metrics
- Docker Compose 本機整合環境

## 專案原則

- 系統畫面只呈現與產品碳足跡盤查、計算、審核及匯出有關的功能。
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
