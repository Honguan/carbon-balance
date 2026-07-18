# Roadmap 與工期

## 1. 估算假設

估算包含：需求澄清、領域審查、設計、開發、測試、文件、部署與修正；不含等待外部查驗或主管機關審核的時間。

| 團隊 | P0 預估 |
|---|---|
| 1 名全職工程師 + 兼任領域審核 | 約 28–36 週 |
| 2 名工程師 + 1 名領域／QA 兼任 | 約 20–28 週 |
| 3 名工程師 + 產品／領域 + QA | 約 16–22 週 |

工期必須在 Phase 1 完成 Golden Case 與資料模型後重新估算。

## 2. Phase 0：治理與安全封存（1–2 週）

- 新 repository、branch protection、Issue／PR templates。
- 撤銷 secrets、舊系統唯讀化。
- 文件 register、source manifest。
- ADR-0001～0003 核准。
- 建立 threat model 與 data classification。

**Gate：** 舊系統不再接受不安全寫入；權威文件與責任人明確。

## 3. Phase 1：技術骨架與 Golden Slice（3–5 週）

- .NET 10 solution、PostgreSQL、Docker Compose。
- Identity、Organization、Product、Inventory Project 基礎。
- Unit catalogue。
- 最小 Factor Registry。
- 第一個端到端 calculation run。
- CI、migration test、Golden Case test。

**Gate：** 新開發者可啟動系統；單一受控案例可重現計算。

## 4. Phase 2：五階段盤查（5–8 週）

- 原料與供應商運輸。
- 製造能資源、產量、分配、廢棄物。
- 配送銷售。
- 使用情境。
- 廢棄處理。
- 表單驗證、草稿、證據上傳。

**Gate：** 單一產品完整五階段資料可輸入、驗證與計算。

## 5. Phase 3：係數與 PCR 治理（3–5 週）

- 係數匯入 staging、版本、差異、審核與發布。
- PCR metadata、版本、適用性、規則要求。
- 係數搜尋、推薦候選（不自動替使用者決定）。
- 過期／撤銷防護。

**Gate：** 新計算只可使用已發布且適用的版本；歷史 run 不受更新影響。

## 6. Phase 4：審核、報告與查驗準備（3–5 週）

- 內部審核、補正、核准。
- 盤查清冊與報告。
- 證據索引與查驗包。
- calculation diff、版本比較。
- Verification／Approval metadata。

**Gate：** 可交付領域顧問與查驗機構進行試查。

## 7. Phase 5：安全、營運與 Beta（3–4 週）

- MFA、權限矩陣、租戶隔離測試。
- 負載、備份還原、災難復原演練。
- 日誌、metrics、traces、alert。
- Accessibility、UX 修正。
- Pilot organization UAT。

**Gate：** Release Candidate 無 blocker，備份還原成功，UAT 簽核。

## 8. Phase 6：正式發布與後續（持續）

- 受控 migration。
- Production release 與監控。
- 30 天 hypercare。
- 係數／PCR 更新流程。
- P1 供應商協作與查驗強化。

## 9. 不可壓縮項目

即使縮短第一版，也不能省略：

- 正確資料型別與 migration。
- 組織隔離與授權。
- 係數／PCR 版本。
- calculation snapshot。
- Golden Case。
- audit log。
- secret 管理。
- 備份還原驗證。
