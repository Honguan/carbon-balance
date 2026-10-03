# Liveness 與 readiness

`/health/live` 只確認 HTTP 程序能回應，不存取外部依賴。`/health/ready` 執行標記 `ready` 的 PostgreSQL、證據 bucket、ClamAV 與 keyring 檢查；任一不可用回 503，正常回 200。匿名 JSON 只含整體與固定元件名稱／狀態，不含 credentials、endpoint、例外或 key 資料。MOENV 不在 hard readiness，來源暫停不阻擋既有盤查。

每項檢查的 caller 期限為 3 秒；網路連線、讀取與 SDK 呼叫傳遞 cancellation。Compose Web 探 `/health/ready`，容器期限為 8 秒。支援獨立探測的 orchestrator 應將 liveness 指向 `/health/live`、流量 readiness 指向 `/health/ready`；不可因來源同步失敗重啟整個系統。

## 啟動前置條件

- 先依部署／儲存服務管理流程建立配置的 bucket。Development Compose／CI 的 SeaweedFS `S3_BUCKET` 在儲存啟動階段建立測試 bucket；若建立失敗，Web 的唯讀 probe 回 503。Production 外部儲存須由平台先 provision，健康 GET 不建立 bucket 或物件。
- 非 Development 必須指定持久化 `DataProtection:KeyPath`。Web 啟動時檢查該目錄讀寫及框架 Protect／Unprotect，初始化全新空 keyring；失敗立即停止啟動。只有 Web 啟動做此操作，`--migrate`／`--sync-factors` 不新增對 evidence 或 keyring 初始化的依賴。
- 配置的目錄與現有 key 必須可讀，且含可用未撤銷 key。健康檢查重新讀取 repository，驗證 key XML／encryptor，偵測遺失、毀損及撤銷，不在 GET 產生／輪替 key。

S3 bucket HEAD 證明當下 bucket／連線／憑證可用，不證明所有上傳、下載或 IAM 權限；最小權限與完整儲存操作另依 #58 驗收。ClamAV 使用官方 [NUL framing PING／PONG 協定](https://docs.clamav.net/manual/Usage/ClamdProtocol.html)，不傳送掃描檔案。

Filesystem 的 OS I/O 無法強制取消；keyring 使用一個共享的 in-flight 檢查，caller 可以逾時，不會因重複探測堆積讀取工作。使用本機持久化 volume，勿以可能永久掛起的網路共享作 keyring。唯讀 health 不能持續證明輪替寫入權限或所有歷史密文可解密；平台須另做權限、輪替與還原演練。

## 故障判讀

```sh
curl --fail http://127.0.0.1:8088/health/ready
curl --fail http://127.0.0.1:8088/health/live
```

ready 503／live 200 表示程序存活但必要依賴不可用。依安全元件名稱檢查平台連線、配置、bucket provisioning、掃描服務或 keyring；恢復後重新確認 ready 200。不要停用必要檢查或將 dependency 故障回報成功。

Windows PowerShell 手動啟動 SeaweedFS 時，將 `'-s3.port=9000'`、`'-admin.ui=false'` 等含點號旗標加引號；否則 native argument parsing 會拆成錯誤參數。Compose 的字串參數陣列不受影響。
