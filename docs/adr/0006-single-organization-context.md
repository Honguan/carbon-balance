# ADR-0006：單一組織上下文

## 決策

- P0 每位使用者同時只能有一筆未撤銷的組織 membership；PostgreSQL 部分唯一索引強制此不變量。
- `organization_id` 不再保存於通用 Identity user claims。每次已驗證請求由 claims transformation 依未撤銷 membership 重建唯一 claim。
- 無 membership、無效 claim 或多個 claim 均不解析組織上下文；Identity security stamp 保留框架預設驗證週期。
- P0 不支援組織切換。撤銷 membership 後，下一個請求即失去組織上下文，並更新 security stamp 使既有 session 失效。

## 結果

併發 onboarding 或邀請接受最多只有一筆 active membership 成功，授權與資料篩選不再依賴任意 user claim 的第一個值。若未來需要多組織，必須另行設計明確選取、session 輪替與切換稽核。
