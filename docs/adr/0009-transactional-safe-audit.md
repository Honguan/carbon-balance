# ADR 0009：交易內安全稽核證據

- 狀態：工程決策已採用；正式領域／查驗核准另行辦理。
- 關聯：Issue #54、ADR 0007、ADR 0008。

## 決策

使用單一具體 `AuditWriter`，僅加入呼叫端 DbContext，不自行 Save／Commit。Workspace、組織、計算、來源匯入與系統管理沿用原交易；一次操作的相關資源使用同一 correlation。係數同步的開始／失敗／重試／完成 attempt 仍使用獨立系統事件交易，不能冒充成功的目錄 mutation。

`audit-v1`／`safe-resource-projection-v1` 對逐資源固定安全欄位計算 SHA-256。更新在 Save 前讀取明確欄位的 EF original/current snapshot；不序列化 entity 或整份 OriginalValues。dictionary 以 ordinal key 排序，decimal 保留數值但移除無意義 scale，時間固定 UTC 微秒精度。actor、原因、session 與 metadata 不參與 state hash。create 為 null→hash，delete 為 hash→null；attempt／export 明確記錄無 mutation 的原因。

metadata 保存 schemaVersion、operation、hashScope、changedFields、safeChanges、reason、sessionReference、correlationId。source／manifest／artifact SHA 使用具名 metadata，不直接當作 state hash。SMTP 憑證、email、邀請／登入 token、password hash、security／concurrency stamp 不納入 projection；憑證變更僅記 boolean。sessionReference 使用框架認證時間與使用者 ID 的摘要，不保存 cookie 或 token。

租戶事件維持 `app.audit_events`；跨組織管理／同步維持 `identity.system_audit_events`，共用安全契約與 before/after 欄位。歷史 `{}` 事件不回填或宣稱可重建。尚未整合的治理 PR #31 不在此決策重建。

## 保護與限制

兩表 migration 加入 UPDATE／DELETE 與 TRUNCATE 拒絕 trigger，runtime 使用非 owner SELECT／INSERT 權限契約。這防止正常應用程式與非 owner SQL 修改；DB owner／superuser 能更改權限或停用 trigger，不能聲稱具有對抗資料庫管理者的簽章或 hash chain。

現有 Calculation Run 提供獨立、受原報表授權保護的 archive：同租戶 run／project／引用版本與 correlation 事件，Timestamp／ID 截止、固定排序、audit JSON SHA、原 manifest bytes／SHA。保留截止值可重做下載。discovery 只使用 immutable run、manifest 與截止前事件，不讀取目前可變的 project／PCR／evidence 關聯。它不實作完整治理簽核、留存／銷毀政策或 ISO 認證。既有 manifest 未包含 PCR ID，archive 明示該歷史參照 unknown 並排除推斷，不以 label 相同當成 stable ID；補入新 run 的 immutable PCR ID 必須另經 calculation snapshot 契約審查。

down migration 在沒有 v1 事件時可移除欄位／guard；有 v1 證據時拒絕降版。此時使用已驗證的部署前備份還原到新實例，避免丟失新增的系統 hash 欄位與保護規則。
