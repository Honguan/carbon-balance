# 架構文件

正式決策採模組化單體，沿用 Domain／Application／Infrastructure／Web 四個專案。五階段共享盤查與計算引擎。Domain 層不得依賴 Web、EF Core、資料庫、檔案系統或外部 API。

- [目前產品需求](../04_PRODUCT_REQUIREMENTS.md)
- [目前技術架構、模組責任與資料流](../05_ARCHITECTURE.md)
- [old 來源分析、證據與架構修正計畫](LEGACY_PURPOSE_ANALYSIS.md)

目前核准決策：

- [ADR-0001：正式版重新建立](../adr/0001-rebuild-instead-of-refactor.md)
- [ADR-0002：ASP.NET Core 10 + PostgreSQL 18 模組化單體](../adr/0002-modular-monolith-dotnet-postgresql.md)
- [ADR-0003：係數版本化與不可變計算](../adr/0003-versioned-factors-and-immutable-calculations.md)
- [ADR-0004：受控公共係數同步](../adr/0004-curated-public-factor-sync.md)
- [ADR-0005：組織 SMTP](../adr/0005-organization-smtp-settings.md)
- [ADR-0006：單一組織上下文](../adr/0006-single-organization-context.md)
- [ADR-0007：五階段主流程與快照邊界](../adr/0007-lifecycle-workflow-and-snapshot-boundary.md)

快照讀取已由 Web 移至 Infrastructure，透過 Application 契約供計算、送審與結果有效性檢查共用。其餘 Workspace 資料操作仍有跨模組編排；不可將目標邊界誤寫為已全部完成。
