# 舊程式碼稽核報告

## 1. 稽核範圍

來源：`public_html_Carbon-master.zip`  
排除：`assets/temp/**` 的備份／暫存副本  
檢查內容：目錄結構、PHP 語法、資料庫 dump、登入、資料寫入、計算流程、secret、測試與交付能力。

## 2. 程式規模

| 類型 | 檔案數 | 約略行數 |
|---|---:|---:|
| PHP | 45 | 3,740 |
| HTML | 21 | 2,266 |
| JavaScript | 10 | 1,013 |
| CSS | 8 | 1,311 |
| Python | 3 | 63 |
| SQL | 3 | 2,029 |
| Markdown | 1 | 58 |

PHP 8.4.16 對 45 個非 temp PHP 檔執行 `php -l`：**0 個語法錯誤**。這只代表語法可解析，不代表功能、安全或資料正確。

## 3. 高風險發現

### L-001：硬編碼 root 資料庫帳號

`dbtools.inc.php:4`：

```php
mysqli_connect("localhost:3306", "root", "")
```

影響：無密碼 root、無環境隔離、無最小權限、無 secret rotation。

### L-002：管理員驗證使用指定運算子

`signin/login.php:62`：

```php
if ($administrator_password = $password)
```

此處是賦值，不是比較，構成嚴重驗證繞過風險。

### L-003：SQL Injection 與明文密碼

`signin/login.php` 直接把 `$_POST` 串入 SQL，並直接比較資料庫密碼字串。專案內未發現 `password_hash` 或 `password_verify`。

### L-004：啟用連結只依 account 更新

`signin/checkcode_database.php` 只接收 `?account=`，未驗證一次性 token、簽章、期限或重放。

### L-005：洩漏 Google Maps API Key

下列檔案包含同一把明文 API Key：

- `set_Map/set_map.php`
- `set_Map/2023-11-26_14-42_set_map copy 2.php`

應視為已洩漏並撤銷或重新限制。

### L-006：授權模型缺失

非 temp PHP 中只找到 2 次 `session_start()`，主要盤查、係數、製造、運輸、使用與廢棄階段端點沒有一致的登入及組織授權檢查。

### L-007：大量直接輸入與動態 SQL

掃描結果：

| 模式 | 次數 |
|---|---:|
| `$_GET` | 37 |
| `$_POST` | 135 |
| `SELECT *` | 39 |
| `INSERT INTO` | 20 |
| `UPDATE` | 21 |
| `DELETE FROM` | 5 |
| `password_hash` | 0 |
| `password_verify` | 0 |
| CSRF 關鍵字 | 0 |

雖有少數 `mysqli_prepare`，並非一致的資料存取策略。

## 4. 架構問題

### L-008：多個資料庫名稱混用

`execute_sql` 呼叫中出現：

- `carbon`
- `shopping`
- `碳足跡`
- `碳足跡資料庫`

登入程式仍顯示「歡迎來到超皮購物」，證明帳號模組來自其他專案且未完成整合。

### L-009：頁面、計算與寫入耦合

例：

- `Material_Stage/material_Stage.php` 以硬編碼產品名稱查詢，並在頁面流程內更新碳排。
- `Product_Inventory_Form/New_Product_Inventory_Form.php` 直接將表單欄位串入 INSERT。
- 多個階段頁面同時負責 HTML、SQL、計算與控制流程。

後果：無法隔離測試、重用或重現計算。

### L-010：原型／測試檔混入正式路徑

包括：

- `Nothing_Manufacturing_Phase_Summary.php`
- `hello.html`
- `hi.py`
- `TEST.php`
- `Use_mode_copy.php`
- `set_map copy.php`
- `Python-Test/`
- 日期命名與 `copy` 目錄

### L-011：README 與實際結構不一致

README 只有基礎說明，缺少可重建環境、資料庫版本、依賴、migration、測試、部署與 secrets 設定。

## 5. 資料庫問題

### L-012：MyISAM 與 InnoDB 混用

`原物料運輸`、`配銷階段總表`、`廢棄物回收階段` 等使用 MyISAM，無法可靠使用交易與外鍵。

### L-013：數值與日期以字串保存

例：

- `碳排放量 varchar(100)`
- `GHG排放系數 varchar(100)`
- `GWP varchar(100)`
- `消耗量 varchar(100)`
- `盤查起始時間 varchar(10)`

這會造成排序、驗證、換算、精度與聚合不可靠。

### L-014：係數資料未正規化

`碳足跡係數` 表：

- 無明確主鍵。
- 數值、數量、宣告單位混為文字。
- 缺少來源文件版本、有效起訖、匯入批次、授權、品質等級、撤銷狀態。

### L-015：關聯與 dump 完整性不足

- 產品名稱、盤查表文字等被當作關聯線索。
- SQL dump 中出現參照未完整提供的製造階段表。
- `製造階段id` 定義可見多餘逗號風險。
- 缺少完整、可重複執行的 migration 歷史。

## 6. 可用資產

| 資產 | 用法 |
|---|---|
| 五階段頁面與欄位 | 建立新表單欄位候選清單 |
| 係數資料庫 dump | 經清洗後建立 import staging 與 fixture |
| 計算公式與案例 | 建立 Golden Tests |
| 地圖／距離流程 | 重新定義 provider abstraction 與人工覆寫 |
| 使用情境公式 | 轉為受控 expression model，不執行任意程式碼 |
| 畫面截圖與展示流程 | 作 UX 參考，不直接複製程式 |

## 7. 最終評級

| 面向 | 評級 | 說明 |
|---|---|---|
| 需求研究價值 | 高 | 報告、案例、流程具參考價值 |
| UI 原型價值 | 中 | 可理解操作順序 |
| 程式可維護性 | 低 | 高耦合、重複、混雜測試檔 |
| 安全性 | 不可接受 | 登入繞過、SQL Injection、明文 secret |
| 資料可稽核性 | 不可接受 | 缺版本、快照、來源鏈 |
| 正式部署準備度 | 不可接受 | 無 CI、測試、migration、secret 管理 |

**處置：禁止直接上線；建立新正式系統。**
