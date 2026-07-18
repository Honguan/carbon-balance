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

目前為可供人工 UAT 的 P0 Release Candidate。正式發布與第三方/主管機關狀態不由系統自動宣稱；產品負責人及領域審查者仍須依 release checklist 完成人工簽核。
