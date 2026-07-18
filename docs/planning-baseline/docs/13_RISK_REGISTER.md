# 風險登錄表

| ID | 風險 | 機率 | 影響 | 緩解措施 | Owner |
|---|---|---:|---:|---|---|
| R-01 | 把歷史 PCR 當成現行規則 | 高 | 高 | 官方查詢、版本與有效期、領域核准 | Domain |
| R-02 | 舊係數缺來源／單位／版本 | 高 | 高 | staging、隔離、人工審核、不得直接發布 | Data |
| R-03 | 公式或四捨五入錯誤 | 中 | 高 | Golden Case、逐項明細、版本化規則 | Calculation |
| R-04 | 跨組織資料洩漏 | 中 | 極高 | resource authorization、負面 integration tests | Security |
| R-05 | 歷史結果因係數更新改變 | 中 | 高 | immutable run、factor_version FK、snapshot hash | Architecture |
| R-06 | 任意公式造成 RCE／DoS | 中 | 極高 | 受限 AST、白名單、資源限制，不使用 eval | Security |
| R-07 | Migration 靜默轉型錯誤 | 高 | 高 | raw staging、error report、禁止 default 0 | Data |
| R-08 | 過度設計延誤交付 | 中 | 中 | modular monolith、vertical slice、P0 邊界 | Product |
| R-09 | 單人專案審核不足 | 高 | 中 | CI、checklist、外部 domain/security review at gates | Governance |
| R-10 | 法規或 ISO 修訂 | 中 | 高 | standards register、rule versioning、季度 review | Compliance |
| R-11 | 第三方 API／地圖費用或停用 | 中 | 中 | provider abstraction、quota、manual distance override | Platform |
| R-12 | 上傳文件含個資／機密 | 高 | 高 | classification、access control、encryption、retention | Security |
| R-13 | 報告被誤認為已查驗 | 中 | 高 | 明確狀態與 disclaimer，證明文件才可切換 | Product |
| R-14 | ISO 文件著作權問題 | 中 | 高 | repo 只放摘要與引用，不提交完整標準內容 | Legal |
| R-15 | 係數資料授權不明 | 高 | 高 | 保存 license、限制 export、法務確認 | Data |
| R-16 | DB major/minor 過期 | 低 | 中 | support calendar、patch SLA、upgrade rehearsal | Platform |
| R-17 | Backup 存在但無法還原 | 中 | 極高 | 定期自動 restore test、RPO/RTO 記錄 | Operations |
| R-18 | AI 產生錯誤領域規則 | 中 | 高 | AI 只提案；domain approval + tests 才發布 | Domain |

## Release Gate 風險門檻

- 極高影響風險不得無 owner 或 mitigation。
- High/Critical security finding 不得帶入 production。
- 法規、PCR、係數與計算規則未核准不得發布為有效資料。
- 無法解釋的 Golden Case 差異視為 blocker。
