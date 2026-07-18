# ADR-0002：ASP.NET Core 10 + PostgreSQL 18 模組化單體

- Status: Accepted
- Date: 2026-07-18

## Context

系統需要強型別數值、交易、權限、報告、背景工作與長期維護，但目前無微服務獨立擴展證據。

## Decision

採 ASP.NET Core 10 LTS、C#、PostgreSQL 18 與模組化單體。UI 以 server-rendered MVC/Razor Pages 為主，僅在必要處加入 TypeScript。

## Consequences

- 減少前後端雙重架構與運維成本。
- 能以 module boundaries、architecture tests 保持可拆分性。
- 需維持 .NET 與 PostgreSQL patch／升級政策。
- 未來只有在量測與組織邊界成立時才拆服務。
