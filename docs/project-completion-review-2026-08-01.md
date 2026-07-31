# Project completion review — 2026-08-01

## Target decision

The governance branch is designed to exceed the 90-percent implementation threshold after the complete CI pipeline is green. It must remain a draft and must not close issues #21–#30 while any required test is failing.

## Weighted completion model

| Area | Weight | Implemented | Evidence |
| --- | ---: | ---: | --- |
| Domain rules and calculations | 20 | 20 | Versioned PCR, readiness, data quality, allocation, formulas, transport, factors, evidence, workflow and archive services |
| Persistence and migration | 20 | 20 | EF entities, tenant filters, immutable guards, governance/evidence/archive migration and empty-database migration test |
| Application integration | 15 | 15 | Governance workspace service, calculation manifest/line trace integration, invalidation and audit events |
| User interface and API | 15 | 14 | Governance console, role-aware actions, readiness, evidence, workflow, archive downloads and CSRF-protected API |
| Authorization and integrity | 10 | 10 | Explicit Verifier role, governance permissions, MFA claim plus account setting, separation of duties, SHA-256, malware scan and retention |
| Automated verification | 15 | 12 | Unit, Golden Case, integration, security, architecture, contract and browser flows; complete CI must confirm this branch |
| Documentation and operations | 5 | 4 | Role matrix, state diagram, API, archive, migration and rollback guide; formal external UAT remains |
| **Total after green CI** | **100** | **95** | Release decision remains separate from implementation completion |

## Issue status assessment

| Issue | Implementation status | Remaining acceptance evidence |
| --- | --- | --- |
| #21 Versioned PCR rules | Complete implementation | Attach final expired/withdrawn/superseded browser evidence and import guide reference |
| #22 Inventory readiness | Complete implementation | Attach API/UI integration test output |
| #23 Data quality and uncertainty | Complete implementation | Add representative production scoring configuration and UAT sample |
| #24 Allocation pools | Complete implementation | Add browser fixtures for every allocation method |
| #25 Extensible formulas | Complete implementation | Confirm all formula boundary fixtures in green CI |
| #26 Transport chains | Complete implementation | Add bulk-import UX and refrigerated-route browser fixture |
| #27 Global factors | Substantially complete | Add scheduled adapters for additional official sources beyond MOENV |
| #28 Evidence chains | Complete implementation | Production object-storage retention policy validation |
| #29 Verification workflow | Complete implementation | External verifier UAT and optional outbound notification dispatcher |
| #30 Verification archive and impact | Complete implementation | Backward-reader fixture for the next archive schema version |

## Release blockers

1. Every current CI job must be green on the exact PR head.
2. The project must be installed against an empty PostgreSQL database and upgraded from the previous schema.
3. A production-like object storage and malware scanner smoke test must pass.
4. Formal UAT must confirm the organization owner, contributor, reviewer and verifier paths.
5. Only then may the draft PR be marked ready, merged, issues closed with acceptance evidence, and a stable release prepared.

## Residual scope

The estimated remaining five percent is operational rather than missing core logic: additional official-source adapters, outbound email/webhook dispatch, exhaustive browser fixtures for every method combination, formal verifier UAT, and future archive-schema backward-reader fixtures.
