# UI 工作流程回歸

從儲存庫根目錄執行。需要 .NET 10 SDK、PowerShell、Node.js，以及 agent-browser 與其 Chromium 瀏覽器。

1. 準備隔離的 PostgreSQL 測試資料庫，並先套用目前版本的所有 migration。將 `CARBON_TEST_DB_CONNECTION` 設為該資料庫的連線字串；工具不建立資料庫、不執行 migration，也不刪除既有資料。
2. 產生測試使用者、Owner 組織、合成 PCR／係數，以及 A、B 兩個盤查版本：

   ```powershell
   dotnet run --project tests/E2E/UiFixture/UiFixture.csproj --configuration Release
   ```

   預設輸出 `.analysis/ui-workflow.local.json`，包含僅供本機測試的帳號密碼；`.analysis/` 已由 Git 忽略。可在命令尾端加上 `-- .analysis/another-fixture.local.json` 指定新路徑。每次執行新增一套資料，既有輸出檔會使工具停止，避免覆寫憑證。請勿將輸出檔提交或分享。
3. 啟動連線至同一測試資料庫的 Web 應用程式，並執行：

   ```powershell
   pwsh -File tests/E2E/ui-workflow.ps1 -BaseUrl http://127.0.0.1:5088 -BrowserExecutable <agent-browser原生執行檔路徑>
   ```

   請先依 agent-browser 安裝方式備妥瀏覽器。Windows 請以 `-BrowserExecutable` 指定套件 `bin/agent-browser-win32-x64.exe` 的完整路徑，避免 `.cmd` 包裝器重新解讀 JavaScript 的特殊字元。其他平台可指定原生 CLI；省略此參數會使用 `npx agent-browser`。若首次啟動因背景瀏覽器程序持有輸出管道而未返回，可先在獨立終端用相同 `--session` 開啟登入頁，再執行腳本。使用其他資料檔時加上 `-FixturePath .analysis/another-fixture.local.json`。`-Session` 可指定獨立瀏覽器工作階段，`-LoginOnly` 只進行登入。

完整流程會修改 B 盤查的測試活動，驗證切換盤查、導覽保留上下文、無效提交保留輸入與小數精度、儲存、更正及停用。再次完整執行時請產生新的 fixture。完成畫面寫入 `.analysis/ui-workflow-complete.png`。

不需要瀏覽器的前端互動回歸：

```powershell
node tests/E2E/site-interactions.mjs
```
