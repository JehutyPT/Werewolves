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

### 2026-07-15 — issue #78

- Integrated Client lobby-evaluation lookup and fallback orchestration through
  commit `498e936528e1e512a8d90eb15643e2c6cd1630c0`.
- `LobbySetupState` now emits one scenario-identity notification only for
  effective Player-count or Role-composition changes. Player names and Seating
  Order remain outside Simulation Scenario and cache identity.
- The Client coordinator applies strict bundled-then-local semantic lookup,
  preserves the 500 millisecond quiet period, accelerates fallback through one
  atomic Lobby Exit-attempt operation, bounds synchronous Core evaluation at
  10 seconds with injected time, and publishes only the latest unchanged
  scenario. Late reads, evaluator completion, timeout, failure, and persistence
  cannot overwrite or release a newer scenario's state.
- Local persistence stages bytes asynchronously and authorizes the actual
  atomic replace under the current request generation. Native writes are
  serialized, each writer owns its temporary file, and stale work cannot
  commit or damage the current write. Runtime cancellation observes late
  evaluator faults, invokes callbacks outside request locks, and disposes each
  request cancellation source after its pipeline drains.
- Native and Browser QA hosts resolve the same host-agnostic coordinator
  boundary with production adapters versus bounded semantic fakes. Public
  tests use state/events and controlled adapter continuations rather than
  private task hooks or wall-clock sleeps.
- Central verification at the integrated commit passed: Release Mac Catalyst
  x64 build with zero warnings and errors; Core tests with 378 passed and one
  known skip; Client tests with 230 passed.
- Fresh final Standards and Spec reviews both reported no findings. The
  canonical issue #78 contract remained byte-for-byte unchanged during
  implementation and review with SHA-256
  `a052bc697b1215239487010d87273e9efc5ea2b0435a0c2407bbe799f67616f5`.

### 2026-07-15 — issue #84

- Integrated the pre-game lobby-evaluation presentation and live Lobby Exit
  gate through commits `5a72bc6f` and `b7d7a311`.
- Role Selection renders one inline, resource-backed Portuguese evaluation
  panel after the role groups and before the fixed action bar. It subscribes
  to the live coordinator, rechecks the current role setup on every otherwise
  valid Start Game attempt, and calls the coordinator's atomic
  `TryRequestLobbyExit()` operation before the start callback can run. Retry is
  available only for the current Could Not Evaluate identity and delegates to
  `RetryCurrent()`.
- Probability presentation preserves every Possible Game Result row, detects
  exact zero and positive sub-one-percent numerators before independent whole
  percentage rounding, performs no compensating normalization, and collapses
  Victory Check Windows into non-cumulative `(Game Result, Ending Turn)` rows.
  Single-Faction, Shared Victory, No-Winner, Faction, and all valid
  Already-Decided reason names use production resources.
- The detail disclosure has a stable accessible name and relationship. Its
  controlled region remains mounted with native hidden semantics while
  collapsed, focus is retained across toggles, and the disclosure and Retry
  share 44-by-44 minimum pointer-target styling. The exact localized Faction
  separator remains protected by the test localization policy while being
  excluded only from noisy substring matching.
- Automated Browser QA passed at 360-by-800 and 900-by-900 with no horizontal
  overflow, retained visible focus, seven reachable Turn rows, and the final
  Turn/caveat clear of the fixed action bar. Root in-app-browser inspection at
  both frozen sizes confirmed the same layout: a 279-by-44 disclosure with a
  3-pixel outline and 3-pixel offset, and more than 97 pixels between the
  caveat and action bar after scrolling.
- Central verification at the integrated repair passed: Client tests with 263
  passed; Browser QA with 6 passed; Core tests with 449 passed and one known
  skip; generator and Release Mac Catalyst x64 builds with zero warnings and
  errors. Fresh final Standards and Spec reviews both reported no findings.
  The canonical issue #84 contract remained byte-for-byte unchanged with
  SHA-256
  `92c45132b647f66017cb03d005bea6af1125dd7cd4bf4233f1197b5d571e3569`.

### 2026-07-15 — issue #81

- Integrated explicit Build-Time Bundled Simulator Cache generation and its
  review repairs through commits `c3926c84`, `bf6a5a3e`, `cf07ce46`, and
  `f7cd749d`.
- The current-profile catalog contains exactly 1,664 canonical identities in
  ordinal order: 832 Already-Decided scenarios and 832 screening scenarios.
  The reviewed production artifact contains 832 already-decided, 52
  degenerate, and 780 probability records with zero omissions, structural
  suspicions, or incomplete Run Seed Material entries.
- The canonical checked-in and packaged artifact is 2,337,001 bytes with
  SHA-256
  `95797d40dfb3ac0b389c6f004956cdf19faeefb77c1f32f35d8071405d9a9253`.
  Evaluated project items contain one root `MauiAsset` with logical name
  `terminal-lobby-cache.json`, no `Content` item for that file, and the Release
  Mac app contains exactly one byte-identical root resource with no nested
  duplicate. Ordinary restore, build, test, and package operations left the
  source artifact hash and timestamp unchanged.
- The original SDK duplicate-item contradiction was repaired in the prepared
  contract by permitting exactly the file-specific
  `<Content Remove="Resources\Raw\terminal-lobby-cache.json" />` exclusion
  while retaining the existing `Resources/Raw/**` `MauiAsset` wildcard.
- Generation now publishes through durable same-directory staging and one
  cancellation-linearized commit decision. Success, cancellation, failure,
  serializer rejection, boundary failure, cleanup, symlink/case aliases, and
  macOS Unicode-normalization aliases have deterministic rollback evidence.
  Observed omissions, suspicions, and incomplete seeds survive terminal
  cancellation/failure diagnostics instead of being discarded.
- Diagnostics use typed internal status, phase, omission, and suspicion
  values, serialize every required stable ordinal code, and strict-round-trip
  only when catalog identities, phase/run bounds, per-kind artifact records,
  count equations, artifact document, hash, and byte length agree. Public
  `Generate`, `GenerateToFile`, and `GenerateToFiles` always bind the complete
  1,664-scenario catalog; bounded scenario selection and prebuilt publication
  remain Tests-only internal seams and cannot produce a public partial
  Completed artifact or report.
- Enumeration evidence uses an independently derived `4N - 6` per-player
  cardinality, exact 1,664/832 splits, structural and classifier gates,
  ordering/uniqueness, boundary membership, and representative exclusions. It
  does not mirror the production enumeration loop.
- Central verification at the final integrated repair passed: Core tests with
  451 passed and one known skip; Client tests with 263 passed; Browser QA with
  6 passed; generator and Release Mac Catalyst x64 builds with zero warnings
  and errors. Fresh final Standards and Spec reviews both reported no
  findings. The repaired canonical issue #81 contract remained byte-for-byte
  unchanged with SHA-256
  `3d6b4d67a33788e6f6cd5ae54e56d7f87aa60d51e7870da4c67257bf83f5c2b9`.

### 2026-07-15 — final PRD audit

- The branch-wide Standards audit found that ADR-0009 still described cache
  distribution as deferred even though this PRD measured and packaged the
  current-profile artifact. ADR-0012 now records the scoped decision: the
  1,664-record, 2,337,001-byte `core-simulator@1` cache ships as one app-bundled
  MAUI asset with no remote distribution surface. A future expanded or
  full-role profile may revisit distribution only after its own realistic
  size and operating constraints are measured.
- The same audit found repeated parsing of the immutable bundled artifact, a
  raw cache-record seam from Core GameLogic into Razor, an unannounced
  asynchronous evaluation transition, and simulator-unavailable copy that
  named the device instead of the selected setup. The final integration repair
  now loads, validates, and indexes the bundled document once per coordinator
  lifetime while continuing to reread mutable local storage. Request
  cancellation stops only the request's wait; disposal cancels the shared
  load, and stale requests still cannot publish.
- Terminal cache records remain private to the coordinator. The public
  `LobbyEvaluationState` projects only StateModels values and client-owned
  probability rows, including explicit collapse of Victory Check Windows into
  one ending-frequency row per Game Result and Turn. The panel exposes one
  concise visually hidden polite, atomic status announcement without making
  the probability table live. English and Portuguese unavailable copy now
  identifies the selected Role Composition.
- Final central verification after these repairs passed: Core tests with 451
  passed and one known skip; Client tests with 266 passed; Browser QA with 6
  passed; generator and Release Mac Catalyst x64 builds with zero warnings and
  errors. The packaged app still contains exactly one 2,337,001-byte root
  cache resource with SHA-256
  `95797d40dfb3ac0b389c6f004956cdf19faeefb77c1f32f35d8071405d9a9253`.
