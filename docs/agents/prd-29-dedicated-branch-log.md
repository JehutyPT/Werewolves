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

### 2026-07-15 — issue #46

- Integrated the Wild Child later-night progression repair through commit
  `db23f48cb3d10de091d1673d66be27bc8d1eed50`.
- A standard later-night Role pass may now end in either Woken Up or Asleep;
  this preserves the Wild Child's Night 1 model selection and later
  model-elimination transformation while allowing its no-action Night 2+
  path to complete.
- The diagnosed `core-simulator@1` / `baseline-random@1-splitmix64` run 11
  replay now returns a Completed Simulation Run with ending Turn 2 or later.
  Its regression assertion intentionally does not freeze a winning Faction,
  exact Game Result, exception, localized copy, transcript, or private state.
- Central verification at the integrated commit passed: solution and Mac
  Catalyst runtime restore; Release Mac Catalyst x64 build with zero warnings
  and errors; Core tests with 275 passed and one known skip; Client tests with
  195 passed.
- Fresh final Standards and Spec reviews both reported no findings. The
  prepared issue #46 contract used SHA-256
  `05e4c0136e41b77f90fba8ad552f80069e96b7b230f988e944e1b81a03554c37`.

### 2026-07-15 — issue #83

- Integrated the terminal lobby-evaluation boundary through commit
  `1db8aeec155647e20dfffdc226168a514242aa6c`.
- The public evaluator preserves distinct rules-invalid, app-unsupported,
  simulator-unsupported, could-not-evaluate, already-decided, degenerate, and
  probability meanings while enforcing the fixed already-decided, 1,000-run
  screening, and 10,000-run probability order.
- Simulator profiles declare the Shared Victory capabilities they can produce;
  the active profile declares none. Possible Game Results therefore remain
  the canonical single-Faction rows plus No-Winner unless a profile explicitly
  supplies an in-inventory Shared Victory capability.
- Simulation Result Evidence preserves every attempted source record,
  compatibility identity, inventory, and Completed/Incomplete count. Any
  Incomplete run makes all frequency projections reject the evidence, so a
  completed-only partial distribution cannot escape. Undefined Game Result
  subclasses are rejected before aggregation.
- Central verification at the integrated commit passed: Release Mac Catalyst
  x64 build with zero warnings and errors; Core tests with 295 passed and one
  known skip; Client tests with 195 passed.
- Fresh final Standards and Spec reviews both reported no findings. The
  canonical issue #83 contract remained byte-for-byte unchanged with SHA-256
  `b4b1d9cb00b22d641d6dda74041a4dbd396507020e77dd676b3b466ed904ff2c`.

### 2026-07-15 — issue #80

- Integrated the versioned terminal lobby cache-record boundary through commit
  `3f5bc397b2afe9a40b3a367a63aac4560e31ab7e`.
- Preparation repaired an authority contradiction in the earlier contract:
  the current `core-simulator@1` public evaluator cannot produce a Shared or
  No-Winner already-decided result. Current-profile cache records therefore
  accept only the exact evaluator-derived Werewolf single-Faction result with
  `WerewolfControlShortcut` and reject fabricated Shared, No-Winner, or
  mismatched result/reason pairs. A future genuinely producing profile must
  carry the required profile/schema versioning and public-path fixture.
- Canonical identity decoding checks Player Count 5–30 and at most 32 physical
  Role cards before expanding Role counts. Syntactically valid oversized
  identities are rejected without attacker-controlled materialization.
- The single-record and collection codecs share one strict canonical JSON
  schema, preserve complete exact aggregate inventories and Turn/window cells,
  and reject an invalid member or aggregate as a whole. Terminal records retain
  no per-run evidence or storage provenance.
- Possible Game Result inventory derivation is shared by the evaluator and
  cache validation, avoiding a second profile-semantics implementation.
- Central verification at the integrated commit passed: Release Mac Catalyst
  x64 build with zero warnings and errors; Core tests with 378 passed and one
  known skip; Client tests with 195 passed.
- Fresh final Standards and Spec reviews both reported no findings. The repaired
  canonical issue #80 contract remained byte-for-byte unchanged during
  implementation and review with SHA-256
  `5a48855511591c4ea0f0d30a7ec47757c7546898fcb7f8ba514d4acd6b287900`.
