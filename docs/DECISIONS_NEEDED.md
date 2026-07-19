# 待決事項

僅記錄需要使用者、領域人員、法務或外部環境決定，且無法由工程實作安全推進的事項。

| ID | 決策 | 所需角色 | 目前處置 |
|---|---|---|---|
| DEC-001 | repository 授權方式 | Owner／法務 | Repository 目前可公開讀取但未提供 `LICENSE`；維持 unlicensed，待正式決策後再新增授權文件 |
| DEC-002 | 歷史 PCR 與案例係數的現行有效性 | 碳足跡領域審核者 | 只作 historical／pending-domain-review，不發布 |
| DEC-003 | 舊 ZIP 內暴露 Google Maps Key 與服務帳戶是否已撤銷 | 憑證 owner | 不複製值；發布前需取得撤銷證據 |
| DEC-004 | 係數來源資料的再散布授權 | 法務／資料 owner | 未確認資料只可 quarantined/staging |
| DEC-005 | 正式環境 hosting、email、object storage 與 secrets provider | Owner／平台 | P0 僅提供無真實憑證的範例設定 |
| DEC-006 | 舊版各階段活動量公式的領域有效性 | 碳足跡領域審核者 | 以 `pending-domain-review` 候選規則實作運輸重量距離、使用情境消耗及廢棄量單位換算；不得視為已核准 PCR 公式 |
