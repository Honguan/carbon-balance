# 實作狀態

> 更新日期：2026-07-18

狀態值：`pending`、`in-progress`、`blocked`、`complete`。只有附上實際驗證證據的項目可標為 `complete`。

| Phase | Epic／Issue | 狀態 | 驗證 | 阻塞／備註 |
|---|---|---|---|---|
| 0 | LEG-001 來源盤點與 checksum | in-progress | ZIP SHA-256 已取得 | 完整機器可讀 manifest 待建立 |
| 0 | GOV-001 repository 與治理基線 | in-progress | 結構人工檢查 | CI 與 secret scan 待驗證 |
| 0 | ADR-0001～0003 | in-progress | 規劃基線已讀取 | 待複製為正式 ADR 並接受 |
| 1 | ARC-001 .NET solution 與模組骨架 | pending | — | — |
| 1 | ARC-002 PostgreSQL／EF migration | pending | — | — |
| 1 | DEV-001 Docker Compose | pending | — | — |
| 1 | CAL Golden Vertical Slice | pending | — | — |
| 2 | Identity／Organizations／Products／Inventory | pending | — | — |
| 3 | Units／Factors／PCR 治理 | pending | — | — |
| 4 | 五階段生命週期資料與 Evidence | pending | — | — |
| 5 | 完整不可變計算引擎 | pending | — | — |
| 6 | 審核、報告、匯出與查驗準備 | pending | — | — |
| 7 | Legacy staging 與遷移工具 | pending | — | — |
| 8 | 安全、營運、CI 與 P0 RC | pending | — | — |

## 最近驗證

- 尚未建立 solution；build、test、migration 與 Docker 啟動均不得宣稱通過。
