# Carbon Footprint System 重建計劃

> 產出日期：2026-07-18  
> 狀態：規劃基準版（Planning Baseline）  
> 決策：**正式版重新建立；舊專題程式僅作為需求、資料與驗證案例來源。**

## 1. 專案定位

本專案要建立一套可維護、可稽核、可重現的產品碳足跡盤查與計算系統，核心涵蓋：

1. 產品與盤查專案管理。
2. 原料取得、製造、配送銷售、使用、廢棄處理五大生命週期階段。
3. 排放係數與碳足跡係數的版本、來源、單位、有效期間與證據管理。
4. PCR 規則套用、系統邊界、功能／標示單位、分配原則與切斷準則。
5. 可重現的計算執行、逐項計算明細、資料品質與稽核軌跡。
6. 盤查清冊、盤查報告、查驗準備資料與匯出。
7. 多組織、多使用者、角色權限與供應商協作。

本系統不自行宣稱計算結果已通過官方查驗，也不取代環境部許可之查驗機構。

## 2. 核心決策

| 項目 | 決策 |
|---|---|
| 開發策略 | Greenfield 重建，保留舊版作唯讀參考 |
| 架構 | 模組化單體（Modular Monolith） |
| 後端 | ASP.NET Core 10 LTS / C# |
| 前端 | Razor Pages／MVC，必要互動以小型 TypeScript 模組實作 |
| 資料庫 | PostgreSQL 18 |
| 資料存取 | EF Core；複雜報表可使用明確審查過的 SQL |
| 部署 | Docker Compose 起步；正式環境可移至受管資料庫與物件儲存 |
| 身分驗證 | ASP.NET Core Identity，支援 MFA 與角色／組織權限 |
| 計算 | 獨立 Domain/Application 模組；禁止在畫面載入時隱式寫入或重算 |
| 稽核 | Append-only audit events + 不可變 calculation run snapshot |
| 文件 | 文件即規格；ADR 記錄不可逆架構決策 |

## 3. 文件導覽

| 文件 | 用途 |
|---|---|
| [AGENTS.md](AGENTS.md) | GPT-5.6／Coding Agent 專案指令 |
| [00_EXECUTIVE_DECISION.md](docs/00_EXECUTIVE_DECISION.md) | 重構或重做的結論 |
| [01_LEGACY_CODE_AUDIT.md](docs/01_LEGACY_CODE_AUDIT.md) | 舊程式碼稽核 |
| [02_DOCUMENT_INVENTORY.md](docs/02_DOCUMENT_INVENTORY.md) | 雲端文件分類與權威順序 |
| [03_STANDARDS_AND_COMPLIANCE.md](docs/03_STANDARDS_AND_COMPLIANCE.md) | ISO、PCR、台灣法規、查驗與認證 |
| [04_PRODUCT_REQUIREMENTS.md](docs/04_PRODUCT_REQUIREMENTS.md) | 產品需求與範圍 |
| [05_ARCHITECTURE.md](docs/05_ARCHITECTURE.md) | 技術架構與部署 |
| [06_DATA_MODEL.md](docs/06_DATA_MODEL.md) | 新資料模型與數值規則 |
| [07_MIGRATION_PLAN.md](docs/07_MIGRATION_PLAN.md) | 舊資料遷移策略 |
| [08_ROADMAP.md](docs/08_ROADMAP.md) | 開發階段、工期與里程碑 |
| [09_GITHUB_GOVERNANCE.md](docs/09_GITHUB_GOVERNANCE.md) | GitHub 工作流程與保護規則 |
| [10_TEST_AND_ACCEPTANCE.md](docs/10_TEST_AND_ACCEPTANCE.md) | 測試與驗收 |
| [11_PROMPT_CONFIGURATION_GPT-5.6.md](docs/11_PROMPT_CONFIGURATION_GPT-5.6.md) | GPT-5.6 模型與提示詞設定 |
| [12_BACKLOG.md](docs/12_BACKLOG.md) | Epic 與首批 Issue |
| [13_RISK_REGISTER.md](docs/13_RISK_REGISTER.md) | 風險清單 |
| [14_DEFINITION_OF_DONE.md](docs/14_DEFINITION_OF_DONE.md) | 完成定義 |

## 4. 開始順序

1. 由碳足跡領域人員確認 `03_STANDARDS_AND_COMPLIANCE.md` 與 `04_PRODUCT_REQUIREMENTS.md`。
2. 建立新 repository，不把舊 PHP 專案直接設為新專案根目錄。
3. 將舊版保存於獨立唯讀 repository、tag 或 `legacy-reference` 分支。
4. 完成 ADR-0001～0003 審核。
5. 建立 Phase 0 專案骨架、CI、資料庫 migration 與第一批 Golden Test。
6. 先完成單一組織、單一產品、完整五階段的垂直切片，再擴充協作與管理功能。

## 5. 重要限制

- 任何係數都必須有來源、版本、單位、有效期間與匯入紀錄。
- 計算結果必須能由當時的活動數據、係數版本、PCR 版本與公式版本重現。
- 已提交查驗或已核准的 calculation run 不得原地覆寫。
- 不得將浮點數 `double` 作為碳排計算的主要儲存型別。
- 不得在 Git 中保存 API Key、SMTP 密碼、資料庫密碼或私密文件。
- 未經查驗不得在 UI、報告或行銷文字中標示「已驗證」「符合官方核定」等字樣。
