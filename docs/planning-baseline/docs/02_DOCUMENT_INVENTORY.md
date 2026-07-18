# 雲端文件分類與權威順序

## 1. 文件分類原則

雲端資料夾包含正式報告、需求文件、論文、PCR、試算表、簡報、競賽稿、個人心得、行政文件與大量副本。不能以「修改時間最新」或檔名中的「最新」直接判斷內容有效。

每份文件應加入 metadata：

```yaml
category: standard | requirement | design | data | test-case | presentation | admin | archive
status: canonical | reference | superseded | duplicate | historical | unknown
owner: domain | product | engineering | admin
source_date: YYYY-MM-DD
valid_from: YYYY-MM-DD | null
valid_to: YYYY-MM-DD | null
reviewed_at: YYYY-MM-DD | null
reviewed_by: string | null
```

## 2. A 類：核心需求與研究依據

### A1. 實務專題報告書.docx

用途：最完整的歷史需求、系統分析、UCD、事件表、情節描述、實作案例、論文與成果彙整。

狀態：**歷史需求基準**，不是現行法規基準。

應提煉：

- 問題陳述。
- 五階段生命週期模型。
- 37 個功能候選。
- 使用者／管理者／供應商協作情境。
- 共通架構符號、公式與案例。
- 四個實作案例的操作與例外流程。

### A2. 2023-09-03_系統概述文件_彙整共用.docx／PDF

用途：產品定位、核心功能、技術背景、使用對象、共通架構符號。

狀態：**歷史產品概述**。

### A3. icim_根據產品碳足跡量化規則之計算共通架構_論文全文.pdf

用途：研究方法、架構合理性與公式來源。

狀態：**研究參考**；公式仍需以現行 ISO／PCR 重新審查。

### A4. 產品碳排不卡關.docx／PDF

用途：對外產品敘述與展示用摘要。

狀態：**歷史產品敘述**。

## 3. B 類：需求與系統分析

| 文件 | 分類 | 建議處理 |
|---|---|---|
| 2023-09-21 需求與功能對應.docx | Requirement | 轉為 Epic、Story、Acceptance Criteria |
| 需求功能對應.docx | Duplicate/variant | 與前者 diff，保留較完整版本 |
| 2023-06-05_UCD.pptx | Design | 轉成 Mermaid／PlantUML，重新核對角色 |
| 最新事件表.pptx | Requirement | 轉成事件與命令清單，移除「最新」命名 |
| 設定碳足跡係數實作.docx | UX/Test Case | 轉成係數搜尋與選用驗收案例 |
| 系統分析展示簡報.pptx | Presentation | 只作圖像與流程參考 |
| 2023-09-03 三.系統功能簡介.docx | Requirement summary | 與報告合併去重 |

## 4. C 類：資料模型與資料字典

| 文件 | 狀態 | 問題／用途 |
|---|---|---|
| 正規化.docx | Reference | 是欄位清單，不是完整正規化模型 |
| 正規化 (1).docx | Variant | 與 canonical 版 diff |
| 做到一半的資料詞彙正規化-2NF(蔡).docx | Draft | 只取詞彙線索 |
| 符號.docx | Reference | 轉成 domain glossary |
| 架構.docx | Unknown | 需確認是否誤放／是否為學長姐文件 |

新的資料字典必須補上：型別、必填、單位、來源、版本、關聯、唯一性、權限、稽核、保存期限與個資分類。

## 5. D 類：PCR、法規與係數來源

| 文件 | 用途 | 狀態 |
|---|---|---|
| 20-058瓶裝水第5.0版.pdf | 瓶裝水案例 PCR | 歷史參考；文件載明核准日 2021-02-08、有效期五年，2026 開發不得直接假定有效 |
| 碳係數明細-全.pdf | 係數來源候選 | 需確認來源日期、授權、版本與是否仍有效 |
| 泰山企業-2022ESG永續報告書 | 案例佐證 | 歷史外部資料，不是計算規則 |
| icim 論文／專題報告引用標準 | 標準索引 | 必須改用官方現行版本清單 |

正式系統應以環境部 PCR 查詢與係數資料庫為更新來源，並保存每次抓取／匯入的來源 URL、時間、checksum 與原始檔。

## 6. E 類：Golden Case 與測試資料

### 優先保留

- 泰山飲用水系列試算表。
- 滑鼠一般版／美化版／製造階段更新系列。
- 衛生紙展示系列。
- 使用階段－電鍋。

處理方式：

1. 先找出每組最後有效版本，禁止只看檔名。
2. 建立檔案 checksum 與欄位對照。
3. 由領域人員確認公式、單位、係數與預期結果。
4. 轉為機器可讀 fixture。
5. 原始檔保存在 private test-data storage；repository 只放可公開且去識別的最小資料集。

## 7. F 類：簡報、競賽與發表成果

包括：

- 期末競賽簡報、濃縮版、原版。
- NSDB 簡報與回應。
- 專題成果海報、ICIM 海報。
- 競賽稿件與講稿。
- 作品集。

用途：產品敘事、歷史成果與 UI 圖像。不得覆蓋正式需求。

## 8. G 類：行政與個人文件

包括：

- 專題心得。
- 在學證明。
- 影像授權書。
- 物品借用清單。
- 確認單、書背。

處置：移出工程規格資料夾，限制權限；不得複製到公開 GitHub。

## 9. H 類：重複與封存

大量 `(1)`、`(2)`、日期版本與內容相同的試算表應執行：

1. SHA-256 去重。
2. 可解析文件做文字 diff。
3. 選定 canonical 版本。
4. 其餘標記 duplicate／superseded，不立刻刪除。
5. 建立 `document-register.csv`，記錄決策與原因。

## 10. 建議的新文件權威結構

```text
docs/
├─ product/
│  ├─ vision.md
│  ├─ personas.md
│  ├─ requirements.md
│  └─ glossary.md
├─ domain/
│  ├─ lifecycle-model.md
│  ├─ calculation-rules.md
│  ├─ unit-rules.md
│  ├─ factor-rules.md
│  └─ pcr-rules.md
├─ compliance/
│  ├─ standards-matrix.md
│  ├─ taiwan-regulations.md
│  └─ evidence-and-retention.md
├─ architecture/
│  ├─ overview.md
│  ├─ data-model.md
│  └─ adr/
├─ operations/
│  ├─ deployment.md
│  ├─ backup-restore.md
│  └─ incident-response.md
└─ test/
   ├─ golden-cases.md
   └─ acceptance-scenarios.md
```
