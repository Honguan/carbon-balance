# P0 人工 UAT 計畫

## 目的

確認 P0 Release Candidate 可由產品負責人與碳足跡領域審查者完成主要業務流程，且畫面、計算、稽核軌跡與匯出內容符合預期。此文件只定義人工驗收方式，不代表第三方查驗、主管機關核定或正式發布。

## 驗收角色

| 角色 | 責任 |
|---|---|
| 產品負責人 | 驗證流程可用性、文案、權限、錯誤提示與發布範圍 |
| 碳足跡領域審查者 | 驗證盤查欄位、生命週期資料、PCR／係數治理、計算與報表語意 |
| 測試協助者 | 準備隔離環境、測試資料、證據檔與操作紀錄，不代替簽核 |

## 前置條件

- 使用與正式環境隔離的 UAT 資料庫、物件儲存、郵件與 ClamAV。
- 使用非正式憑證及非客戶資料，不匯入授權未確認的係數。
- 記錄 Release commit SHA、容器 image digest、migration 版本與測試日期。
- 最新 CI 與 `docs/release/P0_RC_EVIDENCE.md` 所列工程閘門均通過。
- DEC-003 的舊憑證撤銷證據若尚未取得，不得核准正式發布。

## 必測情境

| ID | 情境 | 驗收重點 | 必留證據 |
|---|---|---|---|
| UAT-001 | 註冊、Email 確認、登入與登出 | 未確認帳號不得進入受保護流程；錯誤訊息可理解 | Mailpit 郵件、畫面截圖 |
| UAT-002 | TOTP MFA 與 recovery code | 啟用、挑戰、復原與敏感操作重新驗證正常 | MFA 設定與挑戰紀錄 |
| UAT-003 | 組織、邀請、撤銷與角色 | Owner、Editor、Reviewer、Viewer 權限符合矩陣；跨租戶不可見 | 各角色操作結果 |
| UAT-004 | 廠場、產品與盤查版本 | 必填欄位、版本狀態、期間與功能單位可正確保存 | 盤查基本資料截圖 |
| UAT-005 | 單位、PCR 與係數治理 | Draft、review、publish、withdraw、supersede、適用期與來源資訊清楚 | 版本歷程與審核紀錄 |
| UAT-006 | 五階段活動資料 | 原物料、製造、運輸、使用、廢棄回收皆可輸入；不適用、估算、供應商與情境欄位語意正確 | 每階段至少一筆資料 |
| UAT-007 | 證據上傳 | 合法檔案掃描、SHA-256、物件儲存與活動綁定正常；不合法檔案被拒絕 | Evidence index、拒絕訊息 |
| UAT-008 | 計算與重算 | decimal 精度、單位換算、分配、警告、資料品質摘要與總量合理 | CalculationRun、manifest hash |
| UAT-009 | 不可變性與 lineage | 已建立 run 不可修改；輸入變更建立新 run；supersedes 與 diff 正確 | 兩次 run 與差異結果 |
| UAT-010 | 送審與退回 | Draft、Submitted、ChangesRequested、Approved 的角色與轉移限制正確 | 狀態歷程、audit trail |
| UAT-011 | 匯出與可歸檔報告 | CSV、Evidence index、manifest、HTML 報告內容一致且可追溯 | 全部匯出檔及 SHA-256 |
| UAT-012 | Legacy staging | 來源 checksum、防重、Invalid、Conflict、映射與差異分類可理解 | 匯入摘要與錯誤報告 |
| UAT-013 | 失敗與復原流程 | 服務不可用、上傳失敗、資料驗證失敗時不產生誤導性成功狀態 | 錯誤畫面與日誌摘要 |
| UAT-014 | 基礎無障礙與可用性 | 鍵盤操作、焦點、標籤、錯誤提示、表格與主要流程無阻擋問題 | 問題清單或通過紀錄 |

## Golden Case 核對

至少重跑既有 Golden Case，核對：

- 產品總額為 `7 kgCO2e`。
- 相同輸入產生相同 canonical manifest hash。
- 修改輸入後建立新的 `CalculationRun`，舊 run 保持不變。
- supersedes lineage 與 run diff 可追溯。
- 畫面、CSV、manifest 與可歸檔 HTML 報告總額一致。

## 缺陷分級

| 等級 | 定義 | RC 處置 |
|---|---|---|
| Blocker | 資料遺失、跨租戶存取、計算錯誤、不可追溯、敏感資料外洩或主流程無法完成 | 必須修正並完整重測 |
| Major | 主要功能可繞過但結果不可靠、重要權限或報表語意錯誤 | 必須修正並重測相關情境 |
| Minor | 不影響結果與稽核性的文案、版面或低風險可用性問題 | 可建立後續 Issue；需由簽核者接受 |

## 退出條件

- UAT-001～UAT-014 全部有結果與證據。
- Blocker 與 Major 為零。
- Minor 均有 Issue、負責人與處置決策。
- `P0_UAT_SIGNOFF.md` 填妥 commit SHA、環境、結果及簽核者。
- `P0_RC_CHECKLIST.md` 最後一項只能在實際簽核後勾選。
- 正式部署前仍須完成 `docs/DECISIONS_NEEDED.md` 中適用的外部決策。

## 證據保存建議

將證據保存在不含 secrets 與客戶資料的受控位置，至少包含：

- 操作截圖或錄影索引。
- 匯出檔 SHA-256。
- 測試帳號角色矩陣。
- 缺陷清單及重測結果。
- Release commit SHA、image digest、migration 版本與日期。
