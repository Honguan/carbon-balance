# Legacy 遷移

流程固定為：`Extract → Preserve Raw → Stage → Validate → Transform → Recalculate → Review → Publish`。

`legacy-source-inventory.csv` 只含舊 ZIP 內的相對路徑、大小、SHA-256、風險分類與不可提交旗標，不含檔案內容。Phase 7 才會將可用資料解析至隔離的 `legacy_staging` schema；所有舊計算結果只供 comparison。
