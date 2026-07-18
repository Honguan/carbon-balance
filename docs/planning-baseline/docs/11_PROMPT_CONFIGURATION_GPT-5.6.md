# GPT-5.6 提示詞與模型設定

## 1. 官方指南轉成專案規則

GPT-5.6 適合用「結果、限制、證據、完成標準」描述任務，不需要為每件事指定冗長步驟。專案提示詞採下列原則：

1. 每條規則只寫一次。
2. 只提供目前任務需要的工具與文件。
3. 清楚區分分析與實作授權。
4. 對安全、資料、法規、成本與外部寫入設定核准邊界。
5. 明確要求驗證證據與 Definition of Done。
6. 用評測決定 reasoning effort，不假定越高永遠越好。
7. 靜態專案規則放 `AGENTS.md`，Issue 只描述本次差異。

## 2. 建議模型與參數

### 架構、資料模型、法規交叉檢查

```json
{
  "model": "gpt-5.6-sol",
  "reasoning": {
    "effort": "high",
    "context": "all_turns"
  },
  "text": {
    "verbosity": "high"
  }
}
```

對資料遷移、重大安全審查、正式發布前設計審查，可在同一模型上 A/B 測試：

```json
{
  "reasoning": {
    "effort": "xhigh",
    "mode": "pro"
  }
}
```

Pro 不應用於一般 CRUD、格式化或例行文件更新。

### 一般功能實作

```json
{
  "model": "gpt-5.6-sol",
  "reasoning": {
    "effort": "medium",
    "context": "all_turns"
  },
  "text": {
    "verbosity": "medium"
  }
}
```

遇到跨模組、計算、授權、migration 提升至 `high`。大量規則明確、風險較低的工作可評測 `gpt-5.6-terra`。

### 例行分類、去重與文件整理

```json
{
  "model": "gpt-5.6-terra",
  "reasoning": {
    "effort": "low",
    "context": "current_turn"
  },
  "text": {
    "verbosity": "low"
  }
}
```

高量、可機械驗證的 metadata 清理可評測 `gpt-5.6-luna`，但不得讓低成本模型自行判定 PCR 適用性或法律結論。

## 3. 基礎任務提示詞

```text
<task>
完成 GitHub Issue #<id> 所描述的功能。
</task>

<context>
先讀取 AGENTS.md、Issue、相關 PRD、ADR 與領域規則。
舊 PHP 專案只作歷史參考，不是規格來源。
</context>

<constraints>
只修改本 Issue 範圍。
維持模組邊界、資料版本、租戶隔離與 calculation run 不可變性。
不得加入 secret、任意公式執行或未經審核的係數／PCR 規則。
</constraints>

<evidence>
新增或更新必要測試。
執行 build、test 與受影響的 migration／Golden Case 驗證。
回報實際命令與結果。
</evidence>

<completion_bar>
所有驗收條件通過；文件與 migration 同步；沒有已知 blocker。
無法確認的領域規則列為阻塞問題，不自行猜測。
</completion_bar>

<output>
先給結論，再列修改內容、驗證、風險與未完成事項。
</output>
```

## 4. Code Review 提示詞

```text
審查此 PR 是否會造成：
1. 計算錯誤或無法重現；
2. 單位／精度／四捨五入錯誤；
3. PCR、係數或公式版本被覆寫；
4. 跨組織資料洩漏或授權繞過；
5. migration 資料遺失、長時間鎖表或不可回復；
6. 報告與 calculation run 不一致；
7. 缺少測試、證據或文件。

只回報可由 diff、測試或規格支持的問題。每個問題包含：嚴重度、檔案／位置、失敗情境、影響、具體修正與所需測試。若沒有 blocker，明確說明已檢查的高風險面向。
```

## 5. 資料遷移提示詞

```text
規劃並審查此 migration。目標是零靜默資料遺失。
列出資料量假設、轉型規則、無法解析處理、lock/timeout、備份、preflight、post-check、rollback/recovery 與對舊 application 版本的相容性。
不得把失敗值自動轉成 0、空字串或 NULL。
輸出 migration plan、SQL/程式變更、驗證查詢與停止條件。
```

## 6. Programmatic Tool Calling 使用邊界

適合：

- 大量文件 metadata 去重。
- 係數匯入列的格式驗證、分類、聚合與差異報告。
- 靜態程式碼掃描結果縮減。

不適合：

- PCR 適用性最終判斷。
- 法律解釋。
- 每一筆結果都可能改變下一步推理的調查。
- 需要保留原生 citation／artifact 的工作。
- 具外部寫入、核准或破壞性操作的工作。

PTC 提示必須指定：允許工具、輸出 schema、concurrency、最多 retry、停止條件與失敗格式。

## 7. Prompt Caching 與 reasoning context

- `AGENTS.md`、固定標準摘要、資料模型規則可作穩定 prefix。
- 會頻繁變動的 Issue、diff、測試輸出放在 prefix 後方。
- 使用 explicit cache 前量測 cache write/read，避免把短期內容反覆寫入快取。
- 長期同一 Issue 使用 `all_turns` + `previous_response_id`。
- 切換到無關 Issue、前提已失效或進行獨立審查時使用 `current_turn`。

## 8. 評測集

建立至少 20 個代表性任務：

- 5 個計算／單位案例。
- 3 個租戶授權案例。
- 3 個 migration 案例。
- 3 個係數／PCR 版本案例。
- 3 個報告與稽核案例。
- 3 個一般 CRUD／文件案例。

比較：

```text
task success
必要欄位完整度
事實與證據
Golden Case 通過率
安全問題召回率與誤報率
總 tokens
latency
cost
工具呼叫與 retry
```

只有在品質門檻不下降時，較低 effort、較便宜模型或 PTC 才算優化。

## 9. 不建議的提示詞

避免：

- 重複十次「一定要小心」。
- 要求模型輸出私有思考過程。
- 同時要求「完全自主」又「每一步都先詢問」。
- 在一個提示詞暴露所有工具與全部 100 份文件。
- 用「最新檔案」取代權威文件與版本規則。
- 只說「重構好一點」而沒有驗收標準。

## 10. 官方參考

`https://developers.openai.com/api/docs/guides/latest-model?model=gpt-5.6#prompting-best-practices`
