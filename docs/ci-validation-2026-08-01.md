# CI validation — 2026-08-01

The governance branch preserves the legacy `Unsupported PCR formula rule set` diagnostic while also returning the stable `FORMULA-NOT-REGISTERED` rule code used by the extensible formula registry.

This compatibility adjustment prevents existing callers and tests from breaking while retaining deterministic machine-readable validation.

The calculation engine also permits multiple activities to reference the same immutable formula-definition version. Formula definitions are de-duplicated by version ID instead of incorrectly requiring exactly one activity per definition.

The branch must pass the complete pull-request validation workflow before it can be considered for merge. Passing CI alone does not prove that issues #21–#30 satisfy every acceptance criterion; the completion review and issue-specific evidence remain authoritative.
