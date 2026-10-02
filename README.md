# 碳衡｜產品碳足跡盤查系統

碳衡（Carbon Balance）以產品生命週期五階段引導企業蒐集盤查資料、搜尋與選用係數，產生可追溯的 CO₂e 結果、證據附件與稽核匯出，支援後續改善分析。

產品與開發依據：[目前需求](docs/04_PRODUCT_REQUIREMENTS.md)、[目前架構](docs/05_ARCHITECTURE.md)、[old 來源分析](docs/architecture/LEGACY_PURPOSE_ANALYSIS.md)、[實作狀態與缺口](docs/IMPLEMENTATION_STATUS.md)。`docs/planning-baseline` 是歷史規劃；本機 `old/` 保留唯讀來源，不提交原始資料。

> 本專案目前適合本機或封閉測試環境，請勿直接公開到網際網路。Repository 尚未提供 LICENSE。

## 系統需求

- Windows 10／11、macOS 或 Linux
- Docker Desktop，或 Docker Engine + Docker Compose
- 建議至少 4 GB 可用記憶體

## 快速啟動

先取得專案：

```bash
git clone https://github.com/Honguan/carbon-balance.git
cd carbon-balance
```

Windows PowerShell：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\setup-local.ps1
```

macOS／Linux：

```bash
bash scripts/setup-local.sh
```

腳本會自動建立 `.env`、產生 PostgreSQL 與 MinIO 密碼、執行 migration 並啟動服務。

確認狀態：

```bash
docker compose ps -a
```

正常狀態：

```text
migrate    Exited (0)
postgres   Up (healthy)
minio      Up (healthy)
clamav     Up (healthy)
web        Up (healthy)
```

`migrate` 是一次性容器，`Exited (0)` 代表正常完成。

## 第一次使用

1. 開啟系統：`http://127.0.0.1:8088`
2. 選擇「建立帳號」。
3. 從本機 `.env` 取得 `ADMIN_BOOTSTRAP_TOKEN`，連同顯示名稱、Email 與密碼建立初始管理者。
4. 開啟 Mailpit：`http://127.0.0.1:8025`
5. 開啟確認信並點擊「確認帳號」。
6. 回到系統登入。

角色規則：

- 只有持有一次性 bootstrap token 的註冊可以取得初始 `Administrator`；成功後資料庫會永久關閉 bootstrap。
- 未提供 token 的一般註冊一律取得 `Viewer`，不會因註冊順序升級權限。
- 後續系統管理者只能由已登入的 `Administrator` 在 `/Administration/Users` 指派，並留下全域安全稽核事件。
- 登入時可勾選「在這台裝置保持登入 30 天」。

## 服務網址

| 服務 | 網址 |
|---|---|
| 碳衡系統 | `http://127.0.0.1:8088` |
| Mailpit 測試信箱 | `http://127.0.0.1:8025` |
| MinIO Console | `http://127.0.0.1:9001` |
| PostgreSQL | `127.0.0.1:15432` |
| 健康狀態 | `http://127.0.0.1:8088/health/ready` |

MinIO 帳號與密碼位於 `.env`。

正式環境必須由 secrets provider 設定 32 至 128 個字元的 `AdministratorBootstrap:Token`；不要使用本機 token、提交 token，或透過公開管道傳送。bootstrap 成功後，即使相同 token 仍在設定中也無法再次取得管理者權限。

## 環境部係數同步

1. 執行 `docker compose up -d --build`。
2. `migrate` 工作會在 migration 完成後下載一次資料，並為當時已存在的每個組織直接寫入及發布可用係數；相同資料重複部署不會重複新增版本。
3. 登入後可開啟「係數資料庫」，按「同步並導入可用係數」手動重試或取得後續更新。
4. 如需使用自有環境部 API Key，可在 `.env` 加入 `MOENV_API_KEY=取得的金鑰` 後重新啟動；未設定時會解析政府資料開放平臺提供的官方 JSON 公開下載網址。

同步來源為環境部資料集 `CFP_P_02`。重新整理瀏覽器不會觸發同步；環境部自動導入資料免人工審查並直接發布，手動新增或更新的係數仍建立待審查草稿。係數表單不要求人工輸入原始文件 SHA-256；公開來源的原始紀錄 SHA-256 由系統自動保存。舊版本與歷史計算不會被覆寫。全新空資料庫若尚無組織，部署匯入會記錄 0 個組織且不寫入係數；建立組織後由係數資料庫手動同步。若部署環境無法連線公開來源，可設定 `MOENV_IMPORT_ON_DEPLOYMENT=false` 暫停預設匯入，待連線恢復後再手動同步。

盤查資料與最新計算結果可在「計算與結果」頁匯出為 Excel。

## 更新系統

```bash
git pull
docker compose up -d --build
```

更新後瀏覽器可按 `Ctrl + F5` 強制重新載入畫面。

## 常用指令

查看狀態：

```bash
docker compose ps -a
```

查看日誌：

```bash
docker compose logs web --tail=200
docker compose logs migrate --tail=200
```

停止但保留資料：

```bash
docker compose down
```

重新啟動：

```bash
docker compose up -d
```

## 清除測試資料

> 以下操作會刪除資料庫、附件、測試郵件與登入金鑰，無法復原。

Windows：

```powershell
.\scripts\setup-local.ps1 -ResetData
```

macOS／Linux：

```bash
bash scripts/setup-local.sh --reset-data
```

## 常見問題

### 收不到確認信

開啟 `http://127.0.0.1:8025` 查看 Mailpit。也可在登入頁選擇「重寄確認信」。

### `password authentication failed for user "carbon_app"`

代表 `.env` 密碼與既有 PostgreSQL Volume 不一致。測試資料不需保留時執行：

```powershell
.\scripts\setup-local.ps1 -ResetData
```

### 網頁無法開啟

```bash
docker compose ps -a
docker compose logs web --tail=200
docker compose logs migrate --tail=200
```

使用完整網址：`http://127.0.0.1:8088`，不要使用 `https://`。

### 設定組織 SMTP

登入後開啟工作區的「郵件服務設定」分頁，輸入 SMTP 主機、連接埠、TLS、帳號及寄件人資訊，再寄送測試信驗證。每個組織可使用不同 SMTP；密碼以 ASP.NET Core Data Protection 加密保存，未設定組織值時才使用環境設定的 `Mail` 區段。

工作區各分頁會以局部內容切換，瀏覽器停用 JavaScript 時仍會退回一般完整導覽。

## 報表與機器可讀匯出

盤查清冊與證據索引 CSV 供試算表使用：文字以公式符號、空白或控制字元開頭時，會加上單引號，避免開啟時被當成公式執行；數值欄位仍保留數值與指定的小數位數。CSV 不是原始資料交換格式，請勿移除此保護後再交由試算表開啟。

需要原始、可重現的計算輸入時，請使用報表頁的 canonical manifest JSON 匯出；JSON 不套用 CSV 呈現轉義，保留原始位元組與 SHA-256。XLSX 文字欄位則使用明確的文字儲存格。

## 技術架構

- .NET 10 / ASP.NET Core Razor Pages
- PostgreSQL 18 / Entity Framework Core
- MinIO / ClamAV / Mailpit
- Docker Compose
