# GitHub Repository 與治理

## 1. Repository 建議

```text
carbon-footprint-system/
├─ .github/
│  ├─ workflows/
│  ├─ ISSUE_TEMPLATE/
│  ├─ PULL_REQUEST_TEMPLATE.md
│  ├─ CODEOWNERS
│  └─ dependabot.yml
├─ src/
├─ tests/
├─ docs/
├─ infra/
├─ scripts/
├─ AGENTS.md
├─ CONTRIBUTING.md
├─ SECURITY.md
├─ LICENSE
├─ README.md
├─ .editorconfig
├─ Directory.Build.props
└─ global.json
```

## 2. Branch 策略

- `main`：隨時可發布，禁止直接 push。
- 短生命 feature branch：`feat/<issue>-...`、`fix/<issue>-...`。
- 不建立長期 `develop`，除非 release 流程確有需要。
- 舊版保存在獨立 repository 或 `legacy-final` tag，不與新 main 混合。

## 3. Branch Protection

`main` 至少要求：

- Pull Request。
- 1 名 reviewer；計算、法規、migration、安全變更要求對應 CODEOWNER。
- Required checks 全綠。
- Conversation resolved。
- Linear history 或 squash merge。
- 禁止 force push 與 branch delete。
- 管理員也遵守規則。
- 重要 release 使用 Git tag 與 provenance／簽章能力可用時啟用。

單人專案可用 GitHub Ruleset + required checks；對無法自行批准的限制，可採「PR + CI + explicit self-review checklist + protected release environment」，不要關掉所有保護。

## 4. CODEOWNERS 概念

```text
/docs/compliance/      @domain-owner
/src/**/Calculations/  @domain-owner @backend-owner
/src/**/Factors/       @domain-owner @backend-owner
/src/**/Identity/      @security-owner @backend-owner
/infra/                @platform-owner
/.github/workflows/    @platform-owner @security-owner
```

小團隊可由同一人暫代多個角色，但 PR 仍需記錄所扮演的審核角色。

## 5. CI Workflows

### pull-request.yml

1. Checkout。
2. Setup pinned .NET SDK。
3. Restore with lock file。
4. Format check。
5. Build Release。
6. Unit tests。
7. Integration tests with PostgreSQL service container。
8. Architecture tests。
9. Migration from empty DB。
10. Secret scan。
11. Dependency／container vulnerability scan。
12. Upload test results與 coverage。

### main.yml

- 上述 checks。
- 建立 container image。
- 產生 SBOM。
- 簽署／產生 provenance。
- Push 到 registry。
- 部署 staging。
- Smoke tests。

### release.yml

- 只接受已審核 tag。
- 需 GitHub Environment approval。
- 備份與 migration preflight。
- 部署 production。
- 執行 smoke／health／calculation canary。
- 失敗自動停止，不自動破壞性 rollback DB。

### scheduled.yml

- Dependabot／dependency audit。
- Base image rebuild。
- Backup restore rehearsal（可在專用環境）。
- PCR／係數來源 availability monitor，只產生報告，不直接發布資料。

## 6. Label 系統

```text
type:feature
type:bug
type:security
type:compliance
type:data-migration
type:documentation
area:identity
area:inventory
area:factors
area:calculation
area:reporting
priority:p0
priority:p1
priority:p2
status:blocked-domain
status:needs-decision
breaking-change
good-first-issue
```

## 7. PR 最低內容

- 關聯 Issue。
- 問題與範圍。
- 設計／ADR。
- 資料與 migration 影響。
- 安全與授權影響。
- 測試證據。
- 畫面變更截圖。
- 法規／公式由誰審核。
- Rollout 與 rollback。

## 8. Release 策略

- 使用 SemVer。
- Schema migration 與 application image 成對發布。
- 法規／PCR／係數資料版本不綁死 application SemVer，另有 dataset version。
- Release notes 分成：Features、Fixes、Security、Data/Rule Changes、Migration、Known Issues。
- 正式 release artifact 含 image digest、SBOM、migration bundle、checksum。
