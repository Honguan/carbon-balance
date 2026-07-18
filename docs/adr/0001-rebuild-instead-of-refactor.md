# ADR-0001：正式版重新建立

- Status: Accepted
- Date: 2026-07-18

## Context

舊版為 raw PHP/MariaDB 學校專題原型，存在驗證繞過、SQL Injection、明文 secret、多資料庫混用、資料型別錯誤、無版本與稽核、無測試與 migration 等問題。

## Decision

建立全新正式 repository。舊版保存為唯讀參考，只提煉需求、案例、資料與 UI 線索，不直接移植登入、資料存取、schema 或計算頁面。

## Consequences

正面：安全與資料模型能從正確基礎建立，計算可測試與重現。
負面：初期看似重複實作，需另外做 legacy migration。
降低成本：先完成單一 Golden vertical slice，再逐階段擴充。
