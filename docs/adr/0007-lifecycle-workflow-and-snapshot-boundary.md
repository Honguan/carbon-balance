# ADR-0007：以五階段盤查為主流程，集中計算快照讀取

- Status: Accepted（工程架構；不核准舊 PCR 或公式）
- Date: 2026-09-07

## Context

`old` 的需求對應表、系統概述、專題報告與案例工作表一致指向：以各階段專用資料需求及係數搜尋，降低產品盤查門檻。舊 PHP 只實作部分流程；製造分配、圖表與公式存在不完整或不可靠處，不能直接移植。

目前四層專案與 Domain 五階段分類可支援此目的，但正式需求入口缺失，架構索引仍停留在 Phase 1 前。計算、送審與結果有效性檢查的共用快照由大型 `WorkspaceModel` 查詢及映射，資料讀取責任綁在畫面內。

## Decision

1. 保留 ADR-0002 的模組化單體及現有四層實體專案；五階段共用盤查、規則目錄與計算引擎。
2. 建立目前有效的 `docs/04_PRODUCT_REQUIREMENTS.md` 與 `docs/05_ARCHITECTURE.md`，保留 planning baseline 不修改。
3. Application 定義以專案 ID 讀取 Domain 快照的契約，Infrastructure 負責組織範圍查詢與映射。計算、送審與 freshness 三個 caller 共用；既有授權與狀態檢查保留。
4. `CalculateInventoryHandler` 與不可變 run 儲存契約保留。UI 不自行建立另一份快照或計算總額。
5. BOM、全廠分配、可重用使用模式及處理占比依原始用途列入明確模型缺口；不以推測公式或空模組補齊。

## Consequences

快照可在不依賴 PageModel 的情況下測試，Application 不依賴 EF record；所有快照使用點維持同一映射。新增的讀取介面用於跨層依賴反轉，不是通用 repository 或新框架。

本次不變更資料表、公式版本、舊 run、路由或角色行為。Web 尚有跨模組資料操作，需在後續實作用例時逐步收斂；本 ADR 不宣稱全面分層完成，也不取代既有 PCR 發布／審查規則。
