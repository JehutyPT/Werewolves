# Issue 29 Simulator Handoff

Date: 2026-05-28

This handoff captures the design grilling session about stale PRD #29, especially the shift from on-device-first pre-game simulation to cache-first pre-game Role Composition lookup. It does not replace the settled glossary in `CONTEXT.md`; use that file as the source of truth for domain terms.

This is a temporary coordination document for the staged grilling effort. Keep it only while later topic branches and tracker migration still depend on the accumulated decisions. As decisions land in durable homes, reduce this file to the remaining migration checklist. Delete it after every grilling topic is complete and all decisions have been propagated to the glossary/domain docs, existing ADRs, PRD #29, and canonical issue-body Implementation Contracts. Do not archive or preserve a second copy merely for history; Git already retains that trail.

## Document Routing

Use these homes for the simulator cleanup so the design trail stays parseable:

| Material | Destination |
| --- | --- |
| Active simulator grilling log, unresolved decisions, PRD rewrite notes, and stale-issue rewrite notes | `docs/handoff.md` |
| Canonical vocabulary and avoided synonyms | `CONTEXT.md` |
| Stable domain facts that tests and implementation can assert | `docs/domain/invariants.md` |
| Physical game-rule disambiguations and role interaction rulings | `docs/domain/game-rules-clarifications.md` |
| Durable architectural decisions with alternatives and tradeoffs | `docs/adr/` |
| Product behavior, UI copy/display choices, and acceptance criteria | PRD #29 and replacement/updated GitHub issues |

## Deferred Decisions

Cache distribution remains deliberately unresolved. S6 packages the current-profile Bundled Simulator Cache and records its actual packaged size, hash, entry counts, and omission counts in generation diagnostics; artifact size is evidence, not a CI threshold. A bundled simulator cache is acceptable only if the eventual full-role artifact is negligible in app-package terms; the current rough comfort range is under 5-10 MB, with the upper end already feeling expensive, but that range is only a planning signal. Measure the realistic compressed full-role artifact, including index and metadata overhead, before choosing bundled, static remote, or hybrid distribution in a dedicated ADR. S0-S8 do not add remote distribution infrastructure or abstractions.

On-device fallback remains mandatory regardless of the cache distribution shape. The final bundled-versus-remote decision should wait until the simulator, implemented role catalog, cache schema, and cache generation are far enough along to measure realistic artifact size.

The exact S8 interaction used to open detailed probability output remains deliberately unresolved. Inline disclosure, modal presentation, and navigation are UI-refinement options, not Issue / ADR Reshaping decisions. Resolve that choice while preparing S8, using a small prototype/spike only if ordinary refinement cannot settle it.

## Starting Problem

PRD #29 had grown stale around the Win Probability Simulator. The current investigation in `docs/artifacts/issue-29-merged-investigation.md` recommended tightening several contracts before implementation, but the main design question reopened here was whether pre-game results should be computed on device with repeat caching, or whether the app should be cache-first for normal pre-game UX.

The user argued that precomputing valid Role Compositions may be tractable because real games have strong validity and playability constraints, especially once player count is capped. The goal became: define the cache-first product contract defensibly without inventing fragile balance heuristics.

## Staged Grilling Progress

The design follow-up is being handled as a staged grilling sequence. Each branch should be treated as settled input for later branches once the user has reviewed and committed that branch's diff.

1. **Faction Model** was completed by the first sub-agent and committed as `c103d1f Document faction model terminology`.

2. **Win Condition Semantics** was completed by the second sub-agent. The user provided the summary below and asked to proceed to Role Composition Space.

3. **Role Composition Space** was completed inline using `$grill-with-docs-batched` after the user clarified that this branch should not be offloaded to another sub-agent.

4. **Already-Decided / Degenerate Classification** was completed inline using `$grill-with-docs-batched`.

5. **Simulation Result Contract** was completed inline using `$grill-with-docs-batched`.

6. **Cache Artifact Design** was completed inline using `$grill-with-docs-batched`.

7. **Probability Output** was completed inline using `$grill-with-docs-batched`.

8. **Fallback Runtime** was completed inline using `$grill-with-docs-batched`.

9. **Testing And QA** was completed inline using `$grill-with-docs-batched`. Batch 1 settled the deterministic QA foundation, test surface split, golden fixture meaning, and done-evidence gate. Batch 2 settled replay-test boundaries, generation diagnostics, and fallback runtime test coverage. Batch 3 settled the initial golden fixture inventory, diagnostic report storage boundary, and docs-versus-implementation-issues boundary. Batch 4 settled canonical identity assertion style, already-decided/degenerate test strategy, and probability aggregation test strategy. Batch 5 settled the source-test exclusion, completion boundary for issue-writing, and no-ADR decision.

10. **Issue / ADR Reshaping** was completed inline using `$grill-with-docs-batched`. Batch 1 settled ADR routing, the PRD #29 product boundary, and renaming the PRD to match the new product language. Batch 2 settled the disposition of stale implementation issues and the dependency role of bug #46. Batch 3 settled the replacement implementation spine, current-profile delivery boundary, ownership splits, and parallel execution paths. Batch 4 settled issue categories, initial labels, authoritative contract routing, and the fixed-seed QA boundary. Batch 5 settled issue-by-issue Implementation Contract ownership for S0-S8 and deliberately deferred S8's detailed-view interaction pattern to issue preparation. Batch 6 settled the PRD title/body structure, milestone name, and parent topology. Batch 7 settled durable-document routing and the temporary handoff/loose-end lifecycle.

11. **Future-Scope Boundaries** was completed inline using `$grill-with-docs-batched`. Batch 1 settled the full-role and New Moon extension boundary and the deletion of stale future issues #42 and #43. Batch 2 settled cache-distribution evidence, Reference Turn Horizon and advanced probability UX exclusion, and the diagnostics/audit boundary. Batch 3 froze profile membership, prohibited approximation of unsupported rules, and limited extension architecture to contracts already required by v1. The user confirmed the shared understanding.

12. **Tracker Migration And Finalization** was completed inline using `$grill-with-docs-batched`. Batch 1 settled the three-stage documentation/tracker/cleanup boundary, milestone membership, direct one-off administrative operations, and #30 readiness cleanup. Batch 2 finalized the S0-S8 titles, categories, blocker graph, and initial S0-only execution frontier. Batch 3 settled cache lookup precedence, PRD completion, and temporary follow-up cleanup. The PRD and canonical work-item contract payloads and exact execution checklist were then shaped below. The user confirmed the shared understanding.

## Resolved Decisions

1. Cache-first applies only to normal pre-game UX. On-device simulation still exists as fallback, implementation substrate, and QA evidence.

2. The app-wide Supported Player Count is 5-30 Players. Product docs may still describe 8-20 as the practical ergonomic sweet spot for physical play.

3. Use **Role Composition** as the canonical term for the pre-game multiset of Role cards, independent of Player assignment or Seating Order.

4. The app should not try to decide whether a Role Composition is generally balanced. It surfaces Game Result Frequency; the Moderator judges balance.

5. The app blocks only two categories: **Already-Decided Role Composition** and **Degenerate Simulation Scenario**.

6. **Already-Decided Role Composition** means a Faction would already win at Lobby Exit from Role Composition evidence alone, before random Player assignment, setup artifacts, simulation, or Turn 1 choices.

7. Already-decided detection is rule-based and does not run simulation. The classifier runs every Faction victory trigger that can be evaluated from the Role Composition alone.

8. Already-decided lookup may use the same Canonical Simulation Scenario identity as other lobby results. The already-decided classification still relies only on the Canonical Role Composition, and the app should consume the cached lobby evaluation without second-guessing that evidence boundary.

9. If multiple Faction victory triggers are true at Lobby Exit, already-decided records use Shared Victory Outcome semantics rather than priority ordering.

10. Already-decided classification does not derive or simulate a Simulation Start State.

11. **Degenerate Simulation Scenario** means a legal, supported Simulation Scenario whose 1,000-run baseline screening simulation completes every run and only observes Game Sessions ending by the end of Turn 1.

12. Degenerate classification is scenario-level evidence. Each screening run derives its own seeded pre-game Simulation Start State from the Simulation Scenario, including random assignment and profile/default setup choices.

13. A Turn 1 ending is a completed game outcome, not a simulation failure. The Turn 1 cutoff includes both Turn 1 Victory Check Windows: Dawn after Night 1 resolution and the pre-Night check after Day 1 vote resolution and cascades.

14. An incomplete screening run, regardless of cause, is an error state that invalidates the whole screening batch. It is not evidence for degenerate classification and must not block Lobby Exit.

15. Degenerate classification is practical product screening, not mathematical proof over every possible branch.

16. Do not use percentage thresholds such as 50%, 80%, or 90% for degenerate blocking. The screen is defensive: all 1,000 screening runs completed and ended by Turn 1.

17. Use a layered simulation pipeline:
    - 1,000-run `baseline-random-screening` batch for validity screening.
    - 10,000-run `baseline-random-probability` batch only for Simulation Scenarios that pass screening.

18. `baseline-random-screening` and `baseline-random-probability` use the same decision behavior. They differ by profile name, run count, and result interpretation.

19. Bundled Simulator Cache entries should be terminal lobby evaluations only:
    - already-decided: no simulation; records the winning Faction and reason.
    - degenerate: screening completed all 1,000 runs and observed only Turn 1 endings; records evidence summary.
    - probability: passed screening; records probability summaries.

20. Degenerate cache entries store screening conclusion evidence rather than probability output: canonical scenario, simulator/profile identity, Turn 1 cutoff definition, and aggregate counts by Game Session Outcome and ending window. The app-facing cache does not need requested/completed screening run counts or per-run seed material.

21. Do not store probability records for already-decided Role Compositions or degenerate Simulation Scenarios. Store classification records instead.

22. Moderator-facing behavior:
    - Already-decided blocks Lobby Exit and explains that the selected roles already produce a win before the first night.
    - Degenerate blocks Lobby Exit and explains that every baseline screening game ended during Turn 1, making the composition likely unplayable.
    - Missing or stale terminal lobby evaluation blocks Lobby Exit while evaluation is pending. Incomplete screening, build-time cache-generation errors, and incomplete on-device fallback generation become visible "could not evaluate" states; they are not degenerate and release the Lobby Exit safety gate so the Moderator can decide whether to proceed.

22a. Build-Time Cache Generation means producing Bundled Simulator Cache artifacts outside the Moderator's phone, such as on a development machine, CI worker, or backend job. It must not mean trying to enumerate every cacheable scenario on the Moderator's phone.

22b. On-device fallback generation is allowed only after neither the Bundled Simulator Cache nor local fallback records have a usable terminal lobby evaluation for the selected Simulation Scenario. It may produce a usable local already-decided, degenerate, or probability evaluation only if the same classification pipeline completes successfully.

22c. Failed, incomplete, or operationally suspect generation attempts are omitted from the Bundled Simulator Cache. Any logs for those attempts are implementation/build concerns, not part of the domain cache contract.

22c.1. On-device fallback generation starts automatically once the selected Simulation Scenario is stable and has no usable bundled or local terminal lobby evaluation. It is skipped when the selected setup is rules-invalid, app-unsupported, simulator-unsupported, already has a usable terminal lobby evaluation, or changes before the fallback attempt finishes. If the Moderator attempts Lobby Exit while evaluation is pending, the app waits for a terminal evaluation or failure.

22c.2. Successful on-device fallback may produce local already-decided, degenerate, or probability terminal lobby evaluations with the same product meaning as equivalent bundled terminal lobby evaluations. The materialized local result is a compact Local Fallback Cache Record, not full per-run Simulation Result Evidence. Local fallback records are persisted and reused across app restarts while their Canonical Simulation Scenario plus simulator profile/version identity and invalidation semantics remain current.

22c.3. The only product hard boundaries for on-device fallback generation are any generation failure and a 10-second timeout. Incomplete fallback, timeout, instruction-limit exhaustion, runtime cancellation, start-state generation failure, incomplete screening runs, incomplete probability runs, and generation errors all collapse to visible "could not evaluate." They must not produce partial probability output or already-decided or degenerate claims. Once visible, "could not evaluate" releases the Lobby Exit safety gate and leaves the decision to proceed with the Moderator. Setup changes discard the stale attempt and start evaluation for the new stable Simulation Scenario instead of releasing the safety gate for the old one.

22c.4. Failed fallback state is remembered only for the current unchanged setup in the current app session. It is not persisted across app restarts, and the app must not immediately retry in a loop after failure. If the setup changes, the failed state is discarded and the new stable Simulation Scenario is evaluated normally.

22c.5. After fallback failure, the Moderator may explicitly retry evaluation. Retrying runs the same 10-second bounded fallback evaluation and closes the Lobby Exit safety gate while evaluation is in progress. This retry action is available only after failure and is not a manual skip or dismiss action for an in-progress evaluation.

22c.6. Moderator-facing lobby evaluation status does not distinguish between bundled cache lookup, local fallback cache lookup, and fallback generation. The lobby can show a compact spinner such as "Simulating match..." while evaluation is unresolved; cache hits simply finish that flow faster. The Moderator does not get a manual skip or dismiss action for an in-progress fallback evaluation; proceeding without a terminal evaluation is available only after fallback fails or times out.

22c.7. App-supported but simulator-unsupported setups do not attempt on-device fallback and do not block Lobby Exit solely because evaluation is unavailable. The app should make evaluation unavailability visible without converting simulator support into the app support boundary.

22d. Probability cache entries store Game Result Frequency and Game Result Frequency by Turn. The app-facing cache does not need requested, completed, or incomplete run counts, and it does not ship per-run replay evidence.

22e. Game Result means one mutually exclusive final result: a single-Faction win, a specific Shared Victory Outcome, or No-Winner Outcome. Shared victories are represented as their own Game Result instead of crediting each winning Faction separately.

22e.1. Probability output uses Game Result Frequency only. It does not include Faction Win Probability, credited Faction views, or Exclusive Outcome Share language.

22e.2. Probability output rows come from the Simulation Scenario's Possible Game Result inventory: one single-Faction row for every Possible Faction, No-Winner, and every scenario-specific possible Shared Victory Outcome combination. These rows appear even at 0%, including Factions that never came into being in the completed batch and shared-victory combinations that were not observed. Rows are not generated from a global Faction or outcome catalog.

22f. Game Result Frequency by Turn records how often each Game Result happened on each ending Turn and Victory Check Window out of all completed runs. Game Result Frequency is derived by summing Game Result Frequency by Turn across Turns and Victory Check Windows, and the full distribution sums to 100%. Moderator-facing detailed timing output shows ending frequency by Turn only, collapsing across Victory Check Windows; Victory Check Window stays in evidence/cache semantics.

22f.1. Ended-By-Turn Frequency is a derived view from Game Result Frequency by Turn showing how often completed runs have ended by each Turn, optionally filtered by Game Result. Do not use PMF/CDF acronyms in Moderator-facing language.

22f.2. Probability output does not include additional duration metrics such as expected Turn, average duration, median Turn, real-time estimate, instruction count, or Reference Turn Horizon comparison.

22f.3. Moderator-facing probability output uses whole percentages with no decimal places. Do not mathematically round every nonzero result up to 1%. Rounded and grouped Moderator-facing percentages do not need to visually total to 100%, and the UI should not add reconciliation text just to explain rounding differences. Cache/internal frequency evidence is rounded to the nearest one or two decimal places where needed for display rounding and zero versus nonzero distinctions; no probability evidence needs more precision than that for this product.

22f.4. A detailed view may briefly state that probability output is a baseline simulation estimate from a finite batch, but this caveat should not be front and center in the main lobby summary. Moderator-facing output should avoid confidence intervals, margins of error, standard error, and statistical terminology.

22f.5. Zero-frequency Possible Game Results mean "not observed in this simulation batch," not "impossible." Possible Game Results below 1% exact frequency, including zero-frequency rows, are Unlikely Possible Results. They may be grouped by individual outcome name as possible-but-unlikely outcomes instead of shown as primary percentage rows. Grouping is presentation only; the underlying Game Result Frequency and Game Result Frequency by Turn remain complete.

22f.6. Probability output avoids labels such as balanced, fair, good, bad, recommended, or warning. The Moderator interprets Game Result Frequency for their table.

22f.7. Compact lobby probability output shows primary Game Result Frequency rows as whole percentages plus a possible-but-unlikely outcomes list when relevant. It does not show run counts, cache provenance, simulator profile/version, finite-batch caveats, or timing detail.

22f.8. Detailed Moderator-facing probability output may show primary and possible-but-unlikely outcomes, ending frequency by Turn, Ended-By-Turn Frequency, simple "<1%" versus "0%" distinction for unlikely outcomes, and one brief baseline simulation estimate note. It does not show Victory Check Window, cache provenance, simulator profile/version, confidence intervals, statistical terminology, or replay/audit evidence.

22f.9. Simulator profile/version, cache provenance, canonical identities, Possible Faction inventory, Possible Game Result inventory, rounded one-or-two-decimal internal frequencies, timing aggregates by Victory Check Window, invalidation identity, Run Seed Material, per-run source records, and replay/audit details are internal/cache or QA evidence rather than Moderator-facing probability output.

22f.10. Topic 7 documentation stops at domain and product-output semantics. `CONTEXT.md` defines the canonical terms and constraints, `docs/handoff.md` records the decision chain, and `docs/domain/game-rules.md` remains unchanged because probability output is not a physical game rule. Do not create a separate `docs/loose-ends.md` section for Topic 7; the implementation work belongs in the PRD and updated/new GitHub issues.

22g. Cache lookup identity is the Canonical Simulation Scenario plus simulator profile/version. Batch sizes are generation policy rather than cache identity or Moderator-facing cache evidence.

22h. Run Seed Material stays in Simulation Result Evidence and replay/audit workflows. It is not part of the Bundled Simulator Cache.

22i. Bundled Simulator Cache entries and Local Fallback Cache Records are invalidated by changes to rule interpretation, Role behavior, simulator profile behavior, supported scenario scope, Canonical Simulation Scenario construction, already-decided or degenerate classification semantics, Game Result Frequency semantics, or Turn cutoff semantics. Localization, visual presentation, and explanatory copy changes do not invalidate cache entries.

22j. On-device fallback generation only applies when the selected Simulation Scenario is simulator-supported by the current profile. A cache miss is not itself a terminal product state: a missing or stale terminal lobby evaluation keeps the Lobby Exit safety gate closed while evaluation is pending. Failed fallback generation becomes a visible "could not evaluate" state, releases the gate, and must not make balance, already-decided, or degenerate claims.

22k. Simulator profile/version identifies already-decided cache compatibility; it is not evidence that simulation ran. Already-decided evidence remains Role Composition classification only.

22l. Probability cache entries are coherent only when Game Result Frequency and Game Result Frequency by Turn describe the same Game Result set. Game Result Frequency is the row-sum projection of Game Result Frequency by Turn across Turns and Victory Check Windows.

22m. Game Result evidence in a degenerate cache entry explains the Turn 1 endings; it is not presented as balance probability.

22n. Domain docs define Bundled Simulator Cache entries semantically. Serialized schema, file format, compression, and lookup/index layout are implementation concerns.

23. Already-decided classification only uses Faction victory evidence available at Lobby Exit. Degenerate and probability evidence can observe Starting, Possible, Transient, and Latent Factions through completed simulation outcomes.

24. The full Faction model remains the domain contract, but the active Simulator Profile Role Set controls which Simulation Scenarios can be evaluated today. If the current runtime cannot evaluate a full Faction trigger or scenario, it is not simulator-supported/cacheable and does not attempt fallback, or it becomes a visible "could not evaluate" state after a failed attempt; it must not be mislabeled as already-decided or degenerate.

25. Simulation result contracts separate stable Simulation Result Evidence from implementation diagnostics. Simulation Result Evidence must contain the source data needed to derive probability views, but raw simulator run output does not need to materialize those views directly. Final Player/Faction state snapshots, transcripts, instruction counts, exception details, timing, memory, raw engine traces, and driver limits are diagnostics, not stable result evidence.

26. A simulation run that reaches a Game Session Outcome is a Completed Simulation Run even when it ends during Turn 1. An Incomplete Simulation Run is a simulator/generation/driver failure to reach a Game Session Outcome; it is not a No-Winner Outcome and does not contribute to Game Result Frequency.

27. Stable Simulation Result Evidence for a completed run includes only the Game Session Outcome, ending Turn, ending Victory Check Window, Simulation Scenario/profile identity, and Run Seed Material. Deterministic replay identity is the stable audit mechanism; diagnostics can be optional.

28. Game Result Frequency is derived by aggregating Completed Simulation Runs by Game Result: one single-Faction winner, a specific Shared Victory Outcome, or No-Winner Outcome.

29. A simulation batch's stable source data includes one minimal source record per attempted run: run identity, Run Seed Material, completion state, and for Completed Simulation Runs the Game Session Outcome, ending Turn, and ending Victory Check Window. Topic 6 resolved that the app-facing Bundled Simulator Cache does not store per-run source records.

30. Batch-level Simulation Result Evidence includes the Simulation Scenario's Possible Faction and Possible Game Result inventories so unobserved or never-came-into-being rows can still be shown at zero frequency where useful to the Moderator.

31. Stable evidence for Incomplete Simulation Runs includes replay identity and completion state; specific failure details remain diagnostic.

32. Screening and probability batches share the same per-run Simulation Result Evidence contract; they differ by batch purpose, requested run count, and interpretation.

33. Batch-level Simulation Result Evidence must support screening and probability interpretation from source data: requested runs, Completed and Incomplete Simulation Run counts, Possible Faction inventory, Possible Game Result inventory, completed outcomes by ending Victory Check Window, completed outcomes by Game Result, and ending Turn.

34. Shared Victory Outcome, No-Winner Outcome, and zero-frequency Game Results are represented through the same source evidence rather than special-case summary records.

35. Simulation result contract design stops at what evidence a run or batch must provide. Topic 6 resolved that the app-facing Bundled Simulator Cache stores compressed lobby evaluations derived from simulation evidence, not per-run source records.

36. Already-decided records stay outside the simulation run/batch result contract. They can share Game Session Outcome language, including Shared Victory Outcome, but they are not simulation runs and do not have Run Seed Material, per-run source records, or Completed/Incomplete Simulation Run counts.

37. "Could not evaluate" is a product evaluation state, not a Game Session Outcome. It must not be grouped into Game Result, No-Winner Outcome, Game Result Frequency, or degenerate evidence.

38. **Balanced Role Composition** remains a descriptive domain concept interpreted from Game Result Frequency under the baseline decision model, but the app does not block on that.

39. **Initial Faction Count** is the denominator concept for pre-game balance discussions: count starting win-condition beneficiaries, not every conditional outcome the simulator may later produce.

40. Initial Faction Count excludes latent or transient Factions such as Cross-Faction Lovers and Angel's early solo win condition.

41. **Reference Turn Horizon** was retained only as a dormant descriptive metric: `Player count / Initial Faction Count`. It is not part of degenerate blocking and should not be added to lobby UI yet.

42. Angel is special because it has a transient solo win condition and then falls back into the Villager Faction. Do not let Angel inflate Initial Faction Count.

43. Cross-Faction Lovers are conditional/latent. Track Lovers outcomes if they occur, but do not include them in Initial Faction Count.

44. Prejudiced Manipulator has setup-dependent balance. The baseline simulator profile defaults to an even public group split; only non-default group models need explicit Simulation Scenario material.

45. Run reproducibility uses **Run Seed Material**: a canonical string stored for replay evidence, hashed only at the random-generator boundary.

46. Run Seed Material includes simulator version, profile/strategy, run number, Player count, canonical Role Composition, and any non-default Simulation Scenario assumptions. Example:

```text
sim-v1|baseline-random-screening|players=10|run=17|Seer-1,SimpleVillager-7,SimpleWerewolf-2,WildChild-1
```

47. Run numbers are 1-based within a batch. A single-run simulation uses `run=1`; a 1,000-run batch uses `run=1` through `run=1000`.

48. Canonical Role Composition segments include only non-zero Role counts, sorted alphabetically by exact enum name, not localized display name or UI insertion order.

49. Cache lookup identity uses the Canonical Simulation Scenario and simulator profile/version. Batch sizes are generation policy, not cache identity or Moderator-facing cache evidence.

50. Simulator/cache CI tests must not depend on live randomness, probabilistic thresholds, wall-clock performance, artifact size comfort ranges, timing, memory use, or implementation diagnostics. Tests may cover probabilistic behavior only through fixed Run Seed Material, checked-in fixtures, deterministic synthetic evidence, or pure aggregation checks.

51. Simulator/cache testing uses this evidence split:
    - deterministic unit tests for pure value contracts such as canonical identities, Run Seed Material, invalidation comparison, Completed versus Incomplete Simulation Run classification, Possible Game Result inventory, probability aggregation, and rounded display projections;
    - Core integration tests through public APIs for layered lobby gates, already-decided classification, degenerate screening interpretation, probability batch interpretation, narrow replay coverage from Run Seed Material, and simulator support boundaries;
    - cache artifact tests for semantic round trips, stale-version rejection, bundled/local terminal evaluation equivalence, and the absence of per-run simulation evidence in app-facing cache records;
    - Client service and adapter tests with fakes for fallback orchestration, local cache persistence, timeout, failure, retry, setup-change cancellation, and simulator-unsupported unavailable state;
    - generation diagnostics only for work that produces or validates cache artifacts.

52. Golden fixtures are small checked-in input/expected-output artifacts that protect stable contracts. They are appropriate for canonical strings, minimal simulation evidence records, Possible Game Result inventories, and terminal cache records. They are not snapshots of full transcripts, raw engine traces, exception details, timing, memory, UI screenshots, or random win-rate expectations.

53. Updating a golden fixture is a contract change. The implementation issue or review should name the claim being protected and why the new expected output is correct.

54. Simulator/cache implementation issues can claim done only after recording the Agent QA Gate fields from `docs/agents/qa-strategy.md`: claim, preferred evidence, forbidden evidence, and source-test allowlist status. They need deterministic CI evidence for the behavior being claimed, plus generation diagnostic artifacts when the issue generates or validates cache artifacts.

55. Cache generation diagnostics are required when an issue produces or validates cache artifacts. Diagnostics should be machine-readable and include generator/simulator/profile identity, scenario counts by terminal result type, omitted scenario counts grouped by reason, incomplete run counts with Run Seed Material references, artifact identity/version/hash/size, and representative replay seeds for failures or suspicious outcomes. Timing, memory, and instruction counts may be diagnostic fields, but they are not pass/fail gates unless the issue explicitly claims performance or artifact-size behavior.

56. Fallback runtime behavior requires deterministic service and adapter tests for bundled cache hits, local fallback cache hits, stale or missing evaluation starting fallback, successful fallback persistence of a compact Local Fallback Cache Record, failure/timeout/incomplete fallback collapsing to "could not evaluate", setup changes discarding stale in-flight work, retry only after failure, simulator-unsupported unavailable state with no fallback attempt, Lobby Exit gate behavior while pending versus after failure, and no in-progress skip or dismiss action.

57. Replay tests are not primary simulator correctness tests. Do not create replay tests by running arbitrary stochastic scenarios, copying observed outcomes, and treating those outcomes as oracles.

58. Replay tests are appropriate only for determinism plumbing, known-oracle scenarios, and regression seeds from diagnosed bugs. Determinism plumbing asserts that the same Run Seed Material under the same simulator/profile version reproduces the same stable source record. Known-oracle replay tests assert only independently derivable completion state, Game Session Outcome, ending Turn, and Victory Check Window. Regression-seed tests assert the smallest fixed property that prevents the bug from returning.

59. The first simulator/cache implementation slice should keep golden fixtures small: canonical identity examples, minimal Simulation Result Evidence examples, probability aggregation input/output, and terminal cache records for already-decided, degenerate, and probability entries. Full-run replay fixtures should be limited to known-oracle scenarios or regression seeds.

60. Generated diagnostics reports should not normally be checked into the repo. Commit diagnostic schemas/contracts and small golden fixtures. Keep actual generation reports as CI/build artifacts or issue evidence unless a generated cache artifact is intentionally part of the app/package source.

61. Topic 9 documentation should stop at stable QA policy and simulator/cache evidence contracts. Concrete test class names, fixture file paths, diagnostic schema field names, and implementation sequencing belong in GitHub issues unless a contract must be shared across issues.

62. Already-decided classification should be covered by deterministic classifier tests with rule-derived scenarios. Degenerate classification should mostly use synthetic batch evidence: all 1,000 completed runs ending by Turn 1 means degenerate; one Turn 2 completed run means not degenerate; one Incomplete Simulation Run means could not evaluate. Do not try to maintain tests for every possible degenerate setup. A small number of known-oracle simulator-path integration tests is enough, including one prevalidated degenerate setup.

63. Probability aggregation should be tested with handcrafted Simulation Result Evidence rather than live 10,000-run batches. Tests should cover Completed-only denominators, Incomplete Simulation Run exclusion, zero-frequency Possible Game Results, Shared Victory Outcomes and No-Winner Outcomes as Game Results, Game Result Frequency by Turn summing to Game Result Frequency, Ended-By-Turn Frequency derivation, and display rounding/grouping behavior.

64. Canonical identity tests should prefer structural assertions and round-trip or parse checks over broad exact string assertions. Tests should assert role counts by enum value, sort order by enum identifiers, zero-count omission, Thief extras, Actor Setup Card exclusion, `players=N`, and non-default assumptions as structured fields. Use shared constants or value objects for separators, labels, profile ids, and version ids where available. Exact literal string assertions are allowed only at the narrow serializer/cache-key boundary when the literal format itself is intentionally the public or cache contract.

65. Do not add simulator/cache source-level tests for this work. Prove simulator/cache claims through behavior, semantic artifact checks, parser or serializer round trips, Core integration tests, service/adapter tests, and generation diagnostic artifacts.

66. Topic 9 is complete enough for PRD and implementation issue-writing. Remaining details such as fixture paths, test class names, diagnostic schema field names, exact schema shape, and issue sequencing belong in GitHub issues unless a contract must be shared across issues.

67. Topic 9 does not create a new ADR. The settled QA contract extends `docs/agents/qa-strategy.md` plus the existing ADR-0008 and ADR-0009 evidence boundaries rather than introducing a new hard-to-reverse architecture choice.

68. Topic 10 does not update ADR-0005 and does not create a new ADR. ADR-0005 remains the decision to reuse the game engine through a headless driver. ADR-0007, ADR-0008, and ADR-0009 already cover the Simulation Scenario boundary, layered lobby evaluation pipeline, and cache-artifact evidence boundary. Cache distribution remains the only unresolved decision that may warrant a future ADR after realistic artifact-size evidence exists.

69. PRD #29 remains the product umbrella, but its scope should be rewritten around cache-first pre-game lobby evaluation. The settled product scope includes bundled and local terminal lobby evaluation lookup, bounded on-device fallback generation, scenario-based screening, already-decided and degenerate blocking evaluations, Game Result Frequency probability output, unavailable/pending/could-not-evaluate states, and the current simulator/runtime limits. Live mid-game recomputation, Moderator-configurable run counts, PMF/CDF framing, and stratified sampling are not part of this implementation scope.

70. Rename PRD #29 as well as rewriting its body. The new title should describe the cache-first pre-game lobby evaluation product rather than preserving "Win Probability Simulator" as the primary product name. Use Lobby Evaluation and Game Result Frequency terminology in the title and body; retain the old name only where historical tracker context requires it.

71. Close #38, #39, #40, and #41 with issue-specific supersession comments instead of rewriting them in place. Their stale titles, bodies, Agent Briefs, completed checkboxes, and blocker relationships encode the former on-device-first issue split. Replace #38, #39, and #41 with newly scoped implementation slices after the replacement issue spine is settled. Do not replace #40 in the current scope because Moderator-configurable run counts conflict with the fixed screening and probability generation profiles.

72. Close #59 and #60 with issue-specific supersession comments. Do not preserve replay transcripts or sampled structural QA as detached implementation phases. The replacement simulator-evidence slice should own the narrow Run Seed Material and replay contract; every implementation issue should own claim-first deterministic QA for its behavior, and cache-producing or cache-validating issues should own their generation diagnostics.

73. Keep bug #46 open. It blocks simulator execution and cache generation claims for Simulation Scenarios containing Wild Child, but it does not block canonical identity, terminal lobby evaluation contracts, cache schema, or Client orchestration tested with fakes.

74. The first implementation delivery claims evaluation support only for the current Simulator Profile Role Set. App-supported but simulator-unsupported setups remain visibly evaluation-unavailable, do not attempt fallback, and do not block Lobby Exit solely because evaluation is unavailable. Expanding simulator role support is separate work.

75. Keep Role Composition validity and supported-player work separate from Canonical Simulation Scenario identity and support classification. Physical setup validation owns the 5-30 Player boundary, hard-aligned Faction coverage, and Actor Setup Card accounting. Canonical identity owns deterministic Role Composition and Simulation Scenario construction plus simulator profile/version compatibility; it must consume, not redefine, the validity boundary.

76. Keep already-decided classification separate from simulated terminal evaluation. Already-decided is a rule-based pre-Turn-1 classification with no simulation and owns the explicit bridge between the settled Faction model and the current two-Team runtime. Degenerate screening and probability aggregation consume structured simulation evidence in a later slice.

77. Keep terminal cache records/loading, build-time generation, Client orchestration, and lobby presentation as separate implementation issues. They have distinct ownership and QA evidence: semantic artifact contracts, generation diagnostics, deterministic service/adapter orchestration, and user-facing interaction behavior respectively.

78. The replacement implementation spine is:
    - S0, Role Composition boundary: 5-30 Player validation, hard-aligned coverage, and Actor Setup Card accounting; no dependencies.
    - S1, canonical scenario identity: Canonical Role Composition, Canonical Simulation Scenario, support classification, and simulator profile/version identity; blocked by S0.
    - S2, deterministic simulator evidence: seeded baseline decision behavior, Run Seed Material, structured Completed and Incomplete Simulation Run outcomes, ending Turn, and Victory Check Window evidence; blocked by S1.
    - #46, Wild Child later-night headless transition: rebrief away from closed #38 and block it on S2 so its deterministic random-play dependencies exist.
    - S3, already-decided classification: rule-based pre-Turn-1 classification and the current Team-to-Faction bridge; blocked by S1.
    - S4, simulated terminal evaluation: 1,000-run screening interpretation, 10,000-run probability aggregation, and terminal result production; blocked by S2, S3, and #46 for a complete current-profile support claim.
    - S5, cache records and loading: versioned Bundled Simulator Cache and Local Fallback Cache Record schemas, invalidation, serialization, and semantic round trips; blocked by S3 and S4.
    - S6, build-time generation: cacheable scenario enumeration, artifact production, omission handling, and machine-readable diagnostics; blocked by S5.
    - S7, Client orchestration: bundled/local lookup, fallback generation, timeout, persistence, retry, setup-change cancellation, simulator-unsupported handling, and Lobby Exit gate state; blocked by S4 and S5.
    - S8, lobby presentation: terminal evaluation summary/detail display, pending/unavailable/could-not-evaluate states, retry interaction, and Lobby Exit integration; blocked by S7.

79. S6 and S7 may proceed in parallel after S5. S8 may use fixtures while S6 produces the real bundled artifact, but the complete product delivery still requires both the generated artifact path and integrated lobby UX.

80. Use these work-item categories: S0, S1, S2, S5, and S6 are `architecture`; S3, S4, S7, and S8 are `feature`; #46 remains `bug`. Each work item has exactly one category label.

81. Create replacement issues with only their category labels. Do not apply `ready-for-agent` at creation, and do not use a negative readiness-state label. Establish the complete parent/blocker graph while the work remains in the refinement backlog; preparation and deliberate admission happen later under `docs/agents/issue-labels.md`.

82. The canonical Implementation Contract belongs in each work-item issue body under `docs/agents/implementation-contract.md`; comments remain discussion or evidence only. Implementation contracts should cite PRD #29, `CONTEXT.md`, relevant domain docs and ADRs, and `docs/agents/qa-strategy.md`. They should restate the slice-specific behavior needed for implementation rather than treating `docs/handoff.md` as a normative dependency. The handoff remains design history, unresolved-decision tracking, and the tracker-migration checklist.

83. Fixed Run Seed Material is used only to prove replay equivalence under the same simulator/profile version or the smallest property for a diagnosed regression. Do not snapshot an arbitrary simulator-produced winner, transcript, or win-rate and treat it as a correctness oracle. Screening and probability interpretation use handcrafted Simulation Result Evidence or batch summaries with independently known expectations. Broad simulator/cache runs produce generation diagnostics rather than CI assertions against expected win-rate thresholds. For #46, assert the Wild Child transition behavior and that the diagnosed path no longer throws; do not assert the eventual winning Faction or require a 1,000-game random batch as CI evidence.

84. S0 models and validates Actor Setup Cards now as setup artifacts separate from Role Composition, even though Actor UI and simulator support remain outside the first delivery. This closes the Core configuration/accounting mismatch without claiming Actor simulator support.

85. S3 produces only the semantic already-decided classification result. It does not own cache serialization or an app-facing terminal cache record; S5 later materializes the versioned serialized representation.

86. S4 owns the fixed 1,000-run screening and 10,000-run probability profile policy and exposes one shared simulated terminal-evaluation pipeline. Build-time generation and on-device fallback invoke that same pipeline rather than reimplementing classification or aggregation behavior.

87. S2 owns cooperative cancellation as a simulator mechanism: its public execution boundary accepts a cancellation signal, observes it at bounded safe points within both batch and individual headless-run work, and stops without publishing partial terminal evaluation output. S4 propagates cancellation through the shared pipeline. S7 owns the product policy that requests cancellation after 10 seconds or when the selected setup changes, and it maps timeout versus stale-setup cancellation to the correct Client state.

88. S5 owns the semantic terminal cache record formats, serializers/parsers, compatibility checks, invalidation behavior, and malformed/stale rejection. S7 owns actual bundled and local storage access, lookup order, and persistence orchestration.

89. S6 generates and packages a bundled cache artifact for the current Simulator Profile Role Set so the first delivery has a usable cache-first path. Full-role bundled-versus-remote distribution remains deferred and replaceable behind the S5/S7 boundaries; packaging the bounded initial artifact does not settle that future distribution decision.

90. S7 performs bundled and local lookup immediately for each valid stable scenario identity. When no usable record exists, automatic on-device fallback waits for a 500-millisecond quiet period after the latest setup change. Attempting Lobby Exit during that quiet period starts fallback immediately and then observes the existing pending-evaluation gate. Setup changes cancel the quiet period or in-flight stale evaluation. Deterministic service tests use a fake clock rather than wall-clock waits.

91. S8's Implementation Contract specifies the product meaning, information, and actions required for each Moderator-facing state and requires resource-backed Portuguese UI text. It does not freeze exact prose. Exact wording may be refined without changing cache or domain semantics.

92. Topic 10 does not choose whether S8 presents detailed probability output through inline disclosure, a modal, or navigation. S8 owns making the settled detailed information accessible from the lobby workflow, but its provisional contract remains unready until that interaction is resolved during preparation. Create a time-boxed UI spike only if direct refinement cannot make the choice.

93. Rename PRD #29 to `PRD: Pre-Game Lobby Evaluation`. Cache-first is a core requirement in the body, while the title names the product behavior rather than its implementation mechanism.

94. Rename the existing `Faction Win Probability Calculator` milestone to `Pre-Game Lobby Evaluation` so replacement work does not inherit stale credited-Faction or calculator framing.

95. Make S0-S8 and #46 direct child issues of PRD #29. Retain closed #30 and the closed superseded issues as historical children. Apply the approved native blocker graph and leave every replacement issue with only its category label. Do not reshape #42 or #43 during Topic 10; their parent/scope disposition belongs to the Future-Scope Boundaries branch.

96. Rewrite PRD #29 around the product goal and current Simulator Profile Role Set limits; cache-first evaluation flow; support and safety-gate states; already-decided, degenerate, and probability terminal evaluations; Game Result Frequency presentation; on-device fallback behavior; and product-level acceptance criteria. Its explicit current-scope exclusions are live mid-game evaluation, Moderator-configurable run counts, PMF/CDF framing, stratified sampling, balance recommendations, and full-role simulator support. Keep implementation filenames, issue-body contracts, and concrete test structure out of the PRD.

97. `docs/handoff.md` is temporary cross-topic coordination state, not a durable product, architecture, or implementation artifact. Keep it through the remaining grilling branches and tracker migration, compact it as durable records land, and delete it once every topic and propagation step is complete. Do not create an archival copy solely to preserve the grilling transcript.

98. During tracker migration, replace repeated handoff follow-ups with a compact completion checklist and links as PRD #29 and S0-S8 become canonical. Product behavior belongs in the PRD; implementation scope, assumptions, acceptance criteria, and QA claims belong in issue-body Implementation Contracts; architectural rationale stays in existing ADRs; vocabulary and rules stay in their current domain homes.

99. Remove the Actor Setup Card Validation and Hard-Aligned Coverage Validation sections from `docs/loose-ends.md` after S0 exists. Remove Bundled Simulator Cache Implementation after S5-S8 exist. If no genuinely untracked follow-up remains, delete `docs/loose-ends.md`; later grilling may recreate it when needed.

100. Topic 10 is complete at the approved shape and routing plan. Renaming and rewriting PRD #29, renaming the milestone, creating S0-S8, establishing native relationships, refreshing #46, cleaning migrated loose ends, and eventually deleting this handoff are tracker/document migration operations requiring separate execution authorization.

101. The first Pre-Game Lobby Evaluation delivery executes only the current Simulator Profile Role Set: Simple Werewolf, Seer, Wild Child, and Simple Villager. S0-S1 retain the settled full physical Role Composition validation, explicit simulator-support classification, versioned Simulation Scenario identity, and Actor Setup Card boundary as extension points. S0-S8 must not add executable behavior, cache entries, placeholder event schemas, feature flags, or UI for other Roles, Gypsy, New Moon Events, or New Moon Assignments. Full-role and New-Moon-enabled simulation require separate future work after the corresponding engine and app behavior exists.

102. Delete issue #42 during tracker migration. Do not retain, rewrite, replace, or attach it to PRD #29 or the Pre-Game Lobby Evaluation milestone. S2 retains only the pluggable decision-strategy/profile boundary needed by the fixed deterministic `baseline-random-*` profiles. Stratified sampling, importance weighting, realistic or skill-based strategies, alternative-strategy UX, and their metadata or statistical tests must not enter S0-S8. If any alternative strategy is pursued later, shape a new issue from then-current evidence and product needs.

103. Delete issue #43 during tracker migration. Do not retain, rewrite, replace, or attach it to PRD #29 or the Pre-Game Lobby Evaluation milestone. Current work retains only the general Simulation Start State input, cooperative cancellation, and non-mutating engine-reuse boundaries already required by pre-game evaluation. Live phase triggers, hidden-role posterior sampling, in-progress Game Session snapshot or clone machinery, live probability UI, adaptive run counts, and low-precision states must not enter S0-S8. If live mid-game projection is pursued later, shape a new issue from the delivered simulator and then-current product needs.

104. S6 packages the current-profile Bundled Simulator Cache and records packaged artifact size, hash, entry counts, and omission counts through its required machine-readable generation diagnostics. Artifact size is evidence, not a CI acceptance threshold, and the rough 5-10 MB comfort range is only a planning signal. S0-S8 must not add a remote manifest, downloader, CDN or GitHub Pages integration, network fallback, update protocol, or hybrid distribution abstraction. A bundled-versus-static-remote-versus-hybrid decision waits for realistic compressed full-role artifact measurement, including index and metadata overhead, and is then recorded in a dedicated ADR.

105. Reference Turn Horizon remains only a dormant glossary formula. S0-S8 do not compute, store, cache, test, or display it. The complete Game Result Frequency and Game Result Frequency by Turn contracts are the only extension hooks retained for future probability presentation. S0-S8 must not add confidence intervals, uncertainty bands, statistical terminology, profile comparison, sensitivity analysis, balance scoring, recommendations, data export, or a generic charting framework. No future issue is created until a concrete Moderator decision or action justifies that product work.

106. V1 simulator diagnostics and auditability stop at minimal stable Simulation Result Evidence, deterministic replay from Run Seed Material, and S6's machine-readable generation report. S0-S8 must not add full transcripts, an evidence database, a run browser, a diagnostics dashboard, audit or cache-provenance UI, a general replay CLI, or a performance profiler. Any broader tool requires a new future issue tied to demonstrated debugging, compliance, or operational need.

107. Freeze the first Simulator Profile Role Set to Simple Werewolf, Seer, Wild Child, and Simple Villager. A Role becoming implemented in the engine does not automatically admit it to simulation or cache generation. Profile expansion requires an explicit profile-version change after the Role's setup artifacts, automated decision behavior, Faction outcomes, completion behavior, and generation diagnostics are covered; affected cache identity is invalidated.

108. A rules-valid and app-supported setup containing any Role outside the frozen four-Role profile is simulator-unsupported and visibly evaluation-unavailable. The simulator must not drop unsupported Roles, substitute simpler Roles, ignore required setup or Event state, or emit partial probabilities. S0 may model physical validity and Actor Setup Cards, and S1 may canonicalize and support-classify such a scenario, without implying executable simulator support. Within the frozen profile, Seer prompts execute through the engine, while baseline-random decision behavior deliberately does not model human inference from revealed information.

109. Do not build speculative extension frameworks in S0-S8. The retained extension boundaries are only those already required by v1: Simulation Scenario and Simulation Start State, explicit profile/support/version identity, the decision-strategy boundary, cancellation, semantic cache records separated from storage orchestration, stable minimal evidence, and complete Game Result Frequency by Turn. Future work may revise these contracts behind a version bump. S0-S8 must not add plugin registries, generic Event payloads, strategy-weighting APIs, remote-cache providers, forward-compatible schemas for unknown Roles, or speculative UI frameworks. PRD #29 owns global non-goals; each implementation issue repeats only exclusions adjacent to its responsibility.

110. Topic 11 is complete at the confirmed boundary. No Future-Scope decision blocks rewriting PRD #29 or creating and finalizing the S0-S8 issue spine. Cache distribution is an intentional post-measurement decision, not a v1 blocker. The Topic 10 detailed-view interaction choice remains a preparation-time gate for S8's eventual `ready-for-agent` status only; it does not block PRD or issue-spine finalization. Deleting #42 and #43 and propagating these boundaries into PRD #29 and S0-S8 remain tracker-migration operations rather than documentation work in this session.

## Topic 11: Future-Scope Boundaries Settlements

Batch 1 bounded the first delivery to the current four-Role Simulator Profile Role Set while preserving only the already-settled generic support, scenario, setup-artifact, strategy, cancellation, and Simulation Start State boundaries. Full-role behavior and New Moon support are separate future products, not latent requirements inside S0-S8.

The stale future issues are intentionally discarded rather than carried forward. Issue #42 is deleted with no replacement for stratified or realistic strategies, and issue #43 is deleted with no replacement for live mid-game projection. Reconsidering either capability starts with a new issue and fresh design work after the pre-game product exists.

Batch 2 kept distribution replaceable without prebuilding remote infrastructure: S6 bundles and measures the current-profile artifact, while the distribution ADR waits for realistic full-role size evidence. Reference Turn Horizon and advanced statistics remain outside implementation with no speculative issue or framework. V1 auditability consists only of stable replay evidence and the machine-readable cache-generation report; richer diagnostics tooling is demand-driven future work.

Batch 3 froze the first simulator profile to its named four Roles and made later Role admission explicit and versioned. Unsupported Role or rule behavior is never approximated into a probability result, and newly implemented engine Roles do not join the profile automatically. Current issues retain only boundaries required by v1 and do not prebuild generic frameworks for hypothetical Roles, Events, strategies, distribution sources, or UI.

Topic 11 is complete and the shared understanding is confirmed. Future-scope decisions no longer block PRD #29 or S0-S8 finalization; only the already-deferred S8 interaction must be resolved before S8 itself can become ready for implementation.

## Topic 10: Issue / ADR Reshaping Settlements

Batch 1 kept the architectural record stable while changing the product record. The completed simulator-cache grilling refined existing ADR boundaries rather than reversing ADR-0005 or introducing a new hard-to-reverse architecture choice. PRD #29 should remain the parent product document, but both its title and body now need replacement so implementation agents do not inherit the stale on-device-first, live recomputation, configurable-run-count, or PMF/CDF framing.

Batch 2 chose clean replacement issues over in-place rewrites. Issues #38, #39, #40, #41, #59, and #60 should retain their historical bodies and receive closure comments explaining which settled design made them stale and where any valid responsibility moves. Bug #46 remains a targeted prerequisite only for simulator execution and cache-generation work that claims Wild Child support.

Batch 3 established a nine-issue replacement spine plus the existing #46 bug. The first delivery is deliberately bounded by the current Simulator Profile Role Set. Validation, canonical identity, simulator evidence, rule-based already-decided classification, simulated terminal evaluation, cache records, build-time generation, Client orchestration, and lobby presentation have separate ownership. Cache generation and Client orchestration can proceed in parallel after the shared cache contract exists.

Batch 4 assigned category labels and adopted the current issue-body Implementation Contract lifecycle. Replacement issues enter the refinement backlog with only their category labels. The handoff is not normative implementation input. Fixed seeds are limited to repeatability and minimal diagnosed-regression claims; aggregation uses handcrafted evidence, and broad generation uses diagnostics rather than expected win-rate assertions.

Batch 5 began the issue-by-issue contract review. S0 owns the explicit Actor Setup Card model and validation but not Actor UI or simulator support. S2 owns cooperative cancellation while S7 owns timeout and stale-setup policy. S3 stops at semantic already-decided output, with serialization deferred to S5. S4 owns the shared fixed-profile pipeline used by both build-time generation and on-device fallback.

S5 owns semantic cache formats while S7 owns storage and lookup orchestration. S6 packages the bounded current-profile artifact without deciding future full-role distribution. S7 uses immediate lookup and a 500-millisecond quiet period before fallback, accelerated by a Lobby Exit attempt. S8 owns semantic Portuguese presentation without freezing exact prose. Its detailed-view interaction pattern remains a preparation-time UX decision rather than a Topic 10 architecture choice.

Batch 6 named the product and tracker umbrella `Pre-Game Lobby Evaluation`. S0-S8 and #46 are direct PRD children, while closed historical children remain attached. The rewritten PRD owns product behavior, current runtime limits, and explicit non-goals without absorbing implementation contracts. Issues #42 and #43 remain untouched for the Future-Scope Boundaries branch.

Batch 7 confirmed that the handoff is temporary orchestration memory for the staged grilling, not a permanent historical source. It remains available to the final Future-Scope branch and tracker migration, then is deleted after every decision has a durable home. Loose-end entries are likewise removed when their canonical issues exist, and the file is deleted if nothing untracked remains.

## Topic 1: Faction Model Settlements

Topic 1 added and clarified these domain concepts in `CONTEXT.md`:

- **Starting Faction**, **Possible Faction**, **Transient Faction**, and **Latent Faction** describe Faction lifecycle in simulations and probability output.
- **Hard-Aligned Role** is a Role whose default allegiance is fixed by its Role, as opposed to setup/runtime choices.
- **Minimum Viable Role Composition** requires at least one hard-aligned Villager Role and at least one hard-aligned Werewolf Role.
- **Cross-Faction Lovers** are a distinct Faction outcome; same-Faction Lovers are not.
- **Actor Setup Cards** are setup artifacts, not part of Role Composition.

Topic 1 also clarified `docs/domain/game-rules.md`:

- Thief sees undealt cards after random distribution; cards are not planned or set aside in advance.
- Actor Setup Cards must be eligible hard-aligned Villager Roles and do not transfer win conditions.

Role Composition now includes Thief undealt cards and excludes Actor Setup Cards. Cache-facing probability output should include the Simulation Scenario's zero-frequency Possible Game Result rows where useful to the Moderator, including single-Faction results for Possible Factions that never win or never come into being, No-Winner, and scenario-specific possible Shared Victory Outcome combinations.

## Topic 2: Win Condition Semantics Settlements

Topic 2 introduced a split between beneficiary and operational membership:

- **Faction Beneficiary**: who wins with a Faction. Each Player has exactly one at a time.
- **Faction Agent**: operational membership for waking, acting, and detection.

Key general semantics:

- Seer and generic "Werewolf" checks use Werewolf Faction Agent unless a rule explicitly says Role or Character Card.
- Villagers win by eliminating all non-Villager Faction Beneficiaries.
- Werewolves win by eliminating all non-Werewolf Faction Beneficiaries.
- The Werewolf Control Shortcut applies only when all living non-Werewolf beneficiaries are Villager beneficiaries, and uses Durable Voting Power.
- Durable Voting Power includes permanent voting changes currently in force, including event-originated ones, and excludes one-window effects.
- Shared Victory Outcome is allowed when multiple win conditions are true in one Victory Check Window.
- No-Winner Outcome occurs when no Faction win condition is true and every Player is Eliminated.
- Cache-facing probability output uses Game Result Frequency, where Shared Victory Outcomes are their own Game Results and the distribution sums to 100%.
- Probability output includes the Simulation Scenario's Possible Game Result inventory as rows, including zero-frequency single-Faction results for Possible Factions that never win or never come into being, No-Winner, and scenario-specific Shared Victory Outcome combinations.

Key role-specific semantics:

- White Werewolf is a Werewolf Faction Agent but White Werewolf Faction Beneficiary.
- Infection changes Faction Agent status, not Faction Beneficiary.
- Permanent Role Swaps change Faction Beneficiary to the new Role's default Faction unless explicit precedence applies.
- Cross-Faction Lovers immediately replace both beneficiary links and take precedence over later effects.
- Devoted Servant cannot activate if Lover; successful swap uses the new Role's default beneficiary.
- Miracle reviving only one eliminated Cross-Faction Lover breaks Cross-Faction Lovers.
- Actor is a hard-aligned Villager Role and counts toward hard-aligned Villager composition requirements.
- Actor Setup Cards are outside Role Composition and must be hard-aligned Villager Roles with actionable individual powers.
- Elder village-vote penalty suppresses all Villager Role powers, including Actor, regardless of Faction Beneficiary, and continues to suppress later swaps into Villager Roles.
- Big Bad Wolf extra attack is disabled once any non-temporary Werewolf Faction Agent has been Eliminated.
- Full Moon Rising temporary Werewolf Faction Agents affect operational checks while active but do not affect beneficiary links or Big Bad Wolf disablement.
- Double Agent target must be a living non-Werewolf Faction Agent.

## Topic 3: Role Composition Space Settlements

Topic 3 clarified the boundary between pre-game Role Composition, setup artifacts, simulator scenarios, and arbitrary simulation start states:

- **Role Composition** is only the pre-game multiset of Role cards. It includes Thief's two extra cards and excludes Actor Setup Cards, New Moon Events, Player names, Seating Order, Status Effects, Sheriff, Lovers, Charmed, Prejudiced Manipulator groups, and physical traits.
- Actor does not increase Role Composition size. Actor Setup Cards are separate setup artifacts and must be three eligible hard-aligned Villager Roles with actionable individual powers that are not already selected in the Role Composition.
- Thief increases Role Composition size by two cards. The extra cards are not preselected as undealt; they become the leftovers after random deal.
- **Simulation Scenario** is the pre-game cache/lobby input. It includes Player count, Canonical Role Composition, and any setup artifacts or non-default assumptions that affect simulation.
- **Simulation Start State** is the general simulator input for running N simulations from any fully defined Game Session state. A lobby Simulation Scenario can produce a pre-game Simulation Start State; mid-game projection should use the same simulation mechanism from a later state.
- **Degenerate Simulation Scenario** replaces the older "Degenerate Role Composition" wording because screening depends on simulator profile and setup assumptions, not only the Role Composition.
- New Moon Events are outside Role Composition and outside v1 simulator scope unless a future Simulation Scenario explicitly enables New Moon support. Town Crier is a New Moon Assignment like Sheriff, not a Role Composition Role.
- Prejudiced Manipulator uses an even public group split as the baseline simulator profile default. Only non-default group models need explicit Simulation Scenario material.
- Supported Player Count caps Players only. Thief can make Role Composition card count exceed Player count, and Actor Setup Cards add physical cards outside Role Composition.

Topic 3 added validity/support layers:

- **Rules-Valid Role Composition** satisfies physical card-count, role-count, and hard-aligned Faction coverage rules.
- **App-Supported Role Composition** is rules-valid and actually supported by the app's product/UX implementation.
- **Simulator-Supported Simulation Scenario** can be executed by the active simulator profile.
- **Cacheable Simulation Scenario** is simulator-supported and eligible for build-time cache generation.
- Classification order is: Rules-Valid Role Composition, App-Supported Role Composition, Simulator-Supported Simulation Scenario, Already-Decided Role Composition, Degenerate Simulation Scenario, then probability simulation.

Topic 3 also clarified role-set and canonicalization language:

- Use **Rules Role Set**, **Implemented Role Set**, **Simulator Profile Role Set**, and **Selectable Role Set** instead of overloaded "supported roles."
- Hard-aligned coverage replaces mandatory Simple Villager/Simple Werewolf by role name. Simple Villager and Simple Werewolf may be 0 in the full-suite domain model as long as hard-aligned Villager and Werewolf coverage exists.
- Canonical Role Composition omits zero-count Roles, uses exact enum identifiers, sorts alphabetically by enum identifier, and never includes Actor Setup Cards.
- Canonical Simulation Scenario includes `players=N` separately because Thief makes Role card count differ from Player count.

## Faction Inventory Notes

A read-only explorer produced this high-level faction inventory from `docs/domain/game-rules.md`:

- Villager Faction: Starting Faction if represented by the Role Composition. Supported Role Compositions require at least one hard-aligned Villager Role.
- Werewolf Faction: Starting Faction if represented by the Role Composition. Supported Role Compositions require at least one hard-aligned Werewolf Role.
- Cross-Faction Lovers Faction: Latent Faction through Cupid; same-Faction Lovers are not a distinct Faction.
- White Werewolf Solo Faction: Starting Faction if White Werewolf is in the Role Composition; wakes operationally with Werewolves but does not benefit from normal Werewolf victory.
- Angel Solo Faction: Transient Faction if Angel is in the Role Composition; excluded from Initial Faction Count but included as a possible probability output row.
- Piper Solo Faction: Starting Faction if Piper is in the Role Composition; Charmed players do not benefit from Piper victory.
- Prejudiced Manipulator Solo Faction: Starting Faction if the role is present, but balance depends on public group split.

Not separate Factions: Role Groups such as Ambiguous, Loners, New Moon; Status Effects such as Charmed, Sheriff, Executioner, Town Crier, Little Rascal unless they define a distinct win condition.

## Files Already Updated

- `CONTEXT.md`: added and clarified Supported Player Count, Role Composition, Actor Setup Cards, Already-Decided Role Composition, Degenerate Simulation Scenario, Balanced Role Composition, Faction lifecycle terms, Initial Faction Count, Reference Turn Horizon, Run Seed Material, Simulation Scenario, Canonical Role Composition, Canonical Simulation Scenario, Simulation Start State, and role-set/support layers.
- `CONTEXT.md`: later branches added Faction Beneficiary, Faction Agent, win-condition outcome terminology, Game Result Frequency terms, and Bundled Simulator Cache terminology.
- `docs/domain/game-rules.md`: clarified Thief undealt-card behavior, Actor Setup Card constraints, win-condition semantics from Topic 2, and Town Crier as a New Moon Assignment rather than a Role Composition Role.
- `docs/domain/game-rules-clarifications.md`: records role interaction rulings and physical-rule disambiguations that should not live in the glossary.
- `docs/domain/invariants.md`: records stable domain facts that implementation and tests can assert without carrying rationale.
- `docs/agents/domain.md`: teaches future agents to use the new invariants and rules-clarification homes.
- `docs/adr/0007-simulation-scenario-boundary.md`: records the Simulation Scenario boundary between Role Composition and per-run Simulation Start State.
- `docs/adr/0008-lobby-evaluation-pipeline-uses-layered-gates.md`: records the layered lobby evaluation gates and evidence boundaries.
- `docs/adr/0009-simulation-evidence-diagnostics-and-cache-artifacts.md`: records the Simulation Result Evidence, diagnostics, and compressed cache artifact boundary.
- `docs/adr/0010-faction-model-separates-beneficiaries-from-agents.md`: records the Faction Beneficiary/Faction Agent split and the limits of legacy Team.
- `docs/adr/0011-victory-check-windows-are-resolution-boundaries.md`: records Victory Check Windows as outcome-resolution transaction boundaries.
- `docs/handoff.md`: records the staged simulator/cache decisions and follow-up leads.
- `docs/loose-ends.md`: records implementation follow-ups for Actor Setup Card validation, hard-aligned coverage validation, and Bundled Simulator Cache implementation.
- `docs/agents/qa-strategy.md`: records simulator/cache QA evidence routing, deterministic probability-test policy, golden fixture meaning, and done-evidence expectations.

Topic 8 did not create a new ADR because it extends the existing lobby evaluation and cache/evidence decisions in ADR-0008 and ADR-0009 rather than introducing a separate hard-to-reverse architectural choice. `docs/domain/game-rules.md` remains unchanged because fallback runtime is product/runtime behavior, not a physical game rule.

## Follow-Up Leads

1. Rename PRD #29 to `PRD: Pre-Game Lobby Evaluation` and rewrite it to replace stale "Win Probability Simulator" and on-device-first/cache-as-repeat language with cache-first pre-game lobby evaluation, on-device fallback, scenario-based screening and terminal evaluations, the Topic 7 Game Result Frequency probability-output contract, the Topic 8 fallback runtime safety-gate contract, and the Topic 11 frozen four-Role profile and explicit non-goals.

2. Rename the existing milestone to `Pre-Game Lobby Evaluation`.

3. Write the nine replacement implementation issues S0-S8 from Topic 10, make them direct children of #29, preserve their ownership boundaries and formal blocker relationships, and leave them with category labels only. Redistribute the narrow evidence responsibilities from #59 and #60 into S2, S6, and each issue's claim-first QA evidence. Do not replace #40 in the current scope. Delete #42 and #43 without replacement; future alternate-strategy or live-projection work starts from newly shaped issues only if reconsidered.

4. Refresh #46's canonical issue-body Implementation Contract after S2 exists, make it a direct child of #29, remove dependency language tied to closed #38, and make S2 its formal blocker; #46 then blocks S4's complete current-profile support claim and S6 cache generation for Wild Child scenarios.

5. Have S6 package the frozen-profile Bundled Simulator Cache and report its size, hash, entry counts, and omission counts without a size pass/fail threshold. Defer the cache distribution ADR until a realistic compressed full-role artifact, including index and metadata overhead, can be measured. Bundled cache, static remote cache, and hybrid distribution remain open; remote distribution infrastructure is outside S0-S8, while on-device fallback is settled and mandatory.

6. Implement a shared Role Composition canonicalizer. It must sort exact enum names alphabetically, include non-zero counts only, include Thief extras, exclude Actor Setup Cards, and be reused by cache keys and Run Seed Material.

7. Add app-wide max player validation of 30 in Core and update product docs to distinguish supported range from practical sweet spot.

8. Define the already-decided pre-Turn-1 win-condition check using the Topic 2 Faction Beneficiary semantics. The current runtime still only supports two `Team`s, so implementation should bridge carefully rather than pretending full Faction support already exists.

9. Implement Bundled Simulator Cache terminal lobby evaluations for already-decided, degenerate, and probability results, preserving the semantic contract without putting per-run simulation evidence in the app-facing cache.

10. Implement fallback runtime behavior from Topic 8: unified "Simulating match..." lobby status for cache and fallback lookup, Local Fallback Cache Record persistence, cache/local invalidation, 10-second timeout enforcement, visible "could not evaluate" failure state, session-only failure memory, retry-after-failure, no in-progress skip/dismiss action, simulator-unsupported evaluation-unavailable state, and Lobby Exit safety-gate tests.

11. Define deterministic seed hashing at the last-mile PRNG boundary. Store canonical Run Seed Material as string evidence.

12. Address Role Composition implementation loose ends:
   - Actor currently adds `+3` roles in `GameSessionConfig`, but the settled domain model says Actor Setup Cards are outside Role Composition.
   - Actor role selection must leave at least three eligible Actor Setup Cards outside the Role Composition.
   - Current code requires `SimpleVillager >= 1` and `SimpleWerewolf >= 1`, but the settled full-suite domain model requires hard-aligned Villager/Werewolf coverage instead.
   - Current code groups Actor as `RoleGroup.Ambiguous`, but the settled domain model treats Actor as hard-aligned Villager.

13. Clarify open rule ambiguities before full-suite implementation:
    - Angel cutoff timing.
    - Piper "all surviving players" versus "cannot charm self".
    - Any remaining Lovers edge cases not covered by Topic 2 precedence rules.
    - Any remaining New Moon event effects that alter durable voting power, Faction Agent status, or win-condition checks.

14. Keep `Reference Turn Horizon` dormant and out of computation, cache contracts, tests, and UI until there is a clear Moderator action attached to it. Do not create a speculative future issue.

15. Suggested next skills:
    - `to-issues` if converting this design into GitHub issues.
    - `triage` if preparing issue-body Implementation Contracts and admitting unblocked work.
    - `tdd` when implementing canonicalization, cache artifacts, and simulator screening.

## Topic 12: Tracker Migration And Finalization Settlements

Batch 1 established a three-stage migration boundary. First, finalize and land the durable Topic 1-12 documentation on the default branch while retaining `docs/handoff.md` and `docs/loose-ends.md` as the migration checklist. Second, execute and verify the tracker migration against those landed sources. Third, remove temporary references, delete the two temporary files, and land the cleanup only after tracker verification succeeds.

Rename milestone 2 to `Pre-Game Lobby Evaluation` and use it for active delivery work: PRD #29, S0-S8, and #46. The already-attached closed superseded issues remain in the renamed milestone as history. Closed spike #30 remains a historical child of #29 without a milestone assignment.

Milestone rename and permanent deletion of #42 and #43 are one-off administrative operations. Execute them directly through GitHub rather than adding repository CRUD wrappers. This chooses the execution mechanism only; no tracker mutation is authorized until the full Topic 12 migration plan is confirmed. Remove the stale `ready-for-agent` label from closed #30 during tracker migration because it violates the current readiness invariant.

Batch 2 fixed the replacement spine without further refinement. Use these canonical issue titles and categories: S0 `Role Composition validation and setup-artifact boundaries` (`architecture`); S1 `Canonical Simulation Scenario identity and simulator support classification` (`architecture`); S2 `Deterministic headless simulation evidence and cancellation` (`architecture`); S3 `Already-Decided Role Composition classification` (`feature`); S4 `Simulated terminal lobby evaluation pipeline` (`feature`); S5 `Versioned terminal lobby evaluation cache records` (`architecture`); S6 `Build-time Bundled Simulator Cache generation` (`architecture`); S7 `Client lobby evaluation lookup and fallback orchestration` (`feature`); and S8 `Pre-game Lobby Evaluation presentation and Lobby Exit gate` (`feature`).

Use exactly this native blocker graph: S1 is blocked by S0; S2 and S3 are blocked by S1; #46 is blocked by S2; S4 is blocked by S2, S3, and #46; S5 is blocked by S3 and S4; S6 is blocked by S5; S7 is blocked by S4 and S5; and S8 is blocked by S7. Do not add a redundant direct #46-to-S6 edge; #46 blocks Wild Child cache generation transitively through S4 and S5.

After every issue, parent, milestone assignment, and blocker edge exists, prepare and deliberately admit S0 only. S0 receives a `Validated against` default-branch anchor and `ready-for-agent` as the final mutation. S1-S8 and #46 retain `Drafted against` anchors, exactly one category label, and provisional contracts without readiness. S8 also retains the detailed-view interaction choice as an explicit preparation-time readiness gate.

Batch 3 closed the remaining product and ownership gaps. S7 lookup precedence is Bundled Simulator Cache, then Local Fallback Cache Record, then On-Device Fallback Generation when neither source has a usable record. PRD #29 remains open until S0-S8 and #46 are closed and the cache-first lobby flow passes product-level end-to-end acceptance; the PRD states product acceptance criteria without mirroring the native child or blocker graph.

Delete `docs/loose-ends.md` after its three current sections are represented by S0 and S5-S8. Do not preserve the handoff's old generic rule-ambiguity lead: Angel and Piper are resolved in durable domain docs, while unspecified Lovers or New Moon questions are future full-role refinement rather than currently executable work.

## Topic 12 Tracker Migration Payload

The bodies below are the approved migration payload. Replace `<Stage A default-branch SHA>` during tracker publication with the default-branch commit that landed the durable pre-migration documentation. These bodies cite durable sources only; `docs/handoff.md` is not a normative implementation dependency.

### PRD #29

Title: `PRD: Pre-Game Lobby Evaluation`

```markdown
## Product goal

Help the Moderator evaluate a selected Role Composition before Lobby Exit. The product uses cache-first baseline simulation evidence to identify setups that already produce a win, setups whose baseline games always end during Turn 1, and the Game Result Frequency of setups that pass those gates. It provides evidence for Moderator judgment; it does not label a Role Composition as balanced or recommend changes.

## Current delivery boundary

The first delivery evaluates Simulation Scenarios in the frozen Simulator Profile Role Set: Simple Werewolf, Seer, Wild Child, and Simple Villager. Player count is supported from 5 through 30. A rules-valid and app-supported setup containing any other Role remains simulator-unsupported: evaluation is visibly unavailable, no fallback is attempted, and Lobby Exit is not blocked solely by that unavailability.

Lobby evaluation follows the ordered boundaries defined by ADR-0008: Rules-Valid Role Composition, App-Supported Role Composition, Simulator-Supported Simulation Scenario, Already-Decided Role Composition, Degenerate Simulation Scenario, then probability output. Unsupported rules, Roles, setup artifacts, or Events are never dropped, substituted, or approximated into a probability result.

## Cache-first flow

For each valid stable Canonical Simulation Scenario, the Client checks the Bundled Simulator Cache first and persisted Local Fallback Cache Records second. If neither source has a usable record, On-Device Fallback Generation begins after a 500 millisecond quiet period. Attempting Lobby Exit during that quiet period starts fallback immediately.

Fallback uses the same terminal-evaluation pipeline as Build-Time Cache Generation. It is cancelled when the selected setup changes and is bounded by 10 seconds. A successful fallback persists only a compact Local Fallback Cache Record. Failure, timeout, cancellation for runtime reasons, instruction-limit exhaustion, start-state failure, or an incomplete required batch produces no partial terminal evaluation.

## Moderator-facing states

- Pending evaluation blocks Lobby Exit and offers no skip or dismiss action.
- An Already-Decided Role Composition blocks Lobby Exit and explains the pre-Turn-1 winning Game Result.
- A Degenerate Simulation Scenario blocks Lobby Exit and explains that every completed baseline screening game ended during Turn 1.
- A probability terminal evaluation allows Lobby Exit and presents Game Result Frequency evidence.
- Simulator-unsupported evaluation is visibly unavailable and does not itself block Lobby Exit.
- A failed or timed-out fallback becomes a visible could-not-evaluate state, releases the safety gate, and offers an explicit retry. Failure memory lasts only for the unchanged setup in the current app session.

All Moderator-facing text is resource-backed Portuguese. Exact prose may be refined without changing the state meanings above.

## Probability output

Probability output uses mutually exclusive Game Results: one Faction win, a specific Shared Victory Outcome, or No-Winner Outcome. It shows the Simulation Scenario's Possible Game Result inventory, including zero-frequency results, and distinguishes not-observed from impossible.

The primary view uses whole-percentage Game Result Frequency. Possible results below 1 percent exact frequency, including zero-frequency results, may be grouped by outcome name as possible but unlikely. Detailed output derives ending frequency by Turn from Game Result Frequency by Turn while collapsing Victory Check Windows in the Moderator-facing view. A brief finite-baseline caveat may appear in detail, but confidence intervals and statistical terminology do not.

## Product acceptance criteria

- [ ] Every cacheable current-profile Simulation Scenario has a version-compatible bundled terminal-evaluation path or an explicitly diagnosed generation omission.
- [ ] Bundled lookup, local lookup, and successful bounded fallback produce equivalent terminal lobby-evaluation meaning.
- [ ] Already-decided and degenerate evaluations block Lobby Exit; pending evaluation blocks only while unresolved; simulator-unavailable and could-not-evaluate states follow the release behavior above.
- [ ] Probability output preserves the complete Possible Game Result inventory, Shared Victory Outcomes, No-Winner Outcome, zero-frequency meaning, and ending-Turn detail.
- [ ] Setup changes cannot publish stale evaluation output, and fallback cannot publish partial output after failure, timeout, or cancellation.
- [ ] The integrated lobby flow is usable with the packaged current-profile Bundled Simulator Cache and resource-backed Portuguese presentation.
- [ ] S0-S8 and #46 are closed and the cache-first lobby flow has end-to-end verification evidence.

## Current-scope exclusions

This delivery does not include live mid-game evaluation, Moderator-configurable run counts, PMF/CDF framing, stratified or weighted strategies, balance scores or recommendations, confidence intervals, Reference Turn Horizon output, full-role or New Moon simulator support, remote cache distribution, diagnostics or provenance UI, full transcripts, a replay browser or general replay CLI, or speculative extension frameworks.

Architectural rationale and stable terminology remain in `CONTEXT.md`, `docs/domain/`, and ADR-0005, ADR-0007, ADR-0008, and ADR-0009. Implementation and QA ownership belongs in the child work-item contracts and `docs/agents/qa-strategy.md`.
```

### S0 - Role Composition validation and setup-artifact boundaries

```markdown
## Implementation Contract

**Validated against:** <Stage A default-branch SHA>

### Outcome

Core exposes one coherent physical-setup validation boundary for 5-30 Players. Role Composition contains one Role card per Player plus Thief's two extra Character Cards, while Actor Setup Cards are modeled and validated as separate setup artifacts. Rules-validity requires hard-aligned Villager and Werewolf coverage without requiring Simple Villager or Simple Werewolf by name.

### Acceptance criteria

- [ ] Player counts from 5 through 30 are accepted at the player-count boundary; counts outside that range produce a specific validation failure.
- [ ] Role Composition card count equals Player count plus two exactly when Thief is present; Actor does not increase Role Composition card count.
- [ ] When Actor is present, exactly three Actor Setup Cards are supplied outside Role Composition, are not already selected in Role Composition, and are eligible hard-aligned Villager Roles with actionable individual powers.
- [ ] Simple Villager, Villager-Villager, Two Sisters, and Three Brothers are rejected as Actor Setup Cards.
- [ ] Actor is classified as hard-aligned Villager wherever setup validity and lobby grouping depend on that classification.
- [ ] Rules-validity requires at least one hard-aligned Villager Role and one hard-aligned Werewolf Role while preserving the existing per-Role cardinality rules.
- [ ] Existing Thief extra-card behavior remains valid and distinct from Actor Setup Cards.

### Scope boundaries

In scope:

- Physical Role Composition, Actor Setup Card, hard-aligned coverage, and Supported Player Count models and validation.
- Public validation results usable by Core and Client callers.

Out of scope:

- Actor gameplay UI, Actor simulator execution, cache identity, simulation support classification, and probability behavior.
- Expanding the Selectable Role Set or Simulator Profile Role Set.

### Dependency assumptions

- PRD #29, `CONTEXT.md`, `docs/domain/game-rules.md`, and `docs/domain/invariants.md` define the physical setup terms and rules.
- ADR-0007 keeps Actor Setup Cards outside Role Composition while allowing them in a Simulation Scenario.
- There are no implementation blockers or additional product decisions.

### Verification

- **Claim:** Public setup validation enforces 5-30 Players, Role Composition card accounting, hard-aligned coverage, and existing per-Role cardinality behavior.
  **Preferred evidence:** Deterministic Core integration tests through the public configuration and validation APIs.
  **Forbidden evidence:** Source scans, private-member assertions, or tests coupled to dictionary layout or validation implementation order.
  **Source-test allowlist needed:** no.
- **Claim:** Actor Setup Cards are separate, exactly constrained, and eligibility-checked without changing Role Composition identity.
  **Preferred evidence:** Deterministic value/model tests plus public Core integration tests covering valid and invalid Actor setups and Thief-plus-Actor cases.
  **Forbidden evidence:** Raw enum-source assertions or tests that infer the contract from localized UI metadata.
  **Source-test allowlist needed:** no.
```

### S1 - Canonical Simulation Scenario identity and simulator support classification

```markdown
## Implementation Contract

**Drafted against:** <Stage A default-branch SHA>

### Outcome

Core provides deterministic Canonical Role Composition and Canonical Simulation Scenario identities plus explicit rules, app, simulator, and cacheability classifications. Identity includes every behavior-affecting pre-game input and an explicit simulator profile/version while keeping profile defaults implicit.

### Acceptance criteria

- [ ] Canonical Role Composition includes non-zero Role counts only, uses exact enum identifiers sorted alphabetically, includes Thief extras, and excludes Actor Setup Cards.
- [ ] Canonical Simulation Scenario includes `players=N` separately from Role counts and deterministically includes Actor Setup Cards and every supported non-default assumption that affects simulation.
- [ ] Equivalent inputs produce identical canonical identity regardless of collection order; behaviorally different inputs cannot share an identity.
- [ ] The active simulator profile has a stable id/version and explicitly admits only Simple Werewolf, Seer, Wild Child, and Simple Villager.
- [ ] Rules-valid, app-supported, simulator-supported, and cacheable classifications remain distinct and consume S0 validity rather than redefining it.
- [ ] A rules-valid and app-supported scenario with any Role or required rule state outside the frozen profile is classified simulator-unsupported without dropping or substituting input.
- [ ] Canonical identities support structural round trip or parse validation at the serialization/key boundary.

### Scope boundaries

In scope:

- Canonical identity value contracts, simulator profile/version identity, compatibility comparison, and support/cacheability classification.

Out of scope:

- Running simulations, Run Seed Material, terminal evaluation, cache-record serialization, storage lookup, and UI.
- Generic registries or forward-compatible schemas for unknown Roles, Events, strategies, or remote cache providers.

### Dependency assumptions

- S0 has landed the physical validity and setup-artifact boundary.
- PRD #29, `CONTEXT.md`, `docs/domain/invariants.md`, ADR-0005, ADR-0007, and ADR-0008 govern terminology, profile admission, identity, and support gates.

### Verification

- **Claim:** Canonical identities are deterministic, structurally complete, and distinguish all supported behavior-affecting inputs.
  **Preferred evidence:** Deterministic value tests, structural assertions, and narrow serializer/key-boundary round trips using small golden fixtures where the literal format is contractual.
  **Forbidden evidence:** Broad exact-string snapshots outside the serializer/key boundary or source scans over enum declarations and implementation methods.
  **Source-test allowlist needed:** no.
- **Claim:** Support classification preserves the four distinct gates and never approximates an unsupported scenario.
  **Preferred evidence:** Core integration tests through public classification APIs with rules-invalid, app-unsupported, simulator-unsupported, and cacheable scenarios.
  **Forbidden evidence:** Tests that equate Selectable Role Set, Implemented Role Set, and Simulator Profile Role Set or inspect private branching.
  **Source-test allowlist needed:** no.
```

### S2 - Deterministic headless simulation evidence and cancellation

```markdown
## Implementation Contract

**Drafted against:** <Stage A default-branch SHA>

### Outcome

The engine-reuse simulator can derive seeded pre-game Simulation Start States, drive current-profile Game Sessions with deterministic baseline-random decisions, and report stable minimal Simulation Result Evidence for every attempted run. Batch and per-run execution support cooperative cancellation and never publish partial terminal evaluation output after cancellation.

### Acceptance criteria

- [ ] Run Seed Material is a canonical string containing simulator and profile/version identity, run number, Player count, Canonical Role Composition, and Simulation Scenario assumptions, with profile defaults implicit.
- [ ] The numeric PRNG seed is derived deterministically from Run Seed Material only at the random-generator boundary.
- [ ] Reusing the same Run Seed Material under the same simulator/profile version reproduces the same stable per-run source record.
- [ ] Baseline-random behavior handles every Moderator Instruction reachable by the frozen profile, including Seer prompts, without modeling human inference from revealed information.
- [ ] Every attempted run yields either a Completed Simulation Run with Game Session Outcome, ending Turn, and Victory Check Window, or an Incomplete Simulation Run with replayable Run Seed Material; incomplete runs are never converted to No-Winner Outcomes.
- [ ] Batch evidence preserves one minimal source record per attempted run and completed/incomplete counts without requiring transcripts or final-state snapshots.
- [ ] Public batch and individual-run boundaries accept cancellation, observe it at bounded safe points, and stop without publishing partial terminal evaluation output.

### Scope boundaries

In scope:

- Seeded start-state derivation, baseline-random decision behavior, headless execution, minimal stable evidence, replay determinism, and cooperative cancellation mechanisms.

Out of scope:

- Already-decided classification, screening/probability interpretation, cache records, build-time enumeration, Client timeout policy, full transcripts, a replay CLI, performance profiling, and alternative or weighted strategies.

### Dependency assumptions

- S1 has landed canonical scenario and simulator profile/version identity.
- PRD #29, `CONTEXT.md`, `docs/domain/invariants.md`, ADR-0005, ADR-0007, ADR-0009, and `docs/agents/qa-strategy.md` define engine reuse, start-state boundaries, stable evidence, and replay limits.

### Verification

- **Claim:** Current-profile headless execution produces correctly classified minimal source records and deterministic replay from Run Seed Material.
  **Preferred evidence:** Deterministic Core integration tests through public simulator APIs, using fixed Run Seed Material only for replay equivalence, independently known outcomes, or diagnosed regression properties.
  **Forbidden evidence:** Arbitrary recorded winner snapshots, expected random win rates, full transcript snapshots, timing thresholds, or source tests.
  **Source-test allowlist needed:** no.
- **Claim:** Cancellation stops batch and per-run work at bounded safe points without exposing partial terminal output.
  **Preferred evidence:** Deterministic integration tests with controlled cancellation signals and observable completion/cancellation results.
  **Forbidden evidence:** Wall-clock sleeps, performance assertions, private-loop counters, or source scans for cancellation-token usage.
  **Source-test allowlist needed:** no.
```

### S3 - Already-Decided Role Composition classification

```markdown
## Implementation Contract

**Drafted against:** <Stage A default-branch SHA>

### Outcome

Core can classify an Already-Decided Role Composition at Lobby Exit from Role Composition evidence alone. The classifier evaluates every Faction victory trigger available at that boundary, returns mutually exclusive Game Result semantics including Shared Victory Outcome, and explicitly bridges the settled Faction model to the current two-Team runtime without pretending the runtime already supports every Faction.

### Acceptance criteria

- [ ] Classification runs only after rules, app, and simulator support gates pass and consumes Canonical Role Composition without deriving a Simulation Start State.
- [ ] Every Faction victory trigger that can be decided from Role Composition evidence at Lobby Exit is evaluated without random assignment, setup branches, Night 1 choices, or simulation.
- [ ] A single true predicate returns the corresponding single-Faction Game Result and a stable explanation reason.
- [ ] Multiple true predicates at the same Lobby Exit boundary return one Shared Victory Outcome rather than applying priority ordering or crediting Factions separately.
- [ ] No true predicate returns a not-already-decided semantic result and does not imply that the scenario is balanced.
- [ ] The current Team-to-Faction bridge is explicit, bounded to implemented runtime evidence, and does not erase unsupported Factions or manufacture probability output.

### Scope boundaries

In scope:

- Pure pre-Turn-1 already-decided semantics, Game Result classification, explanation evidence, and the current runtime bridge.

Out of scope:

- Simulation, degenerate screening, probability aggregation, cache serialization, Client state, and a full engine-wide Team-to-Faction migration.

### Dependency assumptions

- S1 has landed canonical Role Composition and support classification.
- PRD #29, `CONTEXT.md`, `docs/domain/game-rules.md`, `docs/domain/game-rules-clarifications.md`, `docs/domain/invariants.md`, ADR-0008, ADR-0010, and ADR-0011 govern evidence, Faction semantics, and simultaneous outcome resolution.

### Verification

- **Claim:** The classifier distinguishes not-already-decided, single-Faction, and Shared Victory results solely from rule-derived Lobby Exit evidence.
  **Preferred evidence:** Deterministic classifier tests and Core integration tests using handcrafted Role Compositions with independently derived expectations.
  **Forbidden evidence:** Monte Carlo runs, sampled winner thresholds, current localized victory strings, private predicate-order assertions, or source tests.
  **Source-test allowlist needed:** no.
- **Claim:** The runtime bridge exposes its limited evidence without collapsing unsupported Faction semantics into Team.
  **Preferred evidence:** Public-boundary tests covering current-profile inputs and an unsupported-Faction boundary case.
  **Forbidden evidence:** Enum-source scans or assertions that every future Faction maps to a current Team value.
  **Source-test allowlist needed:** no.
```

### S4 - Simulated terminal lobby evaluation pipeline

```markdown
## Implementation Contract

**Drafted against:** <Stage A default-branch SHA>

### Outcome

Core exposes one shared terminal lobby-evaluation pipeline used by Build-Time Cache Generation and On-Device Fallback Generation. It returns an already-decided classification without simulation, interprets a fixed 1,000-run baseline screening batch, and produces Game Result Frequency from a fixed 10,000-run probability batch only when every required run completes.

### Acceptance criteria

- [ ] The pipeline checks S3 already-decided classification before deriving or running any Simulation Start State.
- [ ] A non-already-decided scenario runs `baseline-random-screening` for exactly 1,000 attempts using S2 evidence and the active simulator/profile version.
- [ ] Screening is degenerate only when all 1,000 runs complete and every Game Session ends in a Turn 1 Victory Check Window; one later ending means not degenerate.
- [ ] Any Incomplete Simulation Run, execution error, or cancellation prevents a terminal screening or probability result and cannot be interpreted as degenerate, balanced, No-Winner, or partial probability.
- [ ] A scenario that passes screening runs `baseline-random-probability` for exactly 10,000 attempts with the same decision behavior as screening.
- [ ] Probability aggregation uses Completed Simulation Runs as its semantic denominator and represents single-Faction, Shared Victory, and No-Winner Game Results as mutually exclusive categories.
- [ ] Output contains the complete scenario-specific Possible Game Result inventory, including zero-frequency entries, plus Game Result Frequency and Game Result Frequency by Turn and Victory Check Window.
- [ ] Game Result Frequency by Turn sums to Game Result Frequency, and the complete Game Result Frequency distribution sums to 100 percent before display rounding.
- [ ] Cancellation propagates through the shared pipeline and no partial terminal evaluation is published.

### Scope boundaries

In scope:

- Ordered terminal evaluation, fixed screening/probability profile policy, synthetic evidence interpretation, aggregation, Possible Game Result inventory, and cancellation propagation.

Out of scope:

- Cache serialization or storage, scenario enumeration, Client timeout policy, UI projection, balance recommendations, statistical intervals, live recomputation, configurable run counts, and stratified sampling.

### Dependency assumptions

- S2 has landed deterministic Simulation Result Evidence and cancellation.
- S3 has landed semantic already-decided classification.
- #46 has landed the Wild Child later-night transition fix required for a complete frozen-profile support claim.
- PRD #29, `CONTEXT.md`, `docs/domain/invariants.md`, ADR-0008, ADR-0009, and `docs/agents/qa-strategy.md` govern the layered gates and evidence interpretation.

### Verification

- **Claim:** Screening interprets all-Turn-1, later-ending, incomplete, failed, and cancelled evidence exactly as specified.
  **Preferred evidence:** Deterministic tests over handcrafted batch evidence plus a small known-oracle Core integration path, including one prevalidated degenerate scenario.
  **Forbidden evidence:** Live 1,000-run CI thresholds, arbitrary random snapshots, timing assertions, or exhaustive enumeration of degenerate setups.
  **Source-test allowlist needed:** no.
- **Claim:** Probability aggregation preserves completed-only semantics, complete Possible Game Results, Shared Victory, No-Winner, zero-frequency entries, and Turn/window totals.
  **Preferred evidence:** Deterministic tests over handcrafted Simulation Result Evidence and small golden input/output fixtures.
  **Forbidden evidence:** Live 10,000-run CI batches, expected win-rate thresholds, confidence-interval assertions, or source tests.
  **Source-test allowlist needed:** no.
- **Claim:** The public pipeline does not publish partial output after incomplete work or cancellation.
  **Preferred evidence:** Core integration tests with controlled incomplete evidence and cancellation.
  **Forbidden evidence:** Private-state inspection, wall-clock sleeps, or source scans for branch structure.
  **Source-test allowlist needed:** no.
```

### S5 - Versioned terminal lobby evaluation cache records

```markdown
## Implementation Contract

**Drafted against:** <Stage A default-branch SHA>

### Outcome

Core defines versioned semantic cache records for already-decided, degenerate, and probability terminal lobby evaluations. Bundled Simulator Cache entries and Local Fallback Cache Records share equivalent meaning, reject malformed or incompatible data, and contain only compact terminal output rather than per-run Simulation Result Evidence.

### Acceptance criteria

- [ ] A terminal record is keyed by Canonical Simulation Scenario plus simulator profile/version and carries enough artifact/schema identity for compatibility checks.
- [ ] Already-decided records contain the winning Game Result and stable reason without simulation evidence.
- [ ] Degenerate records contain the cutoff definition and aggregate outcome/ending-window conclusion evidence needed by the product without per-run records.
- [ ] Probability records contain the complete Possible Game Result inventory, Game Result Frequency, and Game Result Frequency by Turn and Victory Check Window at the settled internal precision.
- [ ] Bundled and local records round-trip to equivalent terminal-evaluation meaning through the chosen serializer/parser boundary.
- [ ] Malformed, unknown-version, and stale records are rejected as unusable rather than partially interpreted.
- [ ] Compatibility identity invalidates records when rules, Role behavior, simulator profile behavior, supported scenario scope, canonical identity, classification semantics, Game Result Frequency semantics, or Turn cutoff semantics change.
- [ ] Localization, visual presentation, and explanatory copy changes do not invalidate semantic records.
- [ ] No cache record contains Run Seed Material, per-run source records, transcripts, final-state snapshots, exception details, or performance diagnostics.

### Scope boundaries

In scope:

- Semantic record types, schema/version identity, serializers/parsers, compatibility and invalidation checks, malformed/stale rejection, and small golden fixtures.

Out of scope:

- Actual bundled/local storage access, lookup precedence, persistence orchestration, build-time enumeration, artifact packaging, remote distribution, and UI.

### Dependency assumptions

- S3 and S4 have landed all terminal evaluation variants and their semantic evidence.
- PRD #29, `CONTEXT.md`, ADR-0007, ADR-0008, ADR-0009, and `docs/agents/qa-strategy.md` govern identity, evaluation types, stable evidence, and cache boundaries.

### Verification

- **Claim:** Every terminal variant round-trips without semantic change and bundled/local forms remain equivalent.
  **Preferred evidence:** Deterministic semantic artifact tests and small golden fixtures for already-decided, degenerate, and probability records.
  **Forbidden evidence:** Byte-for-byte snapshots unless the bytes are the intentional format contract, per-run transcript fixtures, or source tests.
  **Source-test allowlist needed:** no.
- **Claim:** Incompatible, stale, and malformed records are rejected under the complete invalidation contract.
  **Preferred evidence:** Parser/compatibility tests that mutate one semantic identity dimension at a time through public APIs.
  **Forbidden evidence:** Private version-constant assertions, source scans, or tests coupled to storage paths.
  **Source-test allowlist needed:** no.
- **Claim:** App-facing records exclude stable per-run evidence and diagnostics.
  **Preferred evidence:** Semantic deserialization and public-record assertions over representative fixtures.
  **Forbidden evidence:** Raw source-text scans for property names.
  **Source-test allowlist needed:** no.
```

### S6 - Build-time Bundled Simulator Cache generation

```markdown
## Implementation Contract

**Drafted against:** <Stage A default-branch SHA>

### Outcome

Build-Time Cache Generation deterministically enumerates cacheable Simulation Scenarios for the frozen four-Role profile, invokes the shared terminal-evaluation pipeline, packages a versioned Bundled Simulator Cache artifact for the app, omits unusable results, and emits machine-readable generation diagnostics.

### Acceptance criteria

- [ ] Enumeration covers every cacheable 5-30 Player Simulation Scenario admitted by S0 and S1 for Simple Werewolf, Seer, Wild Child, and Simple Villager, without adding scenarios for unsupported Roles or rules.
- [ ] Generation invokes S4 rather than reimplementing already-decided, screening, probability, or aggregation semantics.
- [ ] Only complete, compatible terminal evaluations are written to the bundled artifact; unsupported, failed, incomplete, and cancelled scenarios are omitted and diagnosed by reason.
- [ ] The packaged artifact has stable artifact/schema/profile identity and is loadable through S5 semantic record APIs.
- [ ] Machine-readable diagnostics include generator, simulator, and profile identity; scenario counts by terminal result type; omissions grouped by reason; incomplete-run references with Run Seed Material; artifact identity, version, hash, and packaged size; and representative replay seeds for failures or suspicious outcomes.
- [ ] Artifact size, timing, memory, and instruction counts may be recorded as evidence but are not pass/fail thresholds for this issue.
- [ ] Actual generation reports are retained as build/CI or issue evidence rather than committed, except that the intentionally packaged bundled artifact is versioned with the app as required by the chosen build integration.
- [ ] No remote manifest, downloader, CDN, GitHub Pages integration, network fallback, update protocol, or hybrid distribution abstraction is introduced.

### Scope boundaries

In scope:

- Current-profile scenario enumeration, repeatable generation, omission handling, bundled artifact production/packaging, and generation diagnostics.

Out of scope:

- Full-role generation, remote distribution, Client lookup orchestration, on-device generation policy, UI, diagnostics dashboards, and artifact-size acceptance thresholds.

### Dependency assumptions

- S5 has landed versioned semantic cache records and artifact compatibility checks; its retained blocker provenance includes the complete S4/#46 simulation path.
- PRD #29, `CONTEXT.md`, ADR-0005, ADR-0008, ADR-0009, and `docs/agents/qa-strategy.md` govern profile scope, terminal generation, diagnostics, and distribution deferral.

### Verification

- **Claim:** Enumeration produces the complete cacheable current-profile scenario set and never includes unsupported scenarios.
  **Preferred evidence:** Deterministic enumeration tests through public generation APIs with independently derived boundary counts and representative scenario membership checks.
  **Forbidden evidence:** Source scans, one giant opaque snapshot, or assertions that implementation Role enumeration equals simulator support automatically.
  **Source-test allowlist needed:** no.
- **Claim:** The produced artifact is semantically loadable, version-compatible, and contains only complete terminal records.
  **Preferred evidence:** End-to-end generation plus S5 artifact loading/semantic validation using a bounded test fixture and the packaged production artifact check.
  **Forbidden evidence:** File-exists-only checks, byte snapshots without semantic parsing, or expected random win-rate thresholds.
  **Source-test allowlist needed:** no.
- **Claim:** Generation omissions and artifact provenance are auditable through the required machine-readable diagnostics.
  **Preferred evidence:** A generation diagnostic artifact attached to CI/build or issue evidence plus deterministic schema/contract tests.
  **Forbidden evidence:** Human-only log review, committed full diagnostic reports, or timing/size pass-fail gates.
  **Source-test allowlist needed:** no.
```

### S7 - Client lobby evaluation lookup and fallback orchestration

```markdown
## Implementation Contract

**Drafted against:** <Stage A default-branch SHA>

### Outcome

The Client owns a deterministic lobby-evaluation state machine that looks up a valid Bundled Simulator Cache record first, then a valid Local Fallback Cache Record, and otherwise coordinates bounded On-Device Fallback Generation for a stable simulator-supported scenario. It persists successful local terminal records, rejects stale results, and exposes state sufficient for the Lobby Exit safety gate and presentation.

### Acceptance criteria

- [ ] Each valid stable Canonical Simulation Scenario triggers immediate bundled lookup followed by local lookup, with compatibility and malformed/stale rejection delegated to S5.
- [ ] A usable bundled result wins over a usable local result for the same identity; a usable local result prevents fallback generation.
- [ ] When no usable record exists, automatic fallback waits for a 500 millisecond quiet period after the latest setup change; a Lobby Exit attempt during that period starts fallback immediately.
- [ ] Fallback runs only for simulator-supported scenarios, invokes S4, requests cancellation after 10 seconds, and persists a compact Local Fallback Cache Record only after complete success.
- [ ] Rules-invalid and app-unsupported setups do not enter evaluation; simulator-unsupported setups expose evaluation unavailable, do not invoke fallback, and do not block Lobby Exit solely because evaluation is unavailable.
- [ ] Setup changes cancel the quiet period or in-flight stale evaluation, discard its eventual result, and begin evaluation for the new stable identity.
- [ ] Failure, timeout, runtime cancellation, instruction-limit exhaustion, start-state failure, or an incomplete required batch exposes could-not-evaluate, persists no record, and releases the Lobby Exit safety gate.
- [ ] Could-not-evaluate is remembered only for the unchanged setup in the current app session; it does not auto-retry or survive app restart.
- [ ] Explicit retry is available only after failure, runs the same bounded pipeline, and closes the Lobby Exit gate while pending.
- [ ] The state machine exposes no in-progress skip or dismiss action and never publishes partial or stale terminal output.

### Scope boundaries

In scope:

- Bundled/local storage adapters, lookup precedence, stable-setup quiet period, fallback invocation, timeout and cancellation policy, local persistence, retry, failure memory, and Lobby Exit gate state.

Out of scope:

- Terminal evaluation semantics, cache schema design, build-time artifact generation, exact UI layout or prose, remote distribution, and live mid-game evaluation.

### Dependency assumptions

- S4 has landed the shared terminal-evaluation pipeline and S5 has landed semantic records and compatibility checks.
- S6 may proceed in parallel; S7 tests and implementation can use a bundled-cache adapter fixture until the production artifact is packaged.
- PRD #29, `CONTEXT.md`, ADR-0007, ADR-0008, ADR-0009, and `docs/agents/qa-strategy.md` govern scenario identity, state meanings, cache boundaries, and deterministic service evidence.

### Verification

- **Claim:** Lookup precedence, persistence, retry, failure memory, and every safety-gate transition follow the specified state machine.
  **Preferred evidence:** Deterministic Client service and adapter tests with fake cache stores, fake simulator pipeline, and fake clock.
  **Forbidden evidence:** Wall-clock delays, rendered-component tests for service behavior, source scans, or tests coupled to private state-field names.
  **Source-test allowlist needed:** no.
- **Claim:** Setup changes, timeout, and cancellation cannot publish stale or partial terminal output.
  **Preferred evidence:** Controlled concurrency tests using completion sources or equivalent deterministic fakes and observable public state.
  **Forbidden evidence:** Timing races, sleep-based tests, private task inspection, or source assertions for token usage.
  **Source-test allowlist needed:** no.
- **Claim:** Simulator-unsupported scenarios remain visibly classifiable as unavailable without invoking fallback or blocking Lobby Exit.
  **Preferred evidence:** Client orchestration tests over the public support/evaluation state boundary.
  **Forbidden evidence:** UI text assertions or equating app support with simulator support.
  **Source-test allowlist needed:** no.
```

### S8 - Pre-game Lobby Evaluation presentation and Lobby Exit gate

```markdown
## Implementation Contract

**Drafted against:** <Stage A default-branch SHA>

### Outcome

The pre-game lobby presents every S7 evaluation state in resource-backed Portuguese and integrates the Lobby Exit gate. It gives the Moderator a compact terminal summary plus access to the settled detailed Game Result Frequency information without exposing cache provenance or simulator diagnostics.

Preparation gate: before this issue receives `ready-for-agent`, choose one detailed-output interaction pattern - inline disclosure, modal, or navigation - and replace this gate with testable interaction criteria. Use a time-boxed UI spike only if direct refinement cannot choose.

### Acceptance criteria

- [ ] Pending evaluation shows a compact resource-backed Portuguese progress state, blocks Lobby Exit, and provides no skip or dismiss action.
- [ ] Already-decided presentation names and explains the pre-Turn-1 Game Result and blocks Lobby Exit.
- [ ] Degenerate presentation explains that every baseline screening game ended during Turn 1 and blocks Lobby Exit without calling the setup invalid.
- [ ] Probability presentation shows whole-percentage Game Result Frequency for the scenario's Possible Game Results and provides access to ending-frequency-by-Turn detail derived from Game Result Frequency by Turn.
- [ ] Zero-frequency results are described as not observed rather than impossible; exact results below 1 percent may be grouped by individual outcome name as possible but unlikely.
- [ ] Shared Victory Outcomes and No-Winner Outcome appear as their own mutually exclusive Game Results rather than credited-Faction rows.
- [ ] Simulator-unsupported state makes evaluation unavailability visible while allowing Lobby Exit.
- [ ] Could-not-evaluate state allows Lobby Exit and exposes retry; retry returns presentation to the pending gated state.
- [ ] Setup changes cannot leave stale summary, detail, failure, or gate state visible for the previous scenario.
- [ ] A brief finite-baseline caveat may appear in detailed output, but the UI does not show PMF/CDF language, confidence intervals, margins of error, Reference Turn Horizon, balance verdicts, recommendations, run controls, cache provenance, or diagnostics.
- [ ] All visible strings are resource-backed Portuguese; exact prose is not frozen by the contract.

### Scope boundaries

In scope:

- Rendered lobby states, terminal summary/detail access, retry interaction, accessibility semantics, and Lobby Exit integration.

Out of scope:

- Evaluation orchestration, cache/storage behavior, exact probability computation, remote distribution, live-game UI, and generic charting or diagnostics frameworks.

### Dependency assumptions

- S7 has landed the complete public evaluation and gate state machine.
- Product behavior is defined by PRD #29 and terminology by `CONTEXT.md`; ADR-0008 and ADR-0009 define state/evidence boundaries.
- The detailed-output interaction remains unresolved until preparation and therefore prevents current readiness.

### Verification

- **Claim:** The Moderator sees the correct state, information, actions, and Lobby Exit behavior for pending, already-decided, degenerate, probability, unavailable, and could-not-evaluate states.
  **Preferred evidence:** bUnit rendered component tests against the shared RCL using S7 fakes, DOM/attribute/event assertions, and production localization accessors.
  **Forbidden evidence:** Raw Razor source assertions, hardcoded localized copy, CSS-class snapshots, or service tests used as proof of rendered behavior.
  **Source-test allowlist needed:** no.
- **Claim:** Probability summary/detail preserves complete Game Result meaning, zero-frequency wording, Turn detail, and prohibited-output boundaries.
  **Preferred evidence:** bUnit rendered tests with handcrafted terminal-evaluation fixtures and production-derived resource values.
  **Forbidden evidence:** Random simulator runs, screenshot baselines as sole semantic evidence, or source scans.
  **Source-test allowlist needed:** no.
- **Claim:** The chosen detailed-output interaction is readable, non-overlapping, focusable, and usable at representative mobile and desktop viewports.
  **Preferred evidence:** Local Browser QA Host inspection with screenshots, DOM/computed-style checks, focus/scroll observations, and recorded claim/environment/result artifacts.
  **Forbidden evidence:** Treating visual inspection as a replacement for deterministic component behavior tests or adding a required broad visual-regression CI gate.
  **Source-test allowlist needed:** no.
```

### #46 - Wild Child random headless games throw on later-night state transition

```markdown
## Implementation Contract

**Drafted against:** <Stage A default-branch SHA>

### Outcome

Wild Child and any equivalent standard night Role with no later-night power can complete the later-night headless transition directly to asleep without violating the declared Role state machine. The diagnosed deterministic random-play path no longer throws while existing Wild Child model-elimination transformation behavior remains intact.

### Acceptance criteria

- [ ] The standard night-role initial stage accepts both the wake path and the direct-asleep path required when a Role has no action that night.
- [ ] The diagnosed Wild Child later-night path replays from fixed S2 Run Seed Material without the invalid `AwaitingAwakeConfirmation` to `Asleep` transition exception.
- [ ] The regression test asserts the transition behavior and absence of the diagnosed exception, not the eventual winning Faction.
- [ ] Existing Wild Child behavior still changes Faction state when the selected model is Eliminated.
- [ ] Existing approved headless spike behavior remains covered.

### Scope boundaries

In scope:

- The shared standard night-role state-machine declaration needed by Wild Child's later-night no-power path and its narrow deterministic regression coverage.

Out of scope:

- Probability aggregation, cache generation, a 1,000-game random CI batch, Team-to-Faction migration, unrelated listener families with no concrete affected Role, and Client changes.

### Dependency assumptions

- S2 has landed baseline-random execution, Run Seed Material, replay plumbing, and stable minimal evidence.
- PRD #29, ADR-0005, and `docs/agents/qa-strategy.md` govern engine reuse and regression-seed evidence.

### Verification

- **Claim:** The diagnosed later-night Wild Child path can transition directly to asleep and no longer throws.
  **Preferred evidence:** A deterministic Core integration regression test replayed from the fixed Run Seed Material or the smallest equivalent known-oracle scenario.
  **Forbidden evidence:** A stochastic 1,000-game pass rate, an asserted eventual winner, wall-clock performance, or source scans over allowed-state declarations.
  **Source-test allowlist needed:** no.
- **Claim:** Existing Wild Child model-elimination transformation and approved headless execution behavior remain intact.
  **Preferred evidence:** Existing public Core integration tests plus a focused regression assertion where coverage is missing.
  **Forbidden evidence:** Private listener-state inspection or broad transcript snapshots.
  **Source-test allowlist needed:** no.
```

## Topic 12 Execution Checklist

1. Finish the pre-migration durable-document edits, commit the current branch, merge it to the default branch, verify the referenced files exist there, and capture the resulting default-branch commit SHA. Keep this handoff and `docs/loose-ends.md` during this stage.
2. Substitute that SHA into the work-item payloads during publication. Rewrite and rename PRD #29 from the payload above, then rename milestone 2 and assign #29, S0-S8, and #46 to it.
3. Create S0-S8 with their category labels and provisional contracts, then make S0-S8 and #46 direct children of #29.
4. Refresh #46 with its canonical contract, remove stale #38 dependency language, and keep its `bug` category.
5. Establish the exact native blocker graph from Batch 2 and verify parent, blocker, milestone, label, body, and state invariants for every active item.
6. Permanently delete #42 and #43. Leave #38-#41 and #59-#60 closed with their existing supersession comments and historical parent/milestone relationships.
7. Remove `ready-for-agent` from closed #30. Prepare S0 against the landed default branch, update its anchor if needed, and add `ready-for-agent` as the final tracker mutation. Do not add readiness to any other item.
8. Verify PRD #29 has exactly the expected active children, historical closed children, milestone, title, and body; verify the only executable frontier is S0.
9. Replace temporary references in `CONTEXT.md` and `docs/domain/invariants.md` with durable homes: product presentation in PRD #29 and work-item contracts, and deferred cache distribution in ADR-0009. Confirm no durable document cites this handoff or `docs/loose-ends.md`.
10. Delete `docs/loose-ends.md` and `docs/handoff.md`, run documentation and repository verification, and land the cleanup commit.

Topic 12 is complete and the shared understanding is confirmed. No tracker mutation was performed during the grilling session.
