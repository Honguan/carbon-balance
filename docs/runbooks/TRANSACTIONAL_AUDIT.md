# 交易稽核與驗證 archive

適用 Issue #54；格式決策見 [ADR 0009](../adr/0009-transactional-safe-audit.md)。此手冊為工程基線，正式領域確認、Golden 答案及人工 UAT 仍待簽核。

## 部署權限

1. migration／備份使用独立管理帳號；Web 與 factor-sync 使用非 owner runtime LOGIN，不持有 superuser、CREATEROLE、CREATEDB、REPLICATION、BYPASSRLS 或 owner role membership。
2. 先備份、套用 `TransactionalAuditContract` migration，再以管理連線執行 `psql -v ON_ERROR_STOP=1 -v runtime_role=<runtime登入角色> -f scripts/audit-runtime-permissions.sql`。該檔不建立密碼、不授予其他業務資料表權限；沿用所需業務權限並單獨限制兩張 audit 表。
3. 權限檔在同一交易驗證 SELECT／INSERT，以及無 UPDATE／DELETE／TRUNCATE／TRIGGER／REFERENCES、無 schema CREATE。繼承角色或 PUBLIC grant 造成違反時會失敗；修正權限來源，不能略過檢查。若沿用資料庫 owner 的 Web 連線，部署尚未滿足此契約。
4. runtime 連線應可 SELECT／INSERT `app.audit_events` 與 `identity.system_audit_events`；直接 UPDATE／DELETE／TRUNCATE 必須失敗。migration 管理角色的普通 SQL 同樣受 trigger 拒絕，但它仍能管理 schema，不能視為對抗 DB owner 的安全邊界。

## 調查與 archive

由 actor、organization、correlation ID、時間查詢。`metadata_json` 的 safeChanges 與 before/after hash 只涵蓋明確安全 projection；sourceDocumentSha256、evidenceFileSha256、inputManifestSha256 等是來源證據，不代表整份 entity 雜湊。使用者合理理由可能是自由文字；不得在理由輸入 secrets。

報表中的「下載稽核驗證 archive」沿用該組織報表的 authorization／MFA。ZIP 包含 audit.json、audit.sha256、canonical-input-manifest.json、canonical-input-manifest.sha256。audit.json 保存截止 Timestamp／ID；再次 POST VerificationArchive handler 時同時帶入 `cutoffTimestamp`／`cutoffId` 可重建截至該點的下載，後續 export event 不加入舊 archive。事件按 Timestamp、ID 排序；跨租戶事件及全域 system audit 不匯入，以免洩漏其他組織。來源 manifest 若雜湊／版本不符則拒絕下载。

保留原 archive 與 SHA 至經核准的儲存位置；自行修改 JSON 後不能沿用原 SHA。此 archive 沒有簽章服務、hash chain 或完整治理核准，不能據此宣稱第三方查驗或 ISO 認證。

## 回滾與復原

空庫／僅 legacy audit 的演練庫可降至前一 migration 再升回；一旦存在 audit-v1 事件，down migration 故意拒絕，不移除 before/after 欄位或 guard。停止寫入，將部署前備份還原到另一個隔離資料庫，核對 migration history、事件數與 hash，再按一般切換流程處理。不在唯一正式資料庫停用 trigger 清除資料。

自動化測試清理僅能使用明確的隔離測試資料庫管理連線。它在自己的交易使用管理能力清除 fixture，提交後恢復保護；runtime 負向測試仍使用獨立非 owner LOGIN，不以管理清理結果代替權限驗證。
