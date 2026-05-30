# Issue 29 Merged Investigation Report

Date: 2026-05-25

Scope: merged review of `docs/artifacts/codex-issues.md` and `docs/artifacts/claude-issues.md` for PRD #29, focused on pending simulator work in #38, #39, #59, and #60.

This report preserves findings from both investigations, removes duplicate wording, and resolves disagreements against the live tracker and current codebase.

## Executive Summary

#38 remains the critical path. #39 is formally blocked by #38 and should not be implemented until #38 settles the random-run outcome contract. #59 is formally unblocked, but it has a strong design dependency on the seed and failure contracts that #38 introduces. #60 is now correctly blocked by both #38 and #59.

#29 itself is still useful as product context, but it is stale in three places: the completed performance spike is still described as future work, the PRD does not acknowledge the current two-`Team` runtime limit, and some stratified-sampling examples mention roles that are not supported yet.

The highest-leverage next step is to tighten the data contracts before implementation: completed vs incomplete runs, the meaning of "winning on Turn T", Faction enumeration rules, seed derivation under parallelism, and transcript replay semantics.

## Current Tracker Snapshot

- #29, `PRD: Win Probability Simulator`: open PRD, attached to the stale simulator milestone.
- #38, `Win Probability Simulator: overall pre-game probability in lobby`: open, `ready-for-agent`.
- #39, `Win Probability Simulator: per-Turn PMF and CDF in lobby`: open with stale PMF/CDF wording, formally blocked by #38, not `ready-for-agent`.
- #59, `Win Probability Simulator: record replayable headless run transcripts`: open, `needs-triage`, formally unblocked.
- #60, `Win Probability Simulator: validate sampled games with structural invariants`: open, formally blocked by #38 and #59.

Disagreement resolved: Claude's report said #60 was not formally blocked by #59. A live blocker query now returns both `38 OPEN` and `59 OPEN`, so Codex's dependency shape is current.

## Priority Decisions

### 1. Refresh PRD #29

Both reports agree that #29 still presents the performance spike as upcoming work. That is stale: #30 is closed and `docs/adr/0005-simulator-reuses-engine-via-headless-driver.md` records the accepted result, including 1,000 complete games in 2.5-3.0 seconds on a Samsung S7 against the 15-second budget.

Required edits:

- Rewrite the "First deliverable: performance spike" section as historical context or remove it.
- Cite ADR-0005 as the settled engine-reuse decision.
- Remove the line that detailed tests should wait until the spike validates feasibility.
- Keep #29 as product context, not as stale implementation guidance.

### 2. Clarify Faction vs Team for v1

The PRD and `CONTEXT.md` define Faction as the canonical domain term, but the runtime still exposes `Team` with only `Villagers` and `Werewolves`. This is workable for v1 only if the boundary is explicit.

Required edits:

- Add to #29 that v1 surfaces two Factions because the current `Team` enum has only `Villagers` and `Werewolves`.
- State that additional Factions should appear as `Team`/victory-condition support expands.
- In #38 and #39, permit internal aggregation by `Team` while presenting results as Game Result Frequency rows.
- Keep engine-wide `Team` to `Faction` migration out of scope for #38, #39, and #59.
- Avoid introducing a second independent Faction model inside the simulator.

Resolved by later Topic 7 design:

- All-zero rows are enumerated from the Simulation Scenario's Possible Game Result inventory, not from a global catalog.
- The row inventory includes one single-Faction Game Result for every Possible Faction, No-Winner, and scenario-specific possible Shared Victory Outcome combinations, even when those rows are unobserved.
- A Possible Faction can have a zero-frequency single-Faction row even when that Faction never came into being in the completed batch.

### 3. Resolve #39's Data Contract Before Starting

#39 depends on #38's output substrate. Starting #39 before #38 lands, or before #38's interface is settled, risks rework.

Required contract points:

- #38 should expose structured outcomes, not only rendered percentages.
- Each run should include total runs, completed runs, incomplete runs, winning Team/Faction, ending Turn, and failure reason when present.
- Define whether per-run detail is retained directly or projected into an aggregate DTO.
- Define the exact ending frequency denominator: completed runs.
- Preserve #39's current invariant using Game Result Frequency by Turn and Victory Check Window; the exact-ending cells sum to 100% of completed runs.
- Define how incomplete runs are displayed in the lobby.

Turn semantics:

- Lock the authoritative source for "winning on Turn T": `VictoryConditionMetLogEntry`, `HeadlessGameResult.TurnCount`, or final `IGameSession.TurnNumber`.
- Current engine behavior checks victory at main-phase transitions entering Day or Night. `HeadlessGameResult.TurnCount` reads `session.TurnNumber` at the end, so "Dawn of Turn 3" and "Day-finalize of Turn 3" can both report `3`.
- Later Topic 7 design settled that Turn alone is insufficient; timing evidence is keyed by ending Turn and Victory Check Window.

Display and statistical scope:

- Later Topic 7 design settled that evidence retains ending Turn and Victory Check Window, while Moderator-facing detailed output shows exact ending frequency by Turn only plus derived Ended-By-Turn Frequency. Do not use PMF/CDF acronyms in Moderator-facing language.
- Decide whether 1,000 runs is acceptable for tail cells, or whether #39 waits for #40's configurable run count. The PRD itself notes per-Turn probabilities need more runs for comparable precision.
- Define rounding rules for displayed percentages.
- Keep all user-facing UI text in Portuguese.

### 4. Make Completed vs Incomplete Runs Explicit

Claude's report adds an important implementation constraint: `HeadlessGameDriver` currently throws if the max instruction count is exceeded or if processing fails. #39 requires incomplete runs to be counted and surfaced, not silently dropped or propagated as batch failure.

Required decision:

- Either add a non-throwing result path to `HeadlessGameDriver`, or have the #38 harness catch and tally failures.
- Whichever layer owns this, reflect the contract in #38 because #39 and #59 both depend on it.
- Include failure reason categories that #59 can transcript later.

### 5. Tighten #59 Before Implementation

#59 is formally unblocked but under-specified. It can start before #38 only if narrowed to deterministic, strategy-agnostic transcript capture. Otherwise it should wait for #38's random strategy and seed contract.

Required decisions:

- Decide whether to add #38 as a formal blocker for #59 or narrow #59 so it can land now.
- If narrowed, seed should be recorded only when the active strategy provides one.
- Keep #59 out of Moderator-facing UI; it is QA/debugging infrastructure.
- Make transcript capture opt-in or sampled by default. Capturing full transcripts for every run in a 1,000-game production batch risks violating the PRD's performance budget.

Transcript contents:

- Starting config.
- Strategy identity and strategy configuration.
- Seed when present.
- Ordered Moderator Instruction and Moderator Response sequence.
- Phase and Turn context.
- Game History Log.
- Final outcome.
- Failure context when the run stops before victory.
- Last instruction and attempted response when present.

Failure boundaries:

- Capture safety-cap exits.
- Capture `ProcessInstruction` failures.
- Capture exceptions while creating a Moderator Response.
- Capture exceptions while processing a response.
- Decide whether cancellation emits a transcript or a separate cancellation result.
- Define how to represent an absent attempted response when strategy response creation fails.
- State that transcript generation must not change the simulated outcome.

### 6. Define Replay Semantics for #59

Both reports agree that "replayable" is ambiguous. The merged recommendation is to make re-execution the primary mechanism.

Preferred contract:

- Re-execute from starting configuration, strategy identity/configuration, and seed when present.
- A successful reproduction reaches an equivalent outcome.
- The instruction/response transcript acts as witness evidence and a diff target, not necessarily as the replay driver.

Avoid as the primary contract:

- Durable restoration of live execution state.
- Event-log rehydration as the replay mechanism.
- Feeding a captured instruction/response stream as the only replay path.

Reason:

- `Werewolves.Core/docs/architecture.md` says the event log can reconstruct game status but not the execution pointer.
- ADR-0002 intentionally keeps transient execution state out of serialization.

Open witnessing decision:

- Decide whether "equivalent outcome" means winning Team/Faction only, winning Team/Faction plus Turn, or bit-identical instruction/response sequence. These are different confidence levels and test costs.

### 7. Set the Seed Contract Across #38 and #59

Seeded random runs are required by #38 and become essential evidence for #59. Parallel execution makes this a contract, not an implementation detail.

Required decisions:

- Define master-seed to per-game-seed derivation, or require captured thread-local/per-run random seeds.
- Ensure seeded runs reproduce equivalent outcomes even when `DefaultDegreeOfParallelism` is greater than 1.
- Decide where seed information lives: exposed by `IModeratorDecisionStrategy`, carried by the harness beside each run, or included in a strategy descriptor.
- Reflect the same contract in #38 and #59.

### 8. Keep #60's Dependency Shape

#60 is now formally blocked by both #38 and #59, which matches the merged recommendation.

Guidance:

- Keep #60 blocked until #38 provides bounded seeded simulation batches and #59 provides reproducible failure evidence.
- Use #60 for structural invariant pass/fail evidence over sampled games.
- Do not duplicate #59's transcript responsibility inside #60.

### 9. Update or Add Agent Briefs

Codex identified process gaps that should be resolved before handing the issues back to agents.

Recommended brief updates:

- Add a structured agent brief for #39 after #38's result contract is known.
- Add or tighten a structured agent brief for #59 before implementation.
- Include key interface expectations from #38.
- Include claim-first QA expectations from `docs/agents/qa-strategy.md`.
- Prefer Core/service tests for Game Result Frequency by Turn, Ended-By-Turn Frequency, and transcript/replay behavior.
- Prefer bUnit or client component tests for lobby display behavior when applicable.
- For phone-screen manual criteria, name the evidence expected: browser QA host screenshot review and/or native manual observation.

## Lower-Priority PRD Gaps

### Stratified Sampling Examples

#29 cites Witch kill potion and Wolf-Father infection as stratified-sampling examples, but the supported role catalog currently includes only `SimpleWerewolf`, `Seer`, `WildChild`, and `SimpleVillager`.

Action:

- Either narrow the examples to currently supported mechanics, or explicitly mark #42/stratified sampling as waiting on broader role support.

### Mid-Game Hidden-Role Sampling

Pre-game simulation can delegate role assignment randomness to the engine. Live mid-game projection (#43) needs a design for snapshotting a partially played `GameSession` and sampling remaining hidden role assignments.

Action:

- Expand #43 or create a separate issue for live-state hidden-role sampling.

## Suggested Order of Operations

1. Resolve the #39 contract questions that affect #38: Faction enumeration, completed vs incomplete run accounting, and Turn semantics.
2. Implement #38 or at least settle its DTO/service interface.
3. Refresh #29 for spike completion, ADR-0005, and current two-Faction runtime reality.
4. Decide whether #59 formally waits for #38 or is narrowed to deterministic transcript capture.
5. Resolve seed derivation under parallelism across #38 and #59.
6. Add/tighten agent briefs for #39 and #59.
7. Implement #59.
8. Keep #60 blocked until #38 and #59 land, then implement invariant checks.
9. Implement #39 once #38's structured results are available.

## Evidence Checked

- Live `gh issue view` for #29, #38, #39, #59, and #60.
- Live blocker queries: #39 is blocked by #38; #59 has no blockers; #60 is blocked by #38 and #59.
- `CONTEXT.md`: Faction definition and deprecated Team terminology.
- `docs/adr/0005-simulator-reuses-engine-via-headless-driver.md`: spike accepted at 2.5-3.0 seconds for 1,000 games on Samsung S7.
- `Werewolves.Core/Werewolves.Core.StateModels/Enums/Team.cs`: only `Villagers` and `Werewolves` exist today.
- `Werewolves.Core/Werewolves.Core.GameLogic/Roles/SupportedRoleCatalog.cs`: supported roles are `SimpleWerewolf`, `Seer`, `WildChild`, and `SimpleVillager`.
- `Werewolves.Core/Werewolves.Core.GameLogic/Services/HeadlessGameDriver.cs`: throws on instruction-cap overflow and processing failure.
- `Werewolves.Core/Werewolves.Core.GameLogic/Models/HeadlessGameResult.cs`: currently returns `IsFinished`, `TurnCount`, `ProcessedInstructionCount`, and `VictoryDescription`.
- `Werewolves.Core/Werewolves.Core.GameLogic/Interfaces/IModeratorDecisionStrategy.cs`: strategy interface exposes only `CreateResponse`.
- `Werewolves.Core/Werewolves.Core.GameLogic/Services/GameBenchmarkHarness.cs`: default 1,000 games and degree of parallelism 2.
- `Werewolves.Core/Werewolves.Core.GameLogic/Services/GameFlowManager.cs`: victory checks happen at phase transitions entering Day or Night and return `Team`.
- `Werewolves.Core/docs/architecture.md`: event log restores status, not the transient execution pointer.
