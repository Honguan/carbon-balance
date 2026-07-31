# Changelog

All notable changes to Carbon Balance are documented in this file.

## Unreleased

### Governance foundations

- Added a deterministic inventory readiness validator with stable rule codes, severities, remediation guidance and acknowledgement requirements (#22).
- Added versioned, dimension-level data-quality scoring, deterministic sensitivity analysis, uncertainty intervals and optional seeded Monte Carlo analysis (#23).
- Added shared-resource and co-product allocation calculations with validation and auditable traces (#24).
- Added a registry-based activity formula framework with published definitions, dynamic input validation and formula traces (#25).
- Added multi-leg transport-chain calculations for road, rail, sea, air, inland waterway, pipeline and custom modes with TTW, WTT and WTW components (#26).
- Added a global official-factor catalogue model with stable keys, organization activation concepts, idempotent synchronization and impact analysis (#27).
- Added a multi-document evidence-chain model with server-computed SHA-256, scan records, immutable replacement versions, physical deduplication, reusable links, access logs and retention locks (#28).
- Added a verification workflow model with centralized transitions, separation of duties, MFA requirements, structured findings, sampling and signed-record validation (#29).
- Added deterministic verification archive construction, per-file SHA-256 indexes, project-version comparison, hotspot deltas and governed-dependency impact analysis (#30).

### Fixes

- Fixed inventory creation when optional exclusions are omitted by passing an empty value to PCR validation instead of dereferencing a null string.

### Validation

- Added unit coverage for readiness blocking, data-quality scoring, uncertainty, allocation, formula execution, multi-leg transport, factor synchronization, evidence integrity, workflow authorization, deterministic archives and project comparisons.
- Added `docs/project-completion-review-2026-07-31.md` to document the remaining persistence, application, UI, export, authorization, integration-test and browser-test work required before issues #21–#30 can be closed.

> This section is not a stable release. No version tag or GitHub Release should be created from this work until every issue acceptance criterion is satisfied and the complete pipeline is green.
