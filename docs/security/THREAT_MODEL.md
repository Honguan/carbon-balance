# P0 威脅模型

| 威脅 | 控制與驗證 |
|---|---|
| IDOR／跨租戶讀寫 | 組織 scope、EF 全域查詢篩選、資源權限與跨租戶安全測試 |
| CSRF | Razor Pages antiforgery token；所有狀態變更使用 POST |
| XSS | Razor 預設編碼、CSP、禁止輸出未信任 HTML |
| SQL injection | EF 參數化查詢；匯入資料只寫 staging，不組合 SQL |
| 惡意檔案上傳 | 大小限制、ClamAV INSTREAM、Clean 後才存 MinIO、SHA-256 稽核 |
| 暴力登入／資源耗盡 | Identity lockout 與全域固定視窗 rate limit |
| 工作階段竊取 | HttpOnly、Secure、SameSite cookie；Data Protection key 持久化；敏感操作要求 MFA |
| 敏感紀錄外洩 | 結構化 correlation ID；不記錄 token、密碼、連線字串及證據內容 |
| 祕密外洩 | 環境／祕密管理服務注入、Gitleaks、範例值不可用於正式環境 |
| 供應鏈漏洞 | locked restore、NuGet audit、Trivy 檔案系統與映像掃描、SBOM |

安全發布閘門為已知 Critical/High 零容忍。任何跨租戶、未授權報表、繞過 MFA 或惡意上傳缺陷均阻擋 RC。
