# 驗證與工作階段政策

## 密碼

- Production／Staging：至少 12 個字元，不強迫大小寫、數字或符號組合，以支援長密語。
- Development：至少 6 個字元，僅供本機測試資料與 E2E 使用。
- 所有環境都拒絕內建清單中的常見外洩密碼與單一字元重複密碼；密碼仍由 ASP.NET Core Identity 雜湊與驗證。

## 工作階段與失效界線

| 環境 | Idle timeout | Absolute lifetime | Security stamp 最長重驗間隔 |
|---|---:|---:|---:|
| Production／Staging | 8 小時 | 24 小時 | 5 分鐘 |
| Development | 30 天 | 30 天 | 30 分鐘 |

Idle timeout 可滑動續期，但 Data Protection 保護的 `session_started_at` 不會重設；超過 absolute lifetime 必須重新登入。可用 `Security:Authentication:SessionIdleTimeout`、`SessionAbsoluteLifetime`、`SecurityStampValidationInterval` 明確縮短或調整，absolute lifetime 不得短於 idle timeout。

ASP.NET Core Identity 會在密碼及 MFA 變更時旋轉 security stamp；本系統另在系統角色授與、組織建立、邀請接受與 membership 撤銷時旋轉 stamp。Production／Staging 的其他既有 session 最遲 5 分鐘失效，執行組織異動的目前 session 會以新 stamp 重新簽發。

## Abuse throttling

所有 bucket 均以處理可信 Forwarded Headers 後的 client IP 分區，且只計算 POST：

| Policy | 端點 | 限制 |
|---|---|---:|
| login | Login | 10 次／5 分鐘 |
| registration | Register | 5 次／15 分鐘 |
| email-resend | ResendEmailConfirmation | 5 次／15 分鐘 |
| recovery | ForgotPassword、ResetPassword、ChangePassword、SetPassword | 5 次／15 分鐘 |
| invitation | AcceptInvitation | 10 次／15 分鐘 |
| mfa | TOTP、recovery code、authenticator 管理 | 10 次／5 分鐘 |

上述 bucket 彼此獨立，並與全域每 IP 120 次／分鐘限制串接。

## Reverse proxy

預設不信任任何 forwarded header。只有 `Security:TrustedProxies` 列出的精確 proxy IP 能提供單層 `X-Forwarded-For` 與 `X-Forwarded-Proto`；無效 IP 會使應用程式拒絕啟動。直接面向網際網路的部署保持空清單；經反向代理部署時，由部署者填入實際 proxy IP，例如：

```json
{
  "Security": {
    "TrustedProxies": ["10.0.0.8"]
  }
}
```

不得填入任意用戶端可控制的地址或萬用範圍。多層 proxy 或 CIDR 信任需要另行審查後再擴充。
