# 貢獻指南

## 工作流程

1. 先讀取根目錄 `AGENTS.md`、相關需求、ADR 與驗收條件。
2. 使用短生命分支，維持單一 Issue 的最小完整垂直切片。
3. 同步更新程式、migration、測試與受影響文件。
4. 提交前執行格式、Release build、測試與必要的 migration／Golden Case 驗證。
5. PR 必須揭露資料、計算、授權、安全、相容性與 rollback 影響。

## 提交規則

- 不提交 secrets、真實客戶資料、授權不明係數或完整 ISO 文件。
- 不弱化或跳過測試以通過 CI。
- 不直接移植舊 PHP 的登入、SQL、資料表或憑證。
- 法規、PCR、係數與重大公式規則需有領域審核紀錄。

## 最低驗證

```powershell
dotnet restore
dotnet format --verify-no-changes
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
docker compose config
```
