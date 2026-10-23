# P0 Release Candidate 驗證紀錄

> 歷史工程紀錄；不是目前版本發布就緒判定。本文未完整提供原始 commit、完整 image digest 與各 artifact 的可驗證位置，不可直接沿用為新候選通過證據。目前閘門與待驗項目見 [RELEASE_READINESS](RELEASE_READINESS.md)。

驗證日期：2026-07-18（Asia/Taipei）
環境：Windows 11、Docker Desktop、.NET SDK 10.0.302、PostgreSQL 18.4

| 閘門 | 實際結果 |
|---|---|
| Release build | 0 warning、0 error |
| 自動測試 | 100/100 通過：Unit 61、Golden 4、Architecture 2、Contract 1、Integration 22、Security 10；CI 產生各測試專案 coverage artifact |
| E2E | PR 工作流驗證註冊、Email 確認、列舉防護密碼重設、TOTP 註冊與挑戰、錯誤 TOTP、一次性 recovery code、password-only／stale MFA 拒絕，以及既有 Workspace 治理流程 |
| 計算一致性 | 產品總額 7 kgCO2e；相同輸入 hash 相同；supersedes lineage 正確 |
| 空庫遷移 | 11 個 EF migrations，建立 28 張 app／identity／staging 資料表 |
| 前版升級 | 從 `20260718100827_AddLifecycleActivityGovernance` 升級到目前 schema，28 張表，通過 |
| 備份還原 | custom-format dump SHA-256 `6fe0a8c8b7665ebf0667b824d0847171f0007e562c2d0296dbdbd549110abe95`；隔離資料庫還原 28 張表，通過 |
| 效能 smoke | `/health/ready` 暖機後 100 次：P50 14.83 ms、P95 16.80 ms、P99 23.96 ms；門檻 P95 500 ms |
| NuGet audit | locked restore、全部 transitive 套件，Critical/High 0 |
| 容器掃描 | Docker Scout，image digest `4deabbce0397`、189 packages，Critical 0、High 0 |
| Secret scan | Gitleaks v8.28.0，15 commits、約 11.33 MB，0 leaks |
| SBOM | 產生 739,978 bytes 的容器 SBOM；CI 另發布 CycloneDX artifact |
| 基礎無障礙 | Lighthouse 首頁 accessibility score 1.00，binary failed audits 0 |

上述保留為歷史本機實測紀錄，不代表目前 commit、領域簽核、人工 UAT 或正式等效部署通過。新候選依 [RELEASE_READINESS](RELEASE_READINESS.md) 重建同版證據並取得適用簽核；不可只將最後一項 UAT 勾選視為完成。
