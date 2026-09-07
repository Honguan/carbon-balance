# 工作區體驗與資料流程檢查

日期：2026-09-07。基準：本機 `e65dbfd`；範圍為現行 Razor Pages 工作區、其直接呼叫的盤查／活動／計算／報告流程與相關權限。介面參考方法記錄於 [DESIGN.md](../../DESIGN.md)。

## 已修正

| 問題 | 修正與驗證重點 |
|---|---|
| 登入後仍進入宣傳首頁 | 已登入首頁直接導向工作區，主要導覽標示目前頁面。 |
| 巨型標題、組織 UUID 與分散版本選擇 | 改中性工作區、組織／成員名稱、統一產品名稱＋版號＋期間選擇器。 |
| 分頁與成功提交默默換成最新盤查 | 所有相關連結、POST 與 redirect 保留目前專案，特定紀錄操作回到實際所屬盤查。 |
| AJAX 換頁高亮、訊息與焦點殘留 | 移除不必要 AJAX，使用原生導航與上一頁機制。 |
| 未儲存資料直接遺失 | 離頁提醒；取消切換會恢復原選項；錯誤 POST 的未儲存狀態保留。 |
| 後端錯誤清空表單 | 每個表單以唯一鍵隔離回填，保留文字、數字、選項及 checkbox，排除隱藏值、密碼與檔案。 |
| 連點重複送出／下載後鎖死 | 合法提交設 busy 並阻擋重送；返回頁面解鎖，下載表單維持可使用。此為 UI 防重送，不宣稱一般 API 冪等性。 |
| 隱藏欄位仍阻擋瀏覽器驗證 | 非適用欄位同步停用；切回保留原輸入，「其他」也不再被清空。 |
| 係數搜尋清掉原選取／空白無說明 | 搜尋不因單純隱藏清除已選係數；列表顯示筆數及零結果訊息。 |
| 第二筆盤查固定版號 1 | 依同產品既有版本取下一版，保留唯一索引，併發碰撞回到可重試表單。 |
| 活動無法更正或移除 | 更正建立新紀錄，舊紀錄停用；停用排除後續計算但保留歷史佐證與 run。涵蓋權限、跨組織及凍結狀態。 |
| 審查意見不可見／無權仍可填寫 | 顯示目前盤查意見與時間；表單依實際權限、MFA 與狀態唯讀，後端授權保留。 |
| 歷史匯出混入後續證據／規則 | 指定 run 的 CSV、Excel、Archive 使用快照規則與 activity ID＋證據 hash；Excel 增加證據索引。 |
| 報告識別碼擠壓內容 | 版本與完整 SHA 放進展開細節，總量、時間、規則及匯出操作分欄。 |
| JSONB 重排使原文雜湊失效 | 快照改 text；離線修復須吻合原雜湊。毀損版本明示且禁止匯出。詳見 [ADR-0008](../adr/0008-preserve-canonical-manifest-bytes.md)。 |
| 手機長排放量撐寬／標題出現 Razor 原碼 | 階段標頭可換行，去除展示用數字的無意義尾零，修正混合中文的 Razor 表達式。原始儲存精度不變。 |

## 驗證紀錄

- 瀏覽器 12 項流程：真正 Identity 登入、A/B 切換與跨頁、後端錯誤回填、12 位小數、更正維持有效筆數、停用減一筆。
- 表單回填 9 項、歷史 JSONB 還原 18 項單元測試；整合測試以全新 DbContext 驗證 manifest 原文與 SHA，並檢查角色、組織、凍結狀態及無效版本的 5 個匯出入口。
- 真 PostgreSQL JSONB 往返與修復 8 項檢查：逐位元組還原、原 SHA、追加一次 audit、重跑零更新，以及有不可還原資料時整批不變。
- 全新資料庫 20 migrations → 18 → 20；含活動及佐證資料 18 → 19 → 20。已使用停用欄位時降版正確拒絕，原資料保留；有 run 時降回 JSONB 正確拒絕。
- 最終 restore、Release build、format 驗證與全套 133／133 測試通過，編譯零警告／錯誤。前端互動檢查與 JS 語法通過；1440px 桌面及 360px／390px 手機實際檢查無整頁水平溢出，鍵盤 Enter 換頁保留盤查；隱藏負值欄位不參與 native validation，dirty／reset 離頁事件符合預期。

可重現瀏覽器測試方式見 [E2E 說明](../../tests/E2E/README.md)。所有 UI 與 migration 演練只使用隔離合成資料；沒有修改正式資料庫或外部系統。

## 主要修改檔案與模組

- `src/CarbonFootprint.Web/Pages/Workspace.cshtml(.cs)`、`Index.cshtml.cs`、Shared partials、`wwwroot/css/site.css`、`wwwroot/js/site.js`：工作區、權限狀態、導覽與互動。
- `Web/TagHelpers/PreserveInputTagHelpers.cs`：隔離表單回填。
- `Web/Pages/Reports.cshtml(.cs)`、`ArchiveReport.cshtml(.cs)`：歷史匯出、有效性與錯誤畫面。
- `Domain/Modules/Calculations/CanonicalManifest.cs`、`LegacyManifestRecovery.cs`：快照規則、證據及原文還原。
- `Infrastructure/Persistence/Records.cs`、`InventorySnapshotReader.cs`、`CarbonFootprintDbContext.cs`、`CanonicalManifestRepair.cs` 與兩組 migration：活動停用、原文保存及離線修復。
- `Web/Program.cs`：顯式修復命令；`tests/Unit`、`Integration`、`Security`、`E2E`：回歸驗證；`DESIGN.md`、ADR-0008 與本文：設計及操作紀錄。

## 明確保留的領域缺口

依 [ADR-0007](../adr/0007-lifecycle-workflow-and-snapshot-boundary.md) 與 [既有用途分析](LEGACY_PURPOSE_ANALYSIS.md)，BOM、全廠多產品分配、可重用使用模式、實際處理流向與替代估算情境仍需要受控資料模型及適用 PCR／領域確認。本次沒有把舊版 `/3`、任意平均、未知權重或未確認規則寫入正式計算。

這些是明確的下一階段需求，不以空白模組或假按鈕宣稱完成。本次完成可重現的介面與資料流程缺陷修正，不代表已窮盡所有產品需求、所有角色組合或通過 ISO 認證。
