# 發布成熟度與證據閘門

此文件是唯一發布就緒判定入口。其他狀態文件、CI、PR 與版本說明必須引用此處；單一測試數、Issue 關閉或 CI 綠燈不代表領域核准、正式環境驗證或查驗接受。

## 目前判定

2026-10-03（Asia/Taipei）：**開發中，工程預發布準備中**。使用者確認尚無 PCR／公式／GWP／獨立 Golden Cases 的領域簽核及人工 UAT，要求先完成工程修正與預發布。不得標示正式發布、production-ready、verifier-ready 或認證就緒。

本次盤點基準為主線 `fadf3b88031c1bbcec5c3483ba242e3f72714626`；預發布目標 SHA／tag／image digest 尚未選定。下表是有日期的盤點，不是動態 GitHub 狀態；候選版本變更後必須重新核對。

| 成熟度 | 必須具備的證據 | 可用聲明 |
|---|---|---|
| 開發中／工程預發布 | 明確版本、已實作與未完成清單、實際驗證及限制；GitHub Release 勾選 prerelease | 測試用工程版本；不可對外提供正式盤查／查驗服務 |
| UAT candidate | 所有候選範圍內工程 blocker 已在主線驗收；同一 SHA 的完整 CI／安全、Golden、migration／restore、瀏覽器證據；領域案例可供獨立審查 | 可供封閉 UAT；尚未領域接受 |
| Release candidate | 上述證據及獨立 PCR／公式／GWP／reference suite 簽核、產品 UAT；未接受的 Blocker／Major 為零，Minor 有具名處置 | 候選正式版本；仍須環境驗證與發布核准 |
| Production-ready | 同一候選的正式等效 TLS／egress／tenant／S3／scanner／Data Protection／secret 配置、備份復原與回滾演練；DEC-003／005、授權及平台 owner 確認 | 僅對已驗證的環境及範圍聲稱可正式部署 |
| Verifier-ready | 指定產品／盤查／run／PCR 的完整查驗包、追溯／證據完整性及獨立查驗者接受紀錄 | 僅對該盤查與接受範圍描述查驗準備；不等於 ISO 認證、主管機關核定或碳標籤 |

預發布不能透過名稱或勾選欄位跳過工程驗證；已知未完成要求必須出現在版本說明。任何新 blocker、來源／規則變更、撤回、artifact hash 不符、CI 失敗或過期的環境／領域證據，都使受影響閘門回到待驗證。

## 工程需求盤點

OPEN 表示尚未以主線驗收證據證明完成；CLOSED 表示 GitHub 已關閉，目標版本仍須重驗。PR #31 的分支程式及 95% 評估不屬主線交付，不能以過去 CI 取代各 Issue 驗收。

| Issue | 要求／發布影響 | 本次狀態與證據 |
|---|---|---|
| [#21](https://github.com/Honguan/carbon-balance/issues/21) | 版本化 PCR／法規規則 | OPEN；PR #31 未合併，須規則與領域證據 |
| [#22](https://github.com/Honguan/carbon-balance/issues/22) | readiness／送審閘門 | OPEN；須主線功能與拒絕路徑驗收 |
| [#23](https://github.com/Honguan/carbon-balance/issues/23) | 品質／不確定性 | OPEN；須獨立參考案例及方法來源 |
| [#24](https://github.com/Honguan/carbon-balance/issues/24) | 共同資源／allocation | OPEN；須分子分母／共產品守恆 reference cases |
| [#25](https://github.com/Honguan/carbon-balance/issues/25) | 受控活動／公式 | OPEN；須白名單／資源限額／版本與 reference cases |
| [#26](https://github.com/Honguan/carbon-balance/issues/26) | 多段運輸 | OPEN；須有序路段／單位／來源與獨立手算 |
| [#27](https://github.com/Honguan/carbon-balance/issues/27) | 全球官方係數主目錄 | OPEN；須來源批次／租戶啟用與覆寫隔離 |
| [#28](https://github.com/Honguan/carbon-balance/issues/28) | 多文件證據鏈／hash | OPEN；須完整性、保留與權限負向驗收 |
| [#29](https://github.com/Honguan/carbon-balance/issues/29) | 查驗工作流 | OPEN；須職責分離、MFA、狀態機及外部接受紀錄 |
| [#30](https://github.com/Honguan/carbon-balance/issues/30) | 查驗封存包／影響分析 | OPEN；須逐檔 hash、replay、變更與歷史相容性 |
| [#42](https://github.com/Honguan/carbon-balance/issues/42) | MFA／account recovery | CLOSED；候選 CI 必須重跑真實 TOTP、recovery、stale／password-only 負向案例 |
| [#43](https://github.com/Honguan/carbon-balance/issues/43) | Administrator bootstrap | CLOSED；候選須單次原子 claim、授權／稽核與 stamp 驗證 |
| [#44](https://github.com/Honguan/carbon-balance/issues/44) | tenant／organization context | CLOSED；候選須跨租戶／claim race／resource authorization |
| [#45](https://github.com/Honguan/carbon-balance/issues/45) | password／session／throttling | CLOSED；候選須 recovery／session／限流負向案例 |
| [#46](https://github.com/Honguan/carbon-balance/issues/46) | hardened deployment | CLOSED；範例與 CI 不是正式環境部署證據 |
| [#47](https://github.com/Honguan/carbon-balance/issues/47) | SMTP egress／TLS | OPEN；須 DNS/IP／TLS／timeout／cancel 與實際 relay 測試 |
| [#48](https://github.com/Honguan/carbon-balance/issues/48) | feature application boundaries | OPEN；須真實流程與授權、架構驗收 |
| [#49](https://github.com/Honguan/carbon-balance/issues/49) | 限範圍／分頁查詢 | OPEN；須代表性資料查詢數／分頁／租戶隔離 |
| [#50](https://github.com/Honguan/carbon-balance/issues/50) | freshness／核准前驗證 | CLOSED；[實測](../IMPLEMENTATION_STATUS.md)與候選快照／治理拒絕案例 |
| [#51](https://github.com/Honguan/carbon-balance/issues/51) | migration／同步分離 | CLOSED；[PR #72](https://github.com/Honguan/carbon-balance/pull/72) 已合併 `fadf3b8`，[主線 CI](https://github.com/Honguan/carbon-balance/actions/runs/37119150558) 5/5 |
| [#52](https://github.com/Honguan/carbon-balance/issues/52) | immutable build provenance | CLOSED；候選 binary／image／manifest SHA 必須等於 checkout SHA |
| [#53](https://github.com/Honguan/carbon-balance/issues/53) | CSV formula injection | CLOSED；[實測](../IMPLEMENTATION_STATUS.md)與候選匯出回歸 |
| [#54](https://github.com/Honguan/carbon-balance/issues/54) | transactional audit | OPEN；須 before/after／metadata 與 rollback 一致性 |
| [#55](https://github.com/Honguan/carbon-balance/issues/55) | optimistic concurrency | OPEN；須過期 draft 拒絕、修正流程與 audit 原子性 |
| [#56](https://github.com/Honguan/carbon-balance/issues/56) | critical dependency readiness | OPEN；須 S3／scanner／keyring／DB 故障演練 |
| [#57](https://github.com/Honguan/carbon-balance/issues/57) | 獨立 Golden reference suite | OPEN；目前四項合成案例不可作完整領域正確性證明 |
| [#58](https://github.com/Honguan/carbon-balance/issues/58) | 最小 S3 權限／串流上傳 | OPEN；須 bucket 權限、容量／stream bounds、scan／hash／故障測試 |
| [#59](https://github.com/Honguan/carbon-balance/issues/59) | 本發布治理修正 | 進行中；本文件與交叉引用須主線／CI 驗收 |
| [#60](https://github.com/Honguan/carbon-balance/issues/60) | 版本化 inventory bulk import | OPEN；須 dry-run、逐列錯誤、防重、版本與全部成功／明確失敗 |
| [#61](https://github.com/Honguan/carbon-balance/issues/61) | 可續接引導工作流程 | OPEN；須 blocker 導覽、resume 與既有完整工作流程回歸 |
| [#69](https://github.com/Honguan/carbon-balance/issues/69) | 受維護容器／儲存服務 | CLOSED；[遷移證據](../runbooks/OBJECT_STORAGE_MIGRATION.md)，候選重掃所有固定 image digests |

PR #31 的 [head `8b22c75`](https://github.com/Honguan/carbon-balance/pull/31) 有歷史測試紀錄，但目前為 Draft、與 main 衝突。其「只剩 production validation／external acceptance」與 95% 文字不作本專案發布判定；須整合最新安全修補、逐 Issue 驗收及獨立案例後才可重評。

## 候選版本證據表

每次候選版本填寫一份此表於版本說明／驗收紀錄，並從本文件連結。證據必須指向同一 commit 與不可變 artifact；缺漏、Pending 或僅歷史記錄一律不是通過。

| 閘門 | 必須記錄 | 目前狀態 |
|---|---|---|
| G-01 工程範圍 | scope、Issue／PR／merge SHA、逐條驗收、未完成／接受的限制 | 工程修正中；尚無完整候選 |
| G-02 同版 CI／security | run URL、SHA、全部 jobs、unit/integration/architecture/security/Golden/browser、scan 時間／版本、SBOM／image digest | [`fadf3b8` 主線 CI](https://github.com/Honguan/carbon-balance/actions/runs/37119150558) 5/5；最終候選須重驗 |
| G-03 計算與獨立領域確認 | PCR／rule／formula／GWP／unit／factor versions、來源授權／hash、獨立 expected line/stage/product totals、reviewer／日期／接受範圍 | #57／DEC-002／006，待領域簽核 |
| G-04 正式等效基礎設施 | TLS／egress／SMTP／S3 IAM／scanner／keyring／secrets、tenant 隔離、失效 readiness 與故障證據 | #47／56／58／DEC-005；未在正式等效環境完成 |
| G-05 遷移／restore／compatibility | 前版與候選 schema、空庫／升級、backup hash、隔離 restore、evidence bytes／hash、歷史 run／archive replay | 歷史演練存在；候選、全新治理 schema 須重驗 |
| G-06 產品／查驗 UAT | [產品 UAT](P0_UAT_SIGNOFF.md)與獨立查驗者的指定 run／archive 接受、具名簽核 | 使用者確認未提供；Pending |
| G-07 外部發布與授權 | DEC-001／003／004／005、未散布受限內容／secret、發布類型與使用者授權 | 已授權工程預發布；外部決策未全部完成 |

## 更新與版本說明規則

每次合併需求、候選 SHA 改變、外部證據到齊或新 blocker 出現，維護者先查 GitHub 真實 Issue／PR 狀態與對應 workflow，再更新此表的日期、SHA、證據 URL、適用範圍與未完成項目。CI 不得自動勾選人工／領域接受；也不得挑選另一個 SHA 的綠色 workflow 掩蓋失敗。

版本說明必須分開列：**已實作**（程式與 merge SHA）、**已驗證**（同版測試與 artifact）、**正式等效環境已驗證**（環境、設定、演練）、**外部已接受**（簽核人／日期／範圍）。未有證據的欄位明寫待驗證。工程預發布使用 prerelease tag，列所有已知缺口，不使用完整產品百分比代替驗收。
