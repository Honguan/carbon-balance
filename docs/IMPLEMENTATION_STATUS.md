# P0 Release Candidate 實作狀態

更新日期：2026-07-18

| Phase | 狀態 | 已完成範圍 |
|---|---|---|
| 0 | complete | 規劃基線、舊 ZIP checksum、187 筆唯讀來源清單、ADR 與治理 |
| 1 | complete | .NET 10 modular monolith、PostgreSQL、EF migrations、Docker Compose、Golden Vertical Slice |
| 2 | complete | Identity、Email 確認、TOTP MFA/recovery、組織邀請/撤銷、角色、廠場、產品與完整盤查 metadata |
| 3 | complete | 版本化單位/alias/複合單位、PCR 與係數 review/publish/withdraw/supersede/applicability、staging 匯入 |
| 4 | complete | 五階段活動類型、適用性、供應商/情境、估算、資料品質、Evidence SHA-256/ClamAV/MinIO |
| 5 | complete | decimal 計算、受控換算、分配、canonical manifest/hash、不可變 run、lineage/diff、警告與品質摘要 |
| 6 | complete | Draft/Submitted/ChangesRequested/Approved、角色限制、CSV、Evidence index、manifest、可歸檔 HTML 報告與 audit |
| 7 | complete | Legacy raw/staging/validate/conflict、checksum、防重、CLI、映射與差異分類；未發現四類完整舊試算表 |
| 8 | complete | threat model、CI 安全閘門、SBOM、環境範例、runbooks、空庫/升級/備份還原、效能與 WCAG 基礎稽核 |

## 剩餘發布閘門

工程實作與自動驗證目前沒有列入中的未完成 Phase。唯一尚未完成的 RC 閘門是由產品負責人與碳足跡領域審查者執行人工 UAT 並簽核：

- 執行方式：[`docs/release/P0_UAT_PLAN.md`](release/P0_UAT_PLAN.md)
- 簽核紀錄：[`docs/release/P0_UAT_SIGNOFF.md`](release/P0_UAT_SIGNOFF.md)
- 發布檢核：[`docs/release/P0_RC_CHECKLIST.md`](release/P0_RC_CHECKLIST.md)
- 外部決策：[`docs/DECISIONS_NEEDED.md`](DECISIONS_NEEDED.md)

人工 UAT 未完成前，狀態只能是「可供 UAT 的 P0 Release Candidate」，不得標記為正式發布、第三方查驗通過、主管機關核定或可使用碳足跡標籤。
