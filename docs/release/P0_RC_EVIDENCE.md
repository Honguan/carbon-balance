# P0 Release Candidate 驗證紀錄

驗證日期：2026-07-18（Asia/Taipei）
環境：Windows 11、Docker Desktop、.NET SDK 10.0.302、PostgreSQL 18.4

| 閘門 | 實際結果 |
|---|---|
| Release build | 0 warning、0 error |
| 自動測試 | 44/44 通過：Unit 30、Golden 4、Architecture 2、Contract 1、Integration 5、Security 2；已產生 6 份 Cobertura coverage artifact |
| E2E | 註冊、Email 確認、TOTP MFA、PCR／因子發布、五階段活動、證據掃描與儲存、重算、送審、核准、CSV／manifest 匯出通過 |
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

上述為本機實測，不代表人工 UAT 簽核。正式發布仍須完成 `P0_RC_CHECKLIST.md` 的產品負責人與領域審查者簽核，且 CI 必須再次通過相同安全閘門。
