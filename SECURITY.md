# 安全政策

## 回報弱點

請透過 repository 的私密安全通報管道回報弱點，不要建立公開 Issue。若尚未設定外部通報管道，請直接聯絡 repository owner，且不要附上真實憑證、客戶資料或可被濫用的完整攻擊資料。

## 支援範圍

P0 Release Candidate 前不承諾正式環境支援。安全修正只套用於目前開發分支與已明確標記的 release。

## 敏感資料規則

- 密碼、API Key、連線字串與 SMTP 憑證只透過環境變數、user secrets 或受管 secret store 提供。
- 歷史 ZIP 已知可能包含暴露金鑰及服務帳戶檔案，只能在忽略且受控的分析目錄處理。
- Audit 與 structured log 不得記錄密碼、token、完整個資或敏感文件內容。
- 發現舊 secrets 時，只記錄來源位置、風險與撤銷建議，不複製其值。

## 發布門檻

未處理的 Critical／High 弱點、跨組織隔離失敗、secret scan 失敗或無法驗證備份還原時，不得發布。
