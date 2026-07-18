# Definition of Done

一個 Issue 只有在以下適用項目完成後才可關閉。

## 需求與設計

- [ ] 驗收條件明確且全部完成。
- [ ] 沒有未記錄的 scope expansion。
- [ ] 重要架構／資料／法規決策已新增或更新 ADR。
- [ ] 領域名詞與文件一致。

## 程式

- [ ] 程式可編譯，無新增 warning（或有核准例外）。
- [ ] 維持模組邊界與 dependency rule。
- [ ] 錯誤不被靜默吞掉。
- [ ] 無 hard-coded secret、測試帳密或客戶資料。
- [ ] 所有 SQL 參數化。

## 資料

- [ ] Schema migration 可由空資料庫套用。
- [ ] 升級路徑已驗證。
- [ ] 資料轉型失敗有報告，不會默認為 0／NULL。
- [ ] 版本、來源、單位與 audit 資訊完整。
- [ ] Rollback／recovery 已規劃。

## 安全與權限

- [ ] 未登入、錯誤角色、跨組織負面測試完成。
- [ ] 敏感資料沒有出現在 log、URL 或錯誤頁。
- [ ] CSRF、XSS、upload 與 rate limit 影響已評估。
- [ ] 依賴與 secret scan 通過。

## 計算與領域

- [ ] Golden Case 或手算案例通過。
- [ ] 單位、精度與四捨五入測試完整。
- [ ] 係數／PCR／公式版本不會被覆寫。
- [ ] 歷史 calculation run 可重現且不受新資料影響。
- [ ] 領域 owner 已核准重大規則變更。

## 測試與交付

- [ ] Unit／integration／E2E（適用時）通過。
- [ ] CI required checks 全綠。
- [ ] 文件、API、報告範例與 release note 已更新。
- [ ] 可觀測性、alert、runbook 已更新（適用時）。
- [ ] PR 內附實際驗證命令與結果。
