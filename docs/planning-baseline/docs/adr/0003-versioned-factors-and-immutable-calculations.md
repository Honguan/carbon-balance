# ADR-0003：係數版本化與不可變計算

- Status: Proposed
- Date: 2026-07-18

## Context

產品碳足跡結果依賴活動數據、單位、PCR、係數、GWP、公式與分配規則。任何來源更新都可能改變結果。

## Decision

- 所有係數、PCR、標準、公式與單位規則版本化。
- Calculation Run 保存明確版本與 input manifest hash。
- Run 建立後不可變；修正以新 run supersede。
- 報告只讀取指定 run，不動態套用最新係數。

## Consequences

- 歷史結果可重現與查驗。
- 資料量增加，需要 archive／retention 設計。
- UI 必須清楚顯示「目前最新資料」與「此 run 使用資料」的差異。
