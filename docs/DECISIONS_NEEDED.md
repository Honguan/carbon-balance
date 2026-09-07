# 待決事項

僅記錄需要使用者、領域人員、法務或外部環境決定，且無法由工程實作安全推進的事項。

| ID | 決策 | 所需角色 | 目前處置 |
|---|---|---|---|
| DEC-001 | repository 授權方式 | Owner／法務 | Repository 目前可公開讀取但未提供 `LICENSE`；維持 unlicensed，待正式決策後再新增授權文件 |
| DEC-002 | 歷史 PCR 與案例係數的現行有效性 | 碳足跡領域審核者 | 只作 historical／pending-domain-review，不發布。已核對官方滑鼠21-024文件；同版號官方檔與old檔hash不同，清單期限與本文修改須分開紀錄；見 [公開來源核對](architecture/LEGACY_PURPOSE_ANALYSIS.md) |
| DEC-003 | 舊 ZIP 內暴露 Google Maps Key 與服務帳戶是否已撤銷 | 憑證 owner | 不複製值；發布前需取得撤銷證據 |
| DEC-004 | 係數來源資料的再散布授權 | 法務／資料 owner | 未確認資料只可 quarantined/staging |
| DEC-005 | 正式環境 hosting、email、object storage 與 secrets provider | Owner／平台 | P0 hardened profile 已定義 TLS、網路與 secret 注入契約；仍須選定實際供應商 |
| DEC-006 | 舊版各階段活動量公式的領域有效性 | 碳足跡領域審核者 | 既有活動量公式維持 `pending-domain-review`。公開標準已支持以物理等合理關係分配；仍需確認工廠實際基礎及每單位正規化。舊 `/3` 已確認是三家焚化廠替代估算平均，須確認代表性及回收重量假設，不可直接改為三筆加總。公式、來源與儲存格定位見 [old 分析](architecture/LEGACY_PURPOSE_ANALYSIS.md)，本次不以舊值定義正式答案 |
