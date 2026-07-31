# CI validation — 2026-08-01

The governance branch preserves the legacy `Unsupported PCR formula rule set` diagnostic while also returning the stable `FORMULA-NOT-REGISTERED` rule code used by the extensible formula registry.

This compatibility adjustment prevents existing callers and tests from breaking while retaining deterministic machine-readable validation.

The calculation engine also permits multiple activities to reference the same immutable formula-definition version. Formula definitions are de-duplicated by version ID, and equivalent immutable definition instances are accepted through structural comparison of their fields and inputs.

Activities without an explicitly published formula definition must still use a supported PCR formula rule-set version. This preserves strict PCR governance while allowing versioned extensible formulas to be registered and executed independently.

The current validation scope additionally covers the governance console, authenticated governance API, role and permission matrix, verified-MFA checks for high-risk operations, evidence access auditing, tenant isolation, immutable governance records, definition publication locks, readiness acknowledgements, workflow transitions, archive generation and browser navigation.

Two-factor login and recovery routes remain enabled. Direct requests without the temporary two-factor identity cookie are safely redirected to the login page, while valid password-to-MFA login flows continue to the Identity two-factor pages.

The branch must pass the complete pull-request validation workflow before it can be considered for merge. Passing CI alone does not prove that issues #21–#30 satisfy every acceptance criterion; the completion review and issue-specific evidence remain authoritative.
