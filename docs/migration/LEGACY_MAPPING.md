# Legacy 係數映射與對帳規則

Legacy 資料只進入 `staging` schema；任何列都必須保留來源檔名、批次、列號、原始 JSON、原始 SHA-256、解析狀態及錯誤，不得直接發布。

| Legacy CSV 欄位 | Staging／新模型 | 驗證 |
|---|---|---|
| `name` | raw payload → factor version name | 不可空白 |
| `value` | raw string → decimal factor value | invariant decimal、不得為負；轉型失敗不可改為 0 |
| `denominator_unit` | raw string → controlled unit code | 必須存在於指定 unit catalogue version |
| `source_version` | factor dataset version | 不可空白，未確認有效性維持 staging |
| `license_code` | factor license code | 不可空白，未確認授權不得發布 |

空字串、NULL、字面值 `0` 與無效數值分開記錄。重複 checksum 批次拒絕；批次內相同自然鍵列標記 `Conflict`，其餘驗證錯誤列標記 `Invalid`。映射修正建立新批次，不覆寫或刪除舊批次。

舊結果只作 comparison，重算差異分類為：輸入差異、單位換算、係數版本、PCR／公式版本、四捨五入或無法解析。只有經領域審查的新引擎結果可成為 Golden Case。

2026-09-07 的本機 `old/` 已找到滑鼠、衛生紙、瓶裝水的五階段工作簿，以及電扇／電鍋使用階段案例；不再沿用早期 ZIP 清單的「未發現試算表」結論。衛生紙及瓶裝水工作簿含失效參照，衛生紙另有外部連結；全廠分配、處理占比與使用情境仍需確認。來源、儲存格定位及 SHA-256 見[old 分析](../architecture/LEGACY_PURPOSE_ANALYSIS.md)。目前 importer 仍僅處理係數 CSV staging，並未新增完整工作簿匯入或發布候選答案。
