# 實作狀態與原始目的對照

更新日期：2026-09-07

目前以[產品需求](04_PRODUCT_REQUIREMENTS.md)及[技術架構](05_ARCHITECTURE.md)為有效入口。以下 Phase 表保留早期 P0 工程交付範圍，`complete` 不代表 `old` 原始產品需求已全部實作或已通過領域核准；舊 RC 檢核證據也不自動代表本次變更已通過驗證。

| Phase | 狀態 | 已完成範圍 |
|---|---|---|
| 0 | complete | 規劃基線、舊 ZIP checksum、187 筆唯讀來源清單、ADR 與治理 |
| 1 | complete | .NET 10 modular monolith、PostgreSQL、EF migrations、Docker Compose、Golden Vertical Slice |
| 2 | complete | Identity、12 字元 production 長密語與常見密碼拒絕、列舉防護 recovery、TOTP／recovery code、absolute session、獨立 auth throttling、組織與角色治理 |
| 3 | complete | 版本化單位/alias/複合單位、PCR 與係數 review/publish/withdraw/supersede/applicability、staging 匯入 |
| 4 | complete | 五階段活動類型、適用性、供應商/情境、估算、資料品質、Evidence SHA-256/ClamAV/S3（SeaweedFS） |
| 5 | complete | decimal 計算、受控換算、分配、canonical manifest/hash、不可變 run、lineage/diff、警告與品質摘要 |
| 6 | complete | Draft/Submitted/ChangesRequested/Approved、角色限制、CSV、Evidence index、manifest、可歸檔 HTML 報告與 audit |
| 7 | complete（係數 staging） | Legacy raw/staging/validate/conflict、checksum、防重、CLI、映射與差異分類；本次已找到三類五階段候選工作簿，尚未完成領域對帳或整本匯入 |
| 8 | complete | threat model、CI 安全閘門、SBOM、環境範例、runbooks、空庫/升級/備份還原、效能與 WCAG 基礎稽核 |

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

## Issue #69：受維護的儲存服務與容器安全更新（2026-10-02）

- 本機與 CI 以固定 digest 的 SeaweedFS 4.48 取代已封存、無法乾淨下載映像的 MinIO。維持 S3 介面，使用獨立 volume；不掛載、不刪除原 MinIO 資料。
- setup 支援新儲存憑證名稱與舊憑證回退；偵測舊 volume 時，必須先完成附件遷移確認才啟動新堆疊。正式環境仍使用外部 TLS S3 服務。
- 新增唯讀預覽、不可覆寫的複製及逐物件下載比對工具與[遷移／復原手冊](runbooks/OBJECT_STORAGE_MIGRATION.md)。來源空 bucket、不同內容的目標物件及比對失敗都拒絕通過；不自動切換應用程式或改動資料庫。
- Web runtime 安裝目前可用的發行版安全更新；原有 High／Critical 漏洞閘門保留，另納入儲存與遷移工具映像。
- 修正既有瀏覽器負向測試：先確認未完成 MFA 的邀請表單停用，再直接送出含有效防偽 token 的 POST 驗證後端拒絕，避免測試卡在 disabled 欄位而未測到授權。盤查／活動按鈕與計算頁盤查選擇器同步至現行 UI，保留原驗證條件。
- 首輪遠端 CI 另揭露 MFA 設定的登入狀態競態：ASP.NET Core 10.0.11 [內建設定頁](https://github.com/dotnet/aspnetcore/blob/v10.0.11/src/Identity/UI/src/Areas/Identity/Pages/V5/Account/Manage/EnableAuthenticator.cshtml.cs) 建立金鑰／啟用 MFA 時更新 security stamp，卻未更新自己的 cookie。新增僅套用該頁的 handler filter，成功變更 stamp 後以框架 API 更新目前 session，不更新其他 cookie、不提升 MFA claim。瀏覽器回歸刻意等待超過 stamp 驗證間隔，檢查目前 session 可完成設定、舊 cookie 被拒絕、設定完成仍須正式 MFA 登入才能管理組織。

本機隔離驗證：locked restore、Release build（0 warning／error）、format、133 項測試（Unit 82、Integration 23、Security 20、Architecture 3、Contract 1、Golden 4）、20 筆空庫 migration、Compose 安全檢查、PowerShell／Bash 語法與 actionlint 通過。Trivy 對 Web、SeaweedFS 與固定遷移工具映像均未發現可修復 High／Critical。

使用一次性測試附件完成 MinIO → SeaweedFS 預覽、複製、bytes 比對及重啟持久化；衝突物件與空來源拒絕測試通過，未認證寫入回傳 403。Windows PowerShell 5.1 的乾淨 setup、舊資料阻擋及舊憑證回退測試通過。Chromium 完整認證／工作區流程（含 PCR 原始文件上傳）、SMTP 中斷、MFA 過期拒絕及前端互動回歸均通過。

固定 rclone 工具為已掃描及演練的預發布 build，限制與更新條件詳見手冊；這些結果不代表正式附件遷移或人工 UAT 已完成。遠端 CI 仍需於 PR 確認。
