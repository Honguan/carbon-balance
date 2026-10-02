# 實作狀態與原始目的對照

更新日期：2026-10-02

目前以[產品需求](04_PRODUCT_REQUIREMENTS.md)及[技術架構](05_ARCHITECTURE.md)為有效入口。以下 Phase 表保留早期 P0 工程交付範圍，`complete` 不代表 `old` 原始產品需求已全部實作或已通過領域核准；舊 RC 檢核證據也不自動代表本次變更已通過驗證。

| Phase | 狀態 | 已完成範圍 |
|---|---|---|
| 0 | complete | 規劃基線、舊 ZIP checksum、187 筆唯讀來源清單、ADR 與治理 |
| 1 | complete | .NET 10 modular monolith、PostgreSQL、EF migrations、Docker Compose、Golden Vertical Slice |
| 2 | complete | Identity、12 字元 production 長密語與常見密碼拒絕、列舉防護 recovery、TOTP／recovery code、absolute session、獨立 auth throttling、組織與角色治理 |
| 3 | complete | 版本化單位/alias/複合單位、PCR 與係數 review/publish/withdraw/supersede/applicability、staging 匯入 |
| 4 | complete | 五階段活動類型、適用性、供應商/情境、估算、資料品質、Evidence SHA-256/ClamAV/MinIO |
| 5 | complete | decimal 計算、受控換算、分配、canonical manifest/hash、不可變 run、lineage/diff、警告與品質摘要 |
| 6 | complete | Draft/Submitted/ChangesRequested/Approved、角色限制、CSV、Evidence index、manifest、可歸檔 HTML 報告與 audit |
| 7 | complete（係數 staging） | Legacy raw/staging/validate/conflict、checksum、防重、CLI、映射與差異分類；本次已找到三類五階段候選工作簿，尚未完成領域對帳或整本匯入 |
| 8 | complete | threat model、CI 安全閘門、SBOM、環境範例、runbooks、空庫/升級/備份還原、效能與 WCAG 基礎稽核 |

## Issue #53：CSV 匯出安全（2026-10-02）

- 盤查清冊與證據索引共用試算表安全編碼；公式、前置空白／控制字元及全形公式符號加上文字前綴，真正的數值維持數值。
- 新增 25 個編碼案例，並以 PostgreSQL 匯出流程驗證盤查 metadata、證據檔名、報表小數位數及 manifest 原始位元組／hash 不變。
- 本機 .NET 10.0.302：locked restore、Release build（0 warning／error）、format、158 個測試全部通過（Unit 107、Integration 23、Security 20、Architecture 3、Contract 1、GoldenCases 4）；獨立 PostgreSQL 18.4 空庫套用 20 筆 migration。本次未改資料表、公式或既有快照。
- 未執行 Excel／LibreOffice 桌面人工驗收；以下既有領域與 UAT 發布閘門仍保留。

## 剩餘發布閘門

早期 P0 RC 的人工 UAT 仍待產品負責人與碳足跡領域審查者執行及簽核。依此次原始目的分析，另有下列能力／領域缺口，不能再以「只剩 UAT」代表整體產品完成：

| 項目 | 目前狀態 |
|---|---|
| 快照讀取責任 | 已移至 Infrastructure，Application 契約供計算、送審與 freshness 共用；其餘 Web 編排未全面遷出 |
| BOM 與材料彙整 | 有原物料活動列，尚無完整 BOM 結構與供應來源彙整流程 |
| 全廠製造數據分配 | 有分配比例／方法／理由；尚缺結構化分子、分母、產量及每宣告單位正規化的完整流程 |
| 使用模式與廢棄情境 | 有情境文字與候選活動量公式；尚無完整可重用模式與處理占比模型 |
| 歷史案例對帳 | 已辨識工作簿、錯誤參照及外部連結；未核准正式 Golden 答案 |
| 改善分析 | 有 run 差異，尚不等於完整熱點／減碳情境產品功能 |

本次原始目的分析與後續計畫見 [old 來源分析](architecture/LEGACY_PURPOSE_ANALYSIS.md)。人工發布閘門仍依：

- 執行方式：[`docs/release/P0_UAT_PLAN.md`](release/P0_UAT_PLAN.md)
- 簽核紀錄：[`docs/release/P0_UAT_SIGNOFF.md`](release/P0_UAT_SIGNOFF.md)
- 發布檢核：[`docs/release/P0_RC_CHECKLIST.md`](release/P0_RC_CHECKLIST.md)
- 外部決策：[`docs/DECISIONS_NEEDED.md`](DECISIONS_NEEDED.md)

人工 UAT 未完成前，狀態只能是「可供 UAT 的 P0 Release Candidate」，不得標記為正式發布、第三方查驗通過、主管機關核定或可使用碳足跡標籤。

## 本次架構調整的驗證（2026-09-07）

使用專案本機 .NET SDK 10.0.302 與獨立 PostgreSQL 18.4 測試容器，不連接既有業務資料庫。

| 檢查 | 結果 |
|---|---|
| `dotnet restore --locked-mode` | 通過 |
| `dotnet build --configuration Release --no-restore` | 通過，0 warning、0 error |
| `dotnet format --verify-no-changes --no-restore` | 首次發現新增測試空白／換行格式，修正後通過 |
| 空測試資料庫套用現有 migrations | 18 筆全部套用；本次無 schema 變更 |
| `dotnet test --configuration Release --no-build` | 105 passed、0 failed、0 skipped：Unit 63、Integration 23、Architecture 3、Security 11、Contract 1、GoldenCases 4 |
| 新增快照測試 | 精度手算、來源／PCR／係數映射、唯讀、變更偵測、舊快照不變及跨組織拒絕 |
| 文件與來源 | 本機連結檢查、11份原始 DOCX／XLSX hash核對及 `git diff --check` 通過 |

公式與資料表未變動；未重做正式資料庫升級／還原演練、人工 UAT、完整瀏覽器 E2E 或對外發布。舊案例不因工程測試通過而成為經核准的 Golden Case。
