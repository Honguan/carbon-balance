# 來源清單

> 建立日期：2026-07-18

本清單只保存歷史來源的識別、checksum、用途與處置。舊 ZIP、展開內容、憑證、客戶資料、完整 ISO 文件及授權不明係數均不提交至 repository。

| ID | 來源 | 大小 | SHA-256 | 用途 | 敏感性 | 可提交 |
|---|---|---:|---|---|---|---|
| SRC-001 | `public_html_Carbon-master.zip` | 6,868,969 bytes | `8646b612813ed38b177286cabe9a5a5a4da23e64030afb5011c158ad152e66db` | 歷史需求、資料映射、UI、Golden Case 候選 | 高：含憑證風險、SQL 與未知資料 | 否 |
| SRC-002 | 原 `carbon-footprint-rebuild-plan/` | 29 files | `a4c5a78f9f4fb490d6a6441f55b1e79f81bc1a2d47293de22b1b8bc9f7badebc` | 規劃基線 | 內部文件 | 是，已置於 `docs/planning-baseline/` |
| SRC-003 | `CODEX_GOAL.md` | 21,253 bytes | `9becd3688c4089008c24226dd916dc74170b4ef1f80bc9f629e6c6cea6130db8` | 本次執行目標的本機副本 | 內部工作指令 | 否，已忽略 |

## 舊 ZIP 處置

- 展開位置：`.analysis/legacy/`，由 `.gitignore` 排除。
- 檔案數：187；展開大小：15,267,478 bytes。
- 逐檔 SHA-256 與風險分類：[legacy-source-inventory.csv](docs/migration/legacy-source-inventory.csv)。
- 舊 PHP 不得放入正式 `src/`，舊 SQL 不得 restore 至正式 schema。
- 發現的 credential risk 只記錄位置，不記錄值；詳見 [legacy-secret-inventory.md](docs/compliance/legacy-secret-inventory.md)。

## 完整性規則

重新取得來源時必須重算 SHA-256。checksum 不符即視為新來源版本，新增 manifest 記錄，不覆寫既有記錄。
