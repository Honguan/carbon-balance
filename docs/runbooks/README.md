# 維運手冊

本目錄定義 P0 Release Candidate 的部署、回復與事件處理基線。所有正式環境密碼、資料庫連線、物件儲存金鑰與 SMTP 憑證均由祕密管理服務注入，不得寫入版本庫。

正式部署前必須依 [驗證與工作階段政策](../security/AUTHENTICATION_POLICY.md) 設定實際 trusted proxy IP；直接對外時保持空清單，不得信任任意 forwarded header。

## 部署 Profile 與網路邊界

`docker-compose.yml` 只供本機開發，所有 published ports 均綁定 `127.0.0.1`。`docker-compose.production.yml` 是 hardened production 範例，只包含 `migrate`、獨立 `factor-sync` 與 `web`；PostgreSQL、物件儲存管理介面、Mailpit 與 ClamAV 不得由此 profile 對外發布。

| 服務 | 開發環境 | 正式環境 |
| --- | --- | --- |
| Web | `127.0.0.1:8088` | `127.0.0.1:${APP_HOST_PORT}`，僅供同機 reverse proxy |
| PostgreSQL | `127.0.0.1:${POSTGRES_HOST_PORT}` | 外部受管服務／私有網路，不發布 |
| SeaweedFS S3 API | `127.0.0.1:9000`；不發布管理介面 | HTTPS 物件儲存；管理介面不發布 |
| Mailpit SMTP／UI | `127.0.0.1:1025`／`127.0.0.1:8025` | 不部署；SMTP 必須啟用 TLS |
| ClamAV | 僅 Compose 私有網路 | 私有隔離網路或同 pod TLS sidecar，不發布 |

正式環境以 `docker compose -f docker-compose.production.yml config` 預檢。必須提供 immutable `CARBON_APP_IMAGE`、`ALLOWED_HOSTS`、`SECRET_PROVIDER` 與所有 `${...:?}` 變數；實值由已核准的 secret provider 注入，不使用 production `.env`。PostgreSQL 連線必須為 `SSL Mode=VerifyFull`，物件儲存必須為 HTTPS，SMTP 必須啟用 TLS，Data Protection `/keys` volume 必須持久化並納入備份。

Reverse proxy 必須終止公開 HTTPS 並傳送 `X-Forwarded-Proto`；`TRUSTED_PROXY_IP` 必須填入應用程式容器實際看到的單一 proxy 來源 IP，不能假設 Docker NAT 後仍為 `127.0.0.1`。若 proxy 不在同機，平台必須改用私有 ingress、限制 web ingress 來源，並在 proxy 到應用程式間重新加密；不得把應用程式或基礎服務改成 wildcard host binding。

## 部署與資料庫遷移

1. 確認映像 digest、內嵌 Git commit provenance、SBOM、測試與 Critical/High 掃描均通過；正式映像缺少完整 commit SHA 時應用程式必須拒絕啟動。
2. 執行 `scripts/migration-preflight.ps1`，確認 EF 模型與遷移一致。
3. 執行 `scripts/backup.ps1` 並將 SHA-256 記入變更單。
4. 先執行單次 `migrate` 工作，只套用 schema 與必要靜態種子，不需要 Internet；其成功後 Web 即可啟動。
5. 獨立 `factor-sync` 工作執行 `--sync-factors --deployment`。從其日誌與 `identity.system_audit_events` 查 `resource_type = 'FactorSynchronization'`，依批次、時間及來源確認開始、失敗、重試與完成事件，以及來源版本、輸入雜湊及新增／未變更筆數；全新空庫顯示零組織屬預期結果。同步中斷不阻擋 Web，也不清除最後可用目錄。
6. 驗證 `/health/live`、`/health/ready`、登入、Golden Case、已發布係數與報表總額。

下載遇到 DNS／網路錯誤、HTTP 408／429／5xx 或逾時，最多嘗試三次、退避 1／2 秒；格式、分頁超限或資料套用失敗直接回報，修正原因後重試。Migration 已完成後，操作人員執行 `docker compose -f docker-compose.production.yml run --rm --no-deps factor-sync --sync-factors`，不需重跑 migration；失敗回傳非零。`MOENV_IMPORT_ON_DEPLOYMENT=false` 只停用部署自動同步，明確重試命令仍可執行。停用不刪除既有版本，回滾保留已產生的係數版本與稽核事件。

受控原因碼 `source-schema-invalid`／`metadata-schema-invalid` 表示來源 JSON 不符格式；`source-page-limit` 表示需確認下載範圍；`metadata-distribution-missing`／`metadata-download-missing`／`metadata-url-invalid` 表示需確認官方下載詮釋資料。其他失敗保留例外類型、階段及 HTTP 狀態，禁止直接記錄含 URL／金鑰的任意例外訊息。最後成功時間及來源版本由最近一筆 `factor.synchronization.completed` 系統事件查得；下載失敗沒有可驗證輸入時，輸入雜湊為空。

組織 SMTP 可由工作區「郵件服務設定」分頁維護。寄件密碼只保存 Data Protection 密文；正式環境仍應優先使用秘密管理服務注入的預設 `Mail` 設定，並以測試信及稽核事件確認變更。

## 回滾

應用程式回滾使用前一個已核准的 immutable image digest。資料庫遷移預設只向前修復；若新版本寫入不相容資料，立即停止流量，從部署前備份還原至新的資料庫實例，驗證後切換連線。不得直接在唯一正式資料庫執行破壞性 down migration。

## 備份與還原演練

使用 `scripts/backup.ps1` 產生 PostgreSQL custom-format 備份與 SHA-256。以 `scripts/restore-rehearsal.ps1 -BackupPath <path>` 還原到名稱受限的演練資料庫，確認 app、identity、staging 資料表存在。物件儲存需另以版本化 bucket replication 備援，並抽查資料庫 Evidence SHA-256 與物件內容一致。

## 事件處理

1. 分級：Critical（跨租戶、資料外洩、計算結果竄改）、High（核心服務中斷或無法復原）、Medium/Low。
2. 封鎖：撤銷工作階段與受影響憑證、隔離流量、保全唯讀稽核紀錄。
3. 調查：以 correlation ID、組織、使用者與時間範圍查詢；禁止在紀錄中輸出密碼、token、連線字串或證據內容。
4. 復原：依已驗證備份建立新實例，執行遷移預檢與 Golden Case，再恢復流量。
5. 通報與改善：記錄時間線、影響、根因、修復、驗證與後續責任人。

## 復原目標

P0 基線為 RPO 24 小時、RTO 4 小時；正式上線前由業務負責人確認是否足夠。每季至少執行一次資料庫與物件證據聯合還原演練。
