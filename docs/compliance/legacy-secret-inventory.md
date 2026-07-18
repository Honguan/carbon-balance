# 舊版敏感資訊清單

> 不保存或顯示秘密值；下列位置皆以 `SRC-001` ZIP 為來源。

| ID | 舊版位置／模式 | 風險 | 處置 |
|---|---|---|---|
| SEC-LEG-001 | `set_Map/set_map.php` 與日期副本 | Google Maps API Key 暴露 | 視為已洩漏；由 owner 撤銷或重新限制，不移植 |
| SEC-LEG-002 | Transportation HTML 地圖頁 | Google Maps API Key 使用候選 | 視為已洩漏；不移植 |
| SEC-LEG-003 | `Python-Test/google sheet api金鑰/*.json` | Google 服務帳戶憑證候選 | 視為已洩漏；撤銷 key，不提交 JSON |
| SEC-LEG-004 | `dbtools.inc.php` 及副本 | 無密碼 root DB 連線 | 不移植；新系統只使用環境設定與最小權限帳戶 |

撤銷屬外部寫入，尚未獲授權且無法由本機驗證；追蹤於 `DEC-003`。
