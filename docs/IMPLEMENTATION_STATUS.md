# 實作狀態

> 更新日期：2026-07-18

狀態值：`pending`、`in-progress`、`blocked`、`complete`。只有附上實際驗證證據的項目可標為 `complete`。

| Phase | Epic／Issue | 狀態 | 驗證 | 阻塞／備註 |
|---|---|---|---|---|
| 0 | LEG-001 來源盤點與 checksum | complete | 3 個頂層來源；187 筆 ZIP 逐檔 inventory；checksum 驗證 | 舊憑證撤銷仍為外部 DEC-003 |
| 0 | GOV-001 repository 與治理基線 | complete | Git commit `181a3d4`；本機 secret pattern scan 通過 | 遠端 ruleset 未獲授權 |
| 0 | ADR-0001～0003 | complete | 正式 ADR 狀態為 Accepted | — |
| 1 | ARC-001 .NET solution 與模組骨架 | complete | .NET SDK 10.0.302 Release build，0 warning／0 error | — |
| 1 | ARC-002 PostgreSQL／EF migration | complete | PostgreSQL 18.4 空庫套用 `InitialCreate`；19 張 app／identity 表 | 前版升級測試待下一 migration |
| 1 | DEV-001 Docker Compose | complete | postgres、MinIO、Mailpit、web 全部 healthy；首頁與 health 200 | Data Protection 持久化待補 |
| 1 | CAL Golden Vertical Slice | in-progress | 五階段手算總量 7 kgCO2e；deterministic hash、總和測試通過 | 可操作登入到計算 UI 待完成 |
| 2 | Identity／Organizations／Products／Inventory | pending | — | — |
| 3 | Units／Factors／PCR 治理 | pending | — | — |
| 4 | 五階段生命週期資料與 Evidence | pending | — | — |
| 5 | 完整不可變計算引擎 | pending | — | — |
| 6 | 審核、報告、匯出與查驗準備 | pending | — | — |
| 7 | Legacy staging 與遷移工具 | pending | — | — |
| 8 | 安全、營運、CI 與 P0 RC | pending | — | — |

## 最近驗證

- `dotnet build --configuration Release --no-restore`：成功，0 warning／0 error。
- `dotnet test --configuration Release --no-build`：13 項通過（Unit 4、Golden 2、Architecture 2、Contract 1、Integration 2、Security 2）。
- `docker compose config --quiet`：成功。
- `docker compose up -d --build`：PostgreSQL 18.4、MinIO、Mailpit、Web healthy。
- 空資料庫 migration：`20260718081447_InitialCreate` 成功，`app`／`identity` 共 19 張表。
- Smoke：`/`、`/health/live`、`/health/ready` 均為 HTTP 200。
