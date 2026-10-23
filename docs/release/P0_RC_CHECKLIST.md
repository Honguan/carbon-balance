# P0 候選版本檢核流程

發布成熟度、所有 blocker、證據與目前狀態統一以 [發布就緒閘門](RELEASE_READINESS.md) 為準。本文件只定義操作流程，不維護另一份預先勾選的通過清單。

1. 選定同一 commit SHA、tag、image digest、schema、archive 與規則版本，列明候選範圍及未完成 Issues。
2. 依 G-01 至 G-07 記錄可驗證的同版 CI、測試、安全掃描、SBOM、Golden、migration／restore、正式等效環境及外部接受證據，保存 artifact 與 hash。
3. 依 [UAT 計畫](P0_UAT_PLAN.md) 執行人工驗收，將實際結果與獨立審查者填入 [簽核紀錄](P0_UAT_SIGNOFF.md)。CI 不得代填人工或領域 Pending／Pass。
4. 正式候選須完成範圍內所有工程 blocker；未接受的 Blocker／Major 為零，Minor 有具名處置。PR #31、四項合成 Golden 或歷史 CI 不能取代逐條驗收。
5. 版本說明分列已實作、已驗證、正式等效環境已驗證、外部已接受與限制。工程版本必須標示 prerelease；正式發布與部署依對應閘門及授權執行。

2026-10-03：使用者確認尚無領域簽核與人工 UAT，要求先完成工程修正與預發布；此決定不構成領域或正式環境驗收。
