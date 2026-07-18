# 碳衡｜產品碳足跡盤查系統

碳衡（Carbon Balance）用於管理產品生命週期資料、排放係數、CO₂e 計算結果、證據附件與稽核匯出。

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
3. 輸入顯示名稱、Email 與至少 6 個字元的密碼。
4. 開啟 Mailpit：`http://127.0.0.1:8025`
5. 開啟確認信並點擊「確認帳號」。
6. 回到系統登入。

角色規則：

- 系統尚無管理者時，第一個完成註冊的帳號取得 `Administrator`。
- 已有管理者時，新帳號預設為 `Viewer`。
- 登入時可勾選「在這台裝置保持登入 30 天」。

## 服務網址

| 服務 | 網址 |
|---|---|
| 碳衡系統 | `http://127.0.0.1:8088` |
| Mailpit 測試信箱 | `http://127.0.0.1:8025` |
| MinIO Console | `http://127.0.0.1:9001` |
| 健康狀態 | `http://127.0.0.1:8088/health/ready` |

MinIO 帳號與密碼位於 `.env`。

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

## 技術架構

- .NET 10 / ASP.NET Core Razor Pages
- PostgreSQL 18 / Entity Framework Core
- MinIO / ClamAV / Mailpit
- Docker Compose
