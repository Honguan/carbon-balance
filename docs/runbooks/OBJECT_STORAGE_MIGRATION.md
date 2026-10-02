# 物件儲存遷移與復原

本機與 CI 使用 SeaweedFS 4.48（固定映像 digest），維持既有 S3 存取介面、bucket、object key、資料庫附件 metadata 與 SHA-256。正式環境仍由外部受管 S3 服務提供 TLS、持久化及備份；本機單節點 Compose 不是正式高可用方案。

MinIO 上游已封存，本專案原先固定的 `RELEASE.2025-09-07T16-13-09Z-cpuv1` 映像在 Docker Hub／Quay 均無法下載，不能把本機快取當成乾淨部署可用的證據。來源：[MinIO](https://github.com/minio/minio)、[SeaweedFS mini](https://github.com/seaweedfs/seaweedfs/wiki/Quick-Start-with-weed-mini)、[rclone copy](https://rclone.org/commands/rclone_copy/)、[rclone check](https://rclone.org/commands/rclone_check/)。

## 舊附件遷移（不自動執行）

遷移工具固定為官方 rclone `v1.76.0-beta.10447.0b8e9c4cc` 的映像 digest，不追蹤浮動 beta。選擇原因：2026-10-02 掃描穩定版 1.75.1 發現 OpenSSL／gRPC 三項可修復 High，此固定 build 已無同級可修復漏洞，且另有 copy／下載驗證／重啟與衝突拒絕演練。它仍是預發布工具，正式附件操作前需先用備份副本演練；不以此宣稱上游完整測試或正式 UAT 已完成。後續穩定版須重新掃描、演練後才能更新 pin。

1. 停止 Web 寫入與背景工作，備份 PostgreSQL、原本 `.env` 及 MinIO volume。保留舊容器與其既有映像；**不要**執行 `docker compose down -v`、清除映像或把 MinIO volume 掛到 SeaweedFS。
2. 讓既有 MinIO 透過原本的 S3 endpoint 提供來源資料；新 SeaweedFS 使用獨立 `object-storage-data` volume。可用 `docker compose up -d object-storage` 單獨啟動目標。若舊容器占用主機 9000，先以內部 Docker 網路連線遷移，或使用不同 loopback port；不要先刪除舊服務。
3. 在同一個 PowerShell 工作階段，以秘密管理工具或安全輸入設定以下四個環境變數；不要寫入程式、命令歷史、CI log 或版控：
   - `RCLONE_CONFIG_SOURCE_ACCESS_KEY_ID`、`RCLONE_CONFIG_SOURCE_SECRET_ACCESS_KEY`
   - `RCLONE_CONFIG_TARGET_ACCESS_KEY_ID`、`RCLONE_CONFIG_TARGET_SECRET_ACCESS_KEY`
4. 先預覽，再套用。以下 endpoint 只是範例，必須是 rclone 容器可達的地址；Windows/macOS 可使用 `host.docker.internal`，同一 Compose 網路可用服務 DNS 並指定 `-Network carbon-footprint_default`。

   ```powershell
   ./scripts/migrate-object-storage.ps1 -SourceEndpoint http://legacy-minio:9000 -TargetEndpoint http://object-storage:9000 -Network carbon-footprint_default
   ./scripts/migrate-object-storage.ps1 -SourceEndpoint http://legacy-minio:9000 -TargetEndpoint http://object-storage:9000 -Network carbon-footprint_default -Apply
   ```

   非預設 bucket 必須同時指定 `-SourceBucket`／`-TargetBucket`。腳本只做 `copy --immutable --checksum`，不刪除來源或覆寫不同的既有目標；之後 `check --download --one-way` 下載串流比對每個來源 object 的內容，不只信任 ETag。目標可有額外資料，但來源缺件或內容不符會失敗。
5. 必須看到 `OBJECT_STORAGE_MIGRATION=PASS`，並保存移轉日期、來源／目標、數量與驗證 log（不含秘密）。使用新設定驗證既有附件，再在 `.env` 加入 `OBJECT_STORAGE_MIGRATION_VERIFIED=true`。setup 腳本偵測舊 MinIO volume 時，未有此確認不會啟動完整新堆疊。
6. Web 切換到新 endpoint 後驗證上傳、掃毒及附件 hash。不要修改附件 object key、歷史 manifest 或計算 run。舊資料保留到人工確認及保留期限結束；腳本不自動刪除。

既有來源若無法啟動，停止切換並從受驗證的備份復原；不得以空 bucket 假裝移轉成功。腳本不修改資料庫、`.env` 或服務設定，也不負責搬移 S3 歷史 object versions／IAM policies；若原服務有額外版本保留或法遵鎖定需求，先另行確認移轉方案。

## 備份與復原

- 資料庫沿用 `backup.ps1`／`restore-rehearsal.ps1`；S3 憑證與 storage volume 是另外的備份範圍。
- 最簡單的一致本機備份：停止 Web 與 object-storage 後，備份完整 `object-storage-data` volume（包含 filer metadata、volume 資料與 IAM），再啟動。不要只複製單一 SeaweedFS 資料檔，也不要在寫入中任意打包 volume。
- 復原演練使用新 volume、新 bucket／隔離環境；透過同一支 migration script 的下載比對確認 bytes，再核對資料庫的附件 SHA-256。確認服務重啟後附件仍可讀。
- 切換失敗時停止新寫入，先確認新端是否已新增附件；若有，先反向複製及驗證增量，再回復原 endpoint。單純改回 endpoint 可能遺漏切換期間新上傳的附件。
