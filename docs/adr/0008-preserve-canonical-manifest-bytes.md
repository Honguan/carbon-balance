# ADR-0008：保存計算快照原文，按原始雜湊修復 JSONB 格式損失

- Status: Accepted（資料保存方式；不變更計算公式）
- Date: 2026-09-07

## Context

計算快照以 `Utf8JsonWriter` 固定順序輸出，再計算 UTF-8 原文的 SHA-256。原欄位使用 PostgreSQL `jsonb`，其讀回內容會改變空白、物件屬性順序及字串編碼。使用新 DbContext 重讀的整合測試確認：即使計算內容未改，原始 SHA-256 也不再吻合；tracked entity 測試曾掩蓋此問題。

## Decision

1. `calculation_runs.canonical_input_manifest` 改成 `text`，保存被雜湊的原文。其他非原文雜湊用途的 JSONB 欄位保留。
2. 保留原 SHA-256、run ID、輸入語意、結果與 append-only 規則。不得以重新計算雜湊讓毀損資料通過驗證。
3. 顯式離線命令 `--repair-canonical-manifests` 只處理既有格式損失。依已知 manifest schema 與公式欄位順序重建有限候選，只有與原 SHA-256 完全吻合的原文才可寫回。
4. 修復先驗證整批，再於單一交易中恢復原文並追加稽核事件。任何無法恢復的 run 使整批失敗，既有資料及雜湊不變。此命令不由 GET、報表或一般頁面請求呼叫。
5. 無效或不支援的快照在清單標示，匯出回傳 409；不阻擋其他有效 run。
6. 有計算紀錄時禁止降回 JSONB；已有活動停用紀錄時禁止移除 `retired_at`。需要降版時使用升級前備份，不能重新啟用被取代活動或再破壞快照原文。

## Consequences

新快照可逐位元組驗證。既有舊 schema、未知公式欄位或已丟失的 JSON 數字表示法不保證可還原，需可信備份；修復工具會失敗而非推測。離線修復會鎖定計算紀錄並將其載入記憶體，應在停止寫入及完成備份後執行。

## 升級與復原

依現有部署程序設定連線與安全組態，停止應用程式寫入並保存可還原的資料庫備份，再執行：

```text
dotnet CarbonFootprint.Web.dll --migrate
dotnet CarbonFootprint.Web.dll --repair-canonical-manifests
```

第二個命令成功後檢查修復數量與 `calculation.manifest.original_bytes_restored` 稽核事件，再驗證歷史匯出。重複執行不會重複修復或追加成功事件。若命令回報無法還原的 run ID，保留錯誤與備份，停止該資料的有效報告作業；不得手動更改 `input_sha256`。

開發用 EF CLI 需設定 `CARBON_DB_CONNECTION` 或明確傳 `--connection`；Web 執行命令使用 `ConnectionStrings:Database`。不要依賴未確認的預設連線。
