# 舊資料與資產遷移計劃

## 1. 原則

舊資料不是可信正式資料，只是待驗證來源。遷移採：

```text
Extract → Preserve Raw → Stage → Validate → Transform → Recalculate → Review → Publish
```

禁止直接把舊 SQL dump restore 到正式 schema。

## 2. Phase M0：封存

1. 建立原始 ZIP、SQL、Excel、PDF 的 SHA-256 manifest。
2. 保存唯讀副本與存取權限。
3. 記錄來源、匯出時間、編碼與資料庫版本（能確認時）。
4. 立即撤銷舊 API Key／密碼。
5. 不在公開 GitHub 上傳含個資、憑證或授權不明的資料。

## 3. Phase M1：盤點與分類

每個舊表／檔案標記：

- 使用中／展示／測試／重複／未知。
- 個資／商業機密／公開資料。
- 可重算／不可重算。
- 來源完整／缺來源。
- 單位明確／不明。
- 有效版本／歷史版本／未知。

## 4. Phase M2：Staging Schema

建立獨立 `legacy_staging` schema，欄位優先保留原始文字：

```text
source_file
source_table
source_row_number
raw_payload_json
raw_hash
parse_status
validation_errors
import_batch_id
```

先保存原貌，再轉型；轉型失敗不得靜默設為 0。

## 5. Phase M3：資料清洗

### 係數

- 拆分數值與單位。
- 正規化科學記號。
- 建立名稱 alias。
- 補來源 dataset/document。
- 解析公告年份、盤查期間、區域與驗證狀態。
- 找出重複與衝突，不自動合併數值不同的記錄。

### 盤查資料

- 建立產品與盤查專案 ID。
- 將文字日期轉為 date；無法判斷時列入錯誤清單。
- 將名稱關聯改為 ID，但保存原始名稱。
- 單位不明的數值不得直接發布。
- 空字串、NULL、0 分開處理。

## 6. Phase M4：重新計算

舊 `碳排放量`、階段總和與產品總和只作 comparison，不作新系統權威結果。

1. 以新計算引擎重算。
2. 比較 legacy result 與 new result。
3. 對差異建立分類：單位、四捨五入、公式、係數、資料、舊 bug、未知。
4. 由領域審核者決定預期結果。
5. 通過後成為 Golden Case。

## 7. Phase M5：發布

只有符合下列條件的資料可進 production：

- 組織與資料擁有者已確認。
- 來源與授權可追蹤。
- 日期、數值、單位與關聯可解析。
- 係數版本已審核。
- 新引擎可重現結果。
- migration review 已簽核。

其餘資料留在隔離 archive，不因趕進度而強行轉換。

## 8. Rollback

- 每批 migration 具 batch ID。
- 正式發布前做資料庫 snapshot。
- 新資料以 batch 標記，能停用／回滾映射，但 audit 不刪除。
- 舊系統保持唯讀，直到新系統完成至少一個完整盤查週期與備份還原演練。

## 9. 遷移驗收

- Raw manifest 數量與 checksum 完整。
- 所有 staging row 都有成功或明確失敗狀態。
- 無未解釋的轉型為 0／NULL。
- 係數重複與衝突報告完成。
- Golden Case 差異在核准容許範圍內。
- 可從新資料追到舊來源檔與列。
