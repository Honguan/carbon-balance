# P0 人工 UAT 簽核紀錄

> 此文件為待填範本。未完成實際驗收前，不得將發布閘門的人工 UAT 判定為通過。

依 [RELEASE_READINESS](RELEASE_READINESS.md) 判定成熟度。2026-10-03 使用者確認尚無領域與人工 UAT 紀錄；所有空白、範本及 Pending 不屬通過證據。

## Release 資訊

| 欄位 | 值 |
|---|---|
| 驗收日期 | `YYYY-MM-DD` |
| Release commit SHA | `<commit-sha>` |
| 容器 image digest | `<image-digest>` |
| Migration 版本 | `<migration-id>` |
| UAT 環境 | `<environment-name>` |
| 產品負責人 | `<name>` |
| 碳足跡領域審查者 | `<name>` |

## 情境結果

| ID | 結果 | 證據位置／SHA-256 | 缺陷 Issue | 備註 |
|---|---|---|---|---|
| UAT-001 | Pending |  |  |  |
| UAT-002 | Pending |  |  |  |
| UAT-003 | Pending |  |  |  |
| UAT-004 | Pending |  |  |  |
| UAT-005 | Pending |  |  |  |
| UAT-006 | Pending |  |  |  |
| UAT-007 | Pending |  |  |  |
| UAT-008 | Pending |  |  |  |
| UAT-009 | Pending |  |  |  |
| UAT-010 | Pending |  |  |  |
| UAT-011 | Pending |  |  |  |
| UAT-012 | Pending |  |  |  |
| UAT-013 | Pending |  |  |  |
| UAT-014 | Pending |  |  |  |

允許結果值：`Pass`、`Fail`、`Blocked`、`Not Applicable`。使用 `Not Applicable` 時必須填寫理由並由兩位簽核者接受。

## 缺陷摘要

| 等級 | 未關閉數量 | 接受方式 |
|---|---:|---|
| Blocker | 待盤點 | 不允許接受；須連結候選範圍內全部未完成 Issue |
| Major | 待盤點 | 不允許接受 |
| Minor | 待盤點 | 需列出 Issue、負責人與處置決策 |

## Golden Case 核對

- [ ] 總額為 `7 kgCO2e`。
- [ ] 相同輸入的 canonical manifest hash 相同。
- [ ] 輸入變更後建立新 run，舊 run 不可變。
- [ ] supersedes lineage 與 run diff 正確。
- [ ] 畫面、CSV、manifest 與 HTML 報告總額一致。

以上 `7 kgCO2e` 只驗證合成最小案例，不代表完整領域正確性。另須記錄 #57 的獨立參考案例版本、expected line／stage／product totals、容許差異規則、來源／hash，以及獨立領域審查者的簽核。

| 領域證據 | 版本／適用範圍 | 來源位置／hash | 獨立審查者／日期／決定 |
|---|---|---|---|
| PCR／rule set | 待填 | 待填 | Pending |
| 公式／分配／情境 | 待填 | 待填 | Pending |
| GWP／factor／unit catalogue | 待填 | 待填 | Pending |
| 獨立 Golden reference suite | 待填 | 待填 | Pending |
| 指定 run／archive 的外部查驗接受 | 待填 | 待填 | Pending |

Expected outputs 不得從 production engine 回填；審查者必須能依獨立來源或參考計算重核。工程實作者不得代填外部接受或簽名。

## 外部發布條件

- [ ] DEC-003 舊憑證撤銷證據已取得，或正式發布明確保持阻塞。
- [ ] 授權未確認的 PCR／係數未被發布或散布。
- [ ] 正式 hosting、email、object storage 與 secrets provider 已由 Owner／平台確認。
- [ ] 最新 CI 與安全閘門再次通過。

## 簽核決定

- [ ] 核准此 commit 作為 P0 Release Candidate。
- [ ] 有條件核准，條件如下。
- [ ] 不核准，需修正後重新驗收。

條件／原因：

`<填寫決定理由與限制>`

產品負責人：`<name / date / reference>`

碳足跡領域審查者：`<name / date / reference>`

## 注意事項

此簽核只代表內部 P0 UAT 結果，不代表 ISO 符合性聲明、第三方查驗、主管機關核定或產品碳足跡標籤資格。
