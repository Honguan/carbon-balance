# WCAG 2.2 AA 基礎稽核

稽核日期：2026-07-18。範圍為首頁、Identity、Workspace、Reports 與可歸檔報告的 P0 主流程。

| 項目 | 結果 | 證據 |
|---|---|---|
| 語意結構 | 通過 | 頁面使用 heading、section、table、dl、form 與原生 button/link |
| 表單 label | 通過 | 主流程輸入皆以唯一 `for`/`id` 關聯；檔案與審查欄位亦有 label |
| 錯誤提示 | 通過 | validation summary 使用 `role="alert"`，狀態訊息使用 `role="status"` |
| 鍵盤與焦點 | 無阻擋項目 | 操作使用原生 input/select/textarea/button/link，未建立自訂鍵盤元件；Bootstrap 保留 focus 樣式 |
| 對比與可辨識名稱 | 通過 | Lighthouse accessibility score 1.00，binary failed audits 0 |
| 表格與列印 | 通過 | 報表使用 table header；可歸檔頁提供列印按鈕並保留文字版 metadata |

Lighthouse 在 Windows 清理暫存 profile 時回報 EPERM，但完整 JSON 報告已成功寫出並解析；此清理警告不影響稽核分數。人工 UAT 仍須以實際支援瀏覽器完成全流程鍵盤巡覽與螢幕閱讀器抽查。
