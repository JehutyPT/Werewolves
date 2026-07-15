# PRD #29 dedicated-branch execution log

This log records decisions made while executing PRD #29 autonomously with the
`fanout-loop` workflow.

## Branch policy

- PRD #29 and all child-issue work land only on `codex/prd-29`.
- `main` must remain unchanged. The user alone will review and merge the final
  dedicated branch into `main`.
- For this PRD, a child is considered landed for tracker-progression purposes
  after its exact commit is integrated, centrally verified, and reviewed on
  `codex/prd-29`. Closing a child therefore means "verified on the dedicated
  PRD branch," not "merged to main."
- Subsequent child contracts and candidates are based on the evolving
  `codex/prd-29` tip. This intentionally replaces the fanout-loop default-branch
  landing assumption so the tracker graph can advance without violating the
  no-merge instruction.

## Decisions and divergences

### 2026-07-15 — issue #79

- Integrated Already-Decided Role Composition classification through commit
  `71e5e7888eaf9e569fd505532396afa854d66622`.
- Central verification at that commit passed: solution restore; Mac Catalyst
  build with zero warnings and errors; Core tests with 251 passed and one known
  skip; Client tests with 195 passed.
- Fresh final Standards and Spec reviews both reported no findings.
- The canonical issue #79 contract remained byte-for-byte unchanged with
  SHA-256 `a09e0a94f3ce75549b0287b88baf9dc50da461a0f802e72ed92304209aa5d5c7`.

### Tracker graph correction

- Issue #85's contract says it consumes #79's Faction/Game Result bridge, but
  the live tracker graph omitted the formal `#85 blocked by #79` relationship.
- The contract-safe order is #79 followed by #85. The missing relationship is
  recorded before advancing the frontier; it does not change behavior after
  #79 closes, but preserves the dependency provenance.

