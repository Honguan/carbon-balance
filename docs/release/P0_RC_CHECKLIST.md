# P0 Release Candidate 檢核表

- [x] Release 建置零警告、格式檢查通過
- [x] Unit、Integration、Architecture、Contract、Security、Golden Case 全數通過
- [x] 真實註冊、Email 確認、TOTP MFA、五階段盤查、證據、審查與報表 E2E 通過
- [x] 空資料庫遷移及前一版 schema 升級通過
- [x] 備份、隔離還原及資料表／證據雜湊抽查通過
- [x] Golden Case、報表總額、manifest hash 與 run lineage 一致
- [x] 跨租戶、IDOR、CSRF、XSS、上傳及 rate limit 控制與自動測試通過
- [x] NuGet、檔案系統與容器映像 Critical/High 為零
- [x] SBOM、映像 digest、遷移 SQL、效能數據與測試報告已保存
- [x] WCAG 2.2 AA 基礎稽核無阻擋項目
- [x] Staging 設定、祕密、備份、監控、告警、回滾與事件手冊已審查
- [ ] 產品負責人與領域審查者完成人工 UAT 並簽核 RC

發布與正式部署不由 CI 自動觸發；必須經人工核准後依維運手冊執行。
