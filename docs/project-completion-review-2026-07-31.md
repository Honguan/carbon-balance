# Project completion review — 2026-07-31

## Decision

**Do not close issues #21–#30, merge this pull request as a completed feature set, or publish version 1.0.0 yet.**

The branch provides substantial domain-layer foundations and fixes an inventory-creation defect discovered by browser testing. However, most acceptance criteria require persistent entities, migrations, application-layer integration, UI/API flows, immutable calculation-manifest integration, exports, audit events, authorization, integration tests, browser end-to-end tests, and technical documentation that are not yet implemented.

## Current validation status

The existing pull-request pipeline verifies:

- locked restore and formatting;
- Release build;
- migrations against an empty PostgreSQL database;
- unit, Golden Case, integration, architecture, contract, and security tests;
- coverage and dependency audit;
- Bash and Windows PowerShell setup paths;
- Docker Compose validation;
- secret scanning;
- image vulnerability scanning and SBOM generation;
- browser authentication and workspace workflows.

A browser test exposed a real `NullReferenceException` in inventory creation when exclusions were omitted. The branch changes the PCR validation input from `exclusions.Trim()` to `exclusions?.Trim() ?? string.Empty`.

## Issue completion matrix

| Issue | Implemented in this branch or current main | Acceptance gaps | Status |
| --- | --- | --- | --- |
| #21 Versioned PCR rules | Versioned PCR records and stage rules already exist; source metadata and SHA-256 fields exist; PCR compatibility is executed during inventory creation; supersession concepts and tests exist. | Complete acceptance verification for expired, withdrawn, custom-fallback and superseded scenarios; affected-project UI; technical import/versioning documentation; full integration/browser matrix. | Partial |
| #22 Inventory readiness gate | Deterministic validator, stable rule codes, severities, remediation, acknowledgement rules, PCR/factor/evidence/allocation/manifest checks. | EF persistence of validation runs and acknowledgements; application service; UI links/grouping; submission/API/export/audit integration; complete integration and browser tests. | Foundation only |
| #23 Data quality and uncertainty | Versioned five-dimension scoring, controlled source categories, deterministic hash, sensitivity ranking, bounds and seeded Monte Carlo. | Persisted assessments and rule sets; calculation-manifest integration; result UI/reporting; low-quality hotspot view; integration tests; scoring and uncertainty documentation. | Foundation only |
| #24 Allocation pools | Versioned pool model; supported methods; denominator, basis, evidence and 100-percent validation; auditable trace; invalidation comparison. | EF entities/migrations; application commands; draft invalidation; immutable calculation-line linkage; reports/exports; pool UI; integration and browser coverage for every method. | Foundation only |
| #25 Formula framework | Registry-based execution without a central calculation switch; published definitions; required inputs and unit checks; direct, factor, mass-balance and energy-balance implementations; calculation trace. | Persisted formula/activity definitions; administrative review/publication; dynamic UI rendering; current-engine integration and migration compatibility; immutable manifest storage; every formula boundary test; integration/browser tests; developer guide. | Foundation only |
| #26 Transport chains | Ordered multi-leg model; seven modes; five calculation methods; load, empty-return, refrigeration and TTW/WTT/WTW calculations; template instantiation; traces. | EF entities/migrations; route-template management; bulk import; application/UI integration; calculation-manifest and Excel/archive export integration; integration/browser tests for refrigerated and invalid routes. | Foundation only |
| #27 Global factor catalog | Stable identifiers and deterministic fallback keys; global versions, aliases, organization activation/override concepts; idempotent synchronization; withdrawal/removal and impact analysis. | Global non-tenant persistence and migration from organization copies; custom-factor compatibility; scheduled source adapters; organization UI; historical-reference migration tests; complete integration matrix and architecture documentation. | Foundation only |
| #28 Evidence chains | Logical documents and immutable versions; server SHA-256; scan metadata; physical deduplication; reusable links; access events; retention locks; publish-gate validation. | EF entities/migrations; object-storage version and authorization integration; multi-upload/replacement UI; factor/PCR review gates; readiness/export integration; download audit; integration and browser tests for scans, permissions and retention. | Foundation only |
| #29 Verification workflow | Central state machine; separation of duties; MFA rules; structured findings; resubmission concepts; sampling, conclusion and signed-record validation; status invalidation. | Persisted workflow/review/verification entities; snapshot creation; authorization policies and UI; notifications and expiry scheduling; audit integration; integration/browser tests; state diagram and role matrix. | Foundation only |
| #30 Verification archive and impact | Deterministic ZIP builder; required file inventory; per-file SHA-256 and archive digest; verification; version comparison, deltas, hotspots and governed-dependency impact analysis. | Build files from a selected immutable calculation run; authorization and audit record; scenario separation; export endpoint/UI; backward-compatible schema documentation; download/integration tests. | Foundation only |

## Release blockers

1. No issue from #21 through #30 currently satisfies every acceptance criterion.
2. New domain models are not represented by EF Core migrations or durable records.
3. New services are not connected to normal application commands, UI/API endpoints, immutable manifests, exports and audit trails.
4. Required integration and browser scenarios are absent.
5. Operational and governance documentation is incomplete.
6. A stable version number and automatic release must not be introduced until the preceding blockers are resolved and the full pipeline is green.

## Recommended delivery order

1. Finish #21 and #22 first because every submission and later verification feature depends on executable PCR validation and a persistent readiness gate.
2. Complete #28 and #29 next because evidence integrity, authorization, review findings and immutable snapshots are P0 release controls.
3. Complete #24, #25 and #26 together with calculation-manifest and export integration.
4. Complete #23 and #27 with durable governance and migration coverage.
5. Finish #30 after all upstream immutable records are available, then run the full acceptance suite and publish the stable release.

## Merge and release policy

- Keep #21–#30 open until their own acceptance evidence is attached.
- Do not use closing keywords in a foundation pull request.
- Merge only independently deployable slices with migrations, rollback notes, tests and documentation.
- Publish a stable release only from a green `main` commit after version, changelog, source archive, checksums, migration smoke test and installation smoke test are verified.
