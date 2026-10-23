# 物件儲存佈建與證據上傳界線（#58）

## 佈建（明確的部署步驟）

`scripts/provision-object-storage.sh` 於物件儲存的管理界線內執行，Web 容器不參與：

1. 建立證據 bucket（已存在時略過）。
2. 重建應用程式身分 `carbon_app`，只授與：
   - `Write:<bucket>/organizations/*`、`Read:<bucket>/organizations/*`
   - `Read:<bucket>/system/readiness-sentinel*`
3. 寫入 `system/readiness-sentinel`，供 `/health/ready` 讀取。

Compose 以一次性 `object-storage-provision` 服務執行，Web 必須等它成功完成。其他環境（含 CI e2e）以
`docker exec -i … sh -s < scripts/provision-object-storage.sh` 執行；腳本可重複執行。
正式環境的外部物件儲存須依同一組權限由部署流程建立身分，Web 只接收 `OBJECT_STORAGE_APP_*` 與 HTTPS endpoint
（`Deployment:Hardened` 已要求 HTTPS）。

## 上傳管線

- `multipart/form-data` POST 先通過非等待的 process-wide 名額（預設 2 個，超額立即 503 + `Retry-After`），
  名額與 90 秒總期限涵蓋整個請求；請求主體上限 12 MiB（含 multipart 框架）。
- 內容分段寫入隔離暫存檔並同時計算 SHA-256（64 KiB 緩衝），檔案上限 10 MiB 以實際讀取位元組計算；
  之後由暫存檔交給 ClamAV 並上傳，暫存檔於 `finally` 刪除。記憶體用量與總上傳位元組數無關。
- 只有掃描回覆為完整 frame 且為 `OK`、上傳成功並提交資料庫後，才建立可用的證據關聯。
- 使用者只會看到 `EvidenceUploadException` 的安全訊息；基礎設施例外不再回顯到畫面。

## 已驗證的權限矩陣（SeaweedFS `dev` 快照 digest，應用程式身分）

| 操作 | 結果 |
| --- | --- |
| 上傳 `organizations/…`（小檔與 10 MiB multipart） | 允許 |
| 讀取 sentinel | 允許（預簽名 ranged GET） |
| 覆寫 sentinel、讀寫其他前綴、建立／刪除／列出 bucket 內容 | 拒絕 |

## 已知限制

- SeaweedFS 的 `Write` 動作同時涵蓋刪除，無法只授與 put 而排除 delete；應用程式身分可刪除 `organizations/` 下的物件。
- 身分可列出 bucket 名稱（`ListBuckets`），但無法讀取其他 bucket 內容。
- Minio SDK 的 stat／get 會先送 bucket 層級 HEAD，故 readiness 改用預簽名 GET。
- 資料庫提交失敗後已上傳的物件視為孤立物件（不可由應用程式下載）；清理需由離線程序比對資料庫，尚未實作。
  #55（掃描與儲存期間的專案狀態競爭）未解決。
- 擴充性壓測僅涵蓋單元層級（記憶體配置、逾時）；多個並行最大檔案的 HTTP 負載測試尚未執行。
