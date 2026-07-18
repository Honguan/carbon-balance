# 架構文件

正式決策採模組化單體。Domain 層不得依賴 Web、EF Core、資料庫、檔案系統或外部 API；跨模組協作透過 application contract 或同程序 domain event。

目前核准決策：

- [ADR-0001：正式版重新建立](../adr/0001-rebuild-instead-of-refactor.md)
- [ADR-0002：ASP.NET Core 10 + PostgreSQL 18 模組化單體](../adr/0002-modular-monolith-dotnet-postgresql.md)
- [ADR-0003：係數版本化與不可變計算](../adr/0003-versioned-factors-and-immutable-calculations.md)

Phase 1 建立 solution 後，將補上模組依賴圖、資料流與 deployment view。
