# 維運手冊

本目錄定義 P0 Release Candidate 的部署、回復與事件處理基線。所有正式環境密碼、資料庫連線、物件儲存金鑰與 SMTP 憑證均由祕密管理服務注入，不得寫入版本庫。

## 部署與資料庫遷移

1. 確認映像 digest、SBOM、測試與 Critical/High 掃描均通過。
2. 執行 `scripts/migration-preflight.ps1`，確認 EF 模型與遷移一致。
3. 執行 `scripts/backup.ps1` 並將 SHA-256 記入變更單。
4. 先執行單次 `migrate` 工作，成功後才更新 `web`。
5. 驗證 `/health/live`、`/health/ready`、登入、Golden Case 與報表總額。

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
