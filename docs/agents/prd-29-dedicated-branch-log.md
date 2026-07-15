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

### 2026-07-15 — issue #85

- Integrated deterministic headless simulation evidence through commit
  `dc61756be191d73d732c610c25ca9401333b6892`.
- One deterministic random source is shared by pre-game start-state derivation
  and baseline-random Moderator decisions for each Run Seed Material value.
- Terminal evidence accepts only the engine's bounded victory-transition pairs:
  `Dawn -> Day` records the current turn and Dawn Victory Check Window, while
  `Day -> Night` records the prior resolved turn and pre-Night Victory Check
  Window. Missing, duplicate, mismatched, unsupported, same-phase, and
  wrong-origin terminal signals remain Incomplete Simulation Runs.
- The execution layer returns `SimulationBatchSourceEvidence`, the minimal
  ordered run-record precursor owned by #85. Complete inventory-bearing
  Simulation Result Evidence remains assigned to #83, so #85 does not pull
  Possible Game Result inventory construction or aggregation forward.
- The public five-parameter `SelectPlayersInstruction` construction seam is
  preserved for existing Client callers while the explicit serialization
  overload carries machine-stable Role-identification metadata.
- Central verification at the integrated commit passed: solution restore;
  Release Mac Catalyst x64 build with zero warnings and errors; Core tests with
  275 passed and one known skip; Client tests with 195 passed.
- Fresh final Standards and Spec reviews both reported no findings. The
  canonical issue #85 contract remained byte-for-byte unchanged with SHA-256
  `e08da083d3c3ccb0e3bcf34188193c865eeb49e2c4a68e732506464b50b69659`.
