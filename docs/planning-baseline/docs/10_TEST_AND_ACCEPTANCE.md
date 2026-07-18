# 測試與驗收策略

## 1. 測試金字塔

| 層 | 目的 |
|---|---|
| Domain Unit | 公式、單位、分配、狀態轉換、版本規則 |
| Application | Use case、授權決策、transaction boundary |
| Integration | PostgreSQL、EF mapping、migration、object storage、email |
| Contract | 匯入格式、API、報告 schema |
| UI/E2E | 關鍵使用者流程，不覆蓋所有組合 |
| Golden Case | 經確認案例的逐項與總和結果 |
| Security | 租戶隔離、IDOR、CSRF、XSS、upload、rate limit |
| Operations | backup/restore、deployment、rollback、observability |

## 2. Golden Case

每個案例保存：

```text
case_id
source_document
reviewed_by
reviewed_at
pcr_version
factor_versions
unit_catalogue_version
formula_rule_set_version
inputs
expected_line_items
expected_stage_totals
expected_product_total
tolerance_or_rounding_rule
```

首批案例：

1. 瓶裝水：原料、製造、配銷，使用階段不適用的案例。
2. 滑鼠：具多種材料與製造能資源。
3. 衛生紙：紙類產品與製造更新案例。
4. 電鍋：具使用階段能源消耗與壽命。
5. 人工最小案例：每階段一筆、數字可手算。

## 3. 計算必測邊界

- 0、負值、極小值、極大值。
- 科學記號輸入。
- 不同單位與複合單位。
- 百分比總和不等於 100%。
- 分配分母為 0。
- 活動期間與係數有效期間不重疊。
- 係數撤銷／過期。
- 使用階段不適用。
- 切斷準則臨界值。
- 重複資料與相同證據。
- 四捨五入只在規定位置發生。
- 修改係數後舊 run 不變。
- 同輸入重跑得到相同 hash 與數值。

## 4. 授權驗收

每個資源測試：

- 未登入 → 拒絕。
- 同組織正確角色 → 允許。
- 同組織錯誤角色 → 拒絕。
- 其他組織即使猜到 ID → 拒絕且不洩漏存在性。
- 已撤銷邀請／成員 → 立即失效。
- 供應商只看被指派的資料。
- 管理員敏感操作需 MFA／重新驗證。

## 5. Migration 驗收

- Empty DB 套用所有 migration。
- 前一 release schema 升級。
- 大表 migration 時間與 lock 評估。
- 失敗時資料庫狀態可判斷與恢復。
- staging 資料轉型錯誤有明確報告。
- 不允許靜默截斷 numeric、日期或文字。

## 6. 報告驗收

- 報告總和與 calculation run 一致。
- 每個數字可追到 line item。
- 顯示 PCR、係數、公式與標準版本。
- 顯示草稿／內審／查驗／核定狀態，不混淆。
- 輸出日期、時區、版本、checksum。
- Excel/PDF/CSV 在繁中與英文資料下可開啟。

## 7. 發布阻擋條件

- 任一 Critical／High 安全弱點未處理。
- Golden Case 失敗。
- 租戶隔離測試失敗。
- migration 無法從空資料庫建立。
- backup restore 未驗證。
- 報告與計算總和不一致。
- 法規／PCR／公式變更未由指定領域角色核准。
