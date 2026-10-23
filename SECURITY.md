# 安全政策

## 回報弱點

請透過 repository 的私密安全通報管道回報弱點，不要建立公開 Issue。若尚未設定外部通報管道，請直接聯絡 repository owner，且不要附上真實憑證、客戶資料或可被濫用的完整攻擊資料。

## 支援範圍

P0 Release Candidate 前不承諾正式環境支援。安全修正只套用於目前開發分支與已明確標記的 release。

## 敏感資料規則

- 密碼、API Key、連線字串與 SMTP 憑證只透過環境變數、user secrets 或受管 secret store 提供。
- 歷史 ZIP 已知可能包含暴露金鑰及服務帳戶檔案，只能在忽略且受控的分析目錄處理。
- Audit 與 structured log 不得記錄密碼、token、完整個資或敏感文件內容。
- 發現舊 secrets 時，只記錄來源位置、風險與撤銷建議，不複製其值。

## 發布門檻

未處理的 Critical／High 弱點、跨組織隔離失敗、secret scan 失敗或無法驗證備份還原時，不得發布。

## SMTP 輸出網路邊界

組織 SMTP 設定是租戶可控制的伺服器輸出網路邊界。所有寄送（包括 Identity、邀請、測試信與預設設定）每次解析所有 DNS 位址；任一位址不合法即拒絕。預設拒絕 loopback、private、link-local、metadata、未指定、multicast、保留及 IPv4-mapped／IPv6 轉換繞過位址。TCP 只連線至當次已驗證 IP，不再解析 hostname；TLS 仍使用原 hostname 驗證憑證與 SNI。

TLS 使用強制 STARTTLS（465 使用直接 TLS），不允許自動降級或忽略憑證驗證。伺服器管理員才可在 `Mail:TrustedRelays` 核准精確 hostname、port、IP/CIDR；明文另需 `AllowInsecure=true`。組織無法自行授權例外。開發 Mailpit 例外只在 Development 生效，且須符合 `Mail:DevelopmentMailpit` 的 hostname、port 與 IP/CIDR。

`Mail:TimeoutSeconds` 預設 30 秒，可設定 1–120 秒，涵蓋寄送設定的資料庫讀取、DNS、TCP、TLS、AUTH、SEND 與斷線；HTTP 中斷與明確 cancellation token 均會取消寄送。不要記錄解密密碼或 SMTP 服務原始回覆。正式部署另須在防火牆／平台限制 SMTP 輸出目的地與連接埠，阻擋內部及 metadata 網路；應用程式驗證不能取代網路隔離。
