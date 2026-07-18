# 初始 Backlog

## Epic 0：Legacy 封存與治理

- [ ] SEC-001 撤銷舊 Google Maps API Key，建立 secret inventory。
- [ ] LEG-001 對 ZIP、SQL、Excel、PDF 建立 SHA-256 manifest。
- [ ] DOC-001 建立雲端 document register 與 canonical 決策。
- [ ] LEG-002 將舊 repository 設為唯讀／封存。
- [ ] GOV-001 建立新 repo ruleset、CODEOWNERS、Issue/PR templates。

## Epic 1：專案骨架

- [ ] ARC-001 建立 .NET 10 solution 與模組目錄。
- [ ] ARC-002 建立 PostgreSQL 18、EF Core 與 migration pipeline。
- [ ] DEV-001 建立 Docker Compose 開發環境。
- [ ] CI-001 建立 PR build/test/migration/security workflow。
- [ ] OPS-001 建立 structured logging、health checks 與 OpenTelemetry 基礎。

## Epic 2：身分與組織

- [ ] IAM-001 使用者註冊、email confirmation、登入與 lockout。
- [ ] IAM-002 MFA 與 recovery code。
- [ ] ORG-001 組織、成員、角色與邀請。
- [ ] IAM-003 Resource authorization 與跨租戶負面測試。
- [ ] FAC-001 廠場管理。

## Epic 3：產品與盤查專案

- [ ] PRD-001 產品與產品版本。
- [ ] INV-001 盤查專案、版本、期間與狀態。
- [ ] INV-002 功能單位／標示單位。
- [ ] INV-003 系統邊界與生命週期流程。
- [ ] INV-004 分配設定與理由。
- [ ] INV-005 排除、估算、替代資料與審核狀態。

## Epic 4：單位與係數

- [ ] UNT-001 單位目錄、alias 與維度換算。
- [ ] FAC-001 係數來源與 dataset。
- [ ] FAC-002 係數及版本資料模型。
- [ ] FAC-003 CSV/Excel import staging 與錯誤報告。
- [ ] FAC-004 係數審核、發布、撤銷與 supersede。
- [ ] FAC-005 係數搜尋、篩選與適用性警告。

## Epic 5：五階段資料

- [ ] LCA-001 原料投入與供應商。
- [ ] LCA-002 原料運輸。
- [ ] LCA-003 製造產量、能資源與分配。
- [ ] LCA-004 製造廢棄物與委外運輸。
- [ ] LCA-005 配送銷售運輸。
- [ ] LCA-006 使用情境。
- [ ] LCA-007 廢棄處理情境。
- [ ] EVD-001 證據上傳、checksum、scan 與關聯。

## Epic 6：計算引擎

- [ ] CAL-001 Calculation input manifest 與 hash。
- [ ] CAL-002 受控單位換算。
- [ ] CAL-003 原料階段計算。
- [ ] CAL-004 製造階段計算與分配。
- [ ] CAL-005 配銷計算。
- [ ] CAL-006 使用情境計算。
- [ ] CAL-007 廢棄情境計算。
- [ ] CAL-008 不可變 run、line item、stage summary。
- [ ] CAL-009 warning、assumption、exclusion summary。
- [ ] CAL-010 run diff 與重現驗證。

## Epic 7：PCR 與規則

- [ ] PCR-001 PCR metadata、版本與原始檔。
- [ ] PCR-002 CCC Code 與適用性記錄。
- [ ] PCR-003 rule requirement template。
- [ ] PCR-004 過期、撤回與新版本警告。
- [ ] STD-001 標準版本 register。

## Epic 8：審核與報告

- [ ] REV-001 Review task、comment、補正與核准。
- [ ] RPT-001 盤查清冊匯出。
- [ ] RPT-002 盤查報告與逐項追溯。
- [ ] RPT-003 證據索引與查驗包。
- [ ] VER-001 查驗聲明 metadata 與狀態。
- [ ] APR-001 主管機關核定 metadata、期限與提醒。

## Epic 9：Migration

- [ ] MIG-001 建立 legacy staging schema。
- [ ] MIG-002 匯入舊係數資料並產生衝突報告。
- [ ] MIG-003 匯入 sample inventory。
- [ ] MIG-004 建立瓶裝水、滑鼠、衛生紙、電鍋 Golden Case。
- [ ] MIG-005 legacy/new result reconciliation。

## Epic 10：正式化

- [ ] SEC-010 Threat model 與 penetration test。
- [ ] OPS-010 Backup/restore rehearsal。
- [ ] OPS-011 Production runbook 與 incident response。
- [ ] PERF-001 代表性資料效能測試。
- [ ] A11Y-001 WCAG 2.2 AA audit。
- [ ] REL-001 Release Candidate 與 Pilot UAT。
