# Legacy staging 匯入

Legacy 來源永遠視為不可信且唯讀。匯入工具只會把逐列原始 payload、來源與列 checksum、解析錯誤及衝突寫入 `staging` schema，不會直接建立或發布正式係數。

完整流程固定為：`Extract → Preserve Raw → Stage → Validate → Transform → Recalculate → Review → Publish`。目前工具只負責 Stage／Validate；後續步驟不得自動跳過人工審查。

`legacy-source-inventory.csv` 只含舊 ZIP 內的相對路徑、大小、SHA-256、風險分類與不可提交旗標，不含舊檔內容；舊計算結果只供 comparison。

## 係數 CSV 格式

來源必須是有效 UTF-8 CSV，且包含：

`name,value,denominator_unit,source_version,license_code`

檔案上限 50 MiB；數值以 invariant decimal 解析；單位必須存在於受控目錄。相同組織、entity type 與來源 SHA-256 不可重複匯入。

## 執行

先由 secret manager 或本機環境提供 migration 專用連線字串，不得放入參數或 repository：

```powershell
$env:CARBON_MIGRATION_DB_CONNECTION = Read-Host '輸入 migration DB connection string'
dotnet run --project tools/CarbonFootprint.LegacyImporter -- `
  --organization '<organization-guid>' `
  --input 'fixtures/legacy/factors-sample.csv'
```

結束碼 `0` 表示全數 Parsed；`4` 表示 staging 完成但含 Invalid／Conflict，需人工處理；其他非零值表示匯入未完成。
