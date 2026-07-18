# 實作狀態

> 更新日期：2026-07-18

狀態值：`pending`、`in-progress`、`blocked`、`complete`。只有附上實際驗證證據的項目可標為 `complete`。

| Phase | Epic／Issue | 狀態 | 驗證 | 阻塞／備註 |
|---|---|---|---|---|
| 0 | LEG-001 來源盤點與 checksum | complete | 3 個頂層來源；187 筆 ZIP 逐檔 inventory；checksum 驗證 | 舊憑證撤銷仍為外部 DEC-003 |
| 0 | GOV-001 repository 與治理基線 | complete | Git commit `181a3d4`；本機 secret pattern scan 通過 | 遠端 ruleset 未獲授權 |
| 0 | ADR-0001～0003 | complete | 正式 ADR 狀態為 Accepted | — |
| 1 | ARC-001 .NET solution 與模組骨架 | complete | .NET SDK 10.0.302 Release build，0 warning／0 error | — |
| 1 | ARC-002 PostgreSQL／EF migration | complete | PostgreSQL 18.4 空庫套用兩版 migration；19 張 app／identity 表、4 筆單位種子 | — |
| 1 | DEV-001 Docker Compose | complete | postgres、MinIO、Mailpit、web 全部 healthy；首頁與 health 200；Data Protection key 持久化 | — |
| 1 | CAL Golden Vertical Slice | complete | E2E 完成註冊、確認、登入、組織、盤查、五階段資料與 7 kgCO2e 不可變計算 | PCR 與係數仍為未核准測試 fixture |
| 2 | Identity／Organizations／Products／Inventory | in-progress | confirmed account、租戶範圍、角色權限矩陣、產品與盤查版本 UI 已可操作 | MFA、邀請與完整工作流待完成 |
| 3 | Units／Factors／PCR 治理 | in-progress | 單位種子；係數與 PCR 草稿、發布、撤回、有效期防護；盤查綁定 PCR 主鍵 | 匯入 staging、差異與 supersede 待完成 |
| 4 | 五階段生命週期資料與 Evidence | in-progress | 五階段資料已可輸入；Evidence 經 ClamAV Clean 後寫入 MinIO，保存 SHA-256 並綁定活動 | 分配、排除與情境專用欄位待完成 |
| 5 | 完整不可變計算引擎 | in-progress | decimal 五階段 line／summary／total、canonical manifest、hash、warning、supersede lineage、run diff | 分配公式與排除／假設摘要待完成 |
| 6 | 審核、報告、匯出與查驗準備 | pending | — | — |
| 7 | Legacy staging 與遷移工具 | pending | — | — |
| 8 | 安全、營運、CI 與 P0 RC | pending | — | — |

## 最近驗證

- `dotnet build --configuration Release --no-restore`：成功，0 warning／0 error。
- `dotnet test --configuration Release --no-build`：27 項通過（Unit 17、Golden 2、Architecture 2、Contract 1、Integration 3、Security 2）。
- `docker compose config --quiet`：成功。
- `docker compose up -d --build`：PostgreSQL 18.4、MinIO、Mailpit、Web healthy。
- migration：五版 migration 可由空庫順序套用；既有資料庫升級成功，共 22 張 app／identity 表。
- Smoke：`/`、`/health/live`、`/health/ready` 均為 HTTP 200。
- E2E Smoke：註冊信、確認、登入、建檔、PCR／係數發布、ClamAV／MinIO Evidence、連續兩次五階段計算通過；總量 7 kgCO2e、hash 相同、supersede lineage 正確且 diff 為 0。
