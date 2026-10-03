# ADR-0005：組織級 SMTP 設定

- Status: Accepted
- Date: 2026-07-28

## Context

不同組織可能使用不同郵件供應商。系統仍需支援本機 Mailpit 與環境變數預設值，並避免在畫面或稽核紀錄保存 SMTP 密碼明文。

## Decision

- 在組織範圍保存 SMTP 主機、連接埠、TLS、帳號、寄件地址與寄件人名稱。
- SMTP 密碼使用 ASP.NET Core Data Protection 加密後保存；未設定組織值時 fallback 至 `Mail` 設定區段。
- 只有組織擁有者或管理員可以儲存設定及寄送測試信，設定變更產生 append-only audit event。
- 工作區以獨立郵件設定分頁呈現，密碼欄位不回填；測試信驗證實際寄送能力。
- 組織 SMTP 屬於輸出網路信任邊界。儲存時與每次寄送時共用目的地政策，既存與 fallback 設定也必須重新驗證；混合 DNS 只要包含禁止位址即拒絕。
- 使用 MailKit 4.x 接收已連線的 socket，TCP 固定到已驗證 IP，原 hostname 保留給 TLS 憑證驗證／SNI；`System.Net.Mail.SmtpClient` 無法注入 socket，前置 DNS 檢查不足以防止重新解析。
- TLS 強制 STARTTLS，465 使用 SslOnConnect；無可降級模式。內部 relay／明文例外由伺服器設定精確 hostname、port 與 IP/CIDR，開發 Mailpit 例外僅 Development 生效。
- 單次寄送總期限預設 30 秒（1–120 秒），涵蓋資料庫、DNS、連線與傳輸，並傳遞 HTTP 中斷／caller cancellation。

## Consequences

- 各組織可獨立切換 SMTP，不需重建映像或修改正式程式設定。
- Data Protection 金鑰必須持久化並納入正式環境備份；金鑰遺失時既有密文無法解密，需重新輸入密碼。
- 正式環境仍應優先使用秘密管理服務提供的預設 SMTP 憑證。
