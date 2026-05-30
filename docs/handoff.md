# Issue 29 Simulator Handoff

Date: 2026-05-28

This handoff captures the design grilling session about stale PRD #29, especially the shift from on-device-first pre-game simulation to cache-first pre-game Role Composition lookup. It does not replace the settled glossary in `CONTEXT.md`; use that file as the source of truth for domain terms.

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

Cache distribution remains deliberately unresolved. A bundled simulator cache is acceptable only if the full-role artifact is negligible in app-package terms; the current rough comfort range is under 5-10 MB, with the upper end already feeling expensive. If the complete cache grows beyond that range, a static remote cache, such as one hosted through GitHub Pages, is a live alternative.

On-device fallback remains mandatory regardless of the cache distribution shape. The final bundled-versus-remote decision should wait until the simulator, implemented role catalog, cache schema, and cache generation are far enough along to measure realistic artifact size.

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
    - Incomplete screening, build-time cache-generation errors, and incomplete on-device fallback generation are "could not evaluate" states; they are not degenerate and do not block Lobby Exit.

22a. Build-Time Cache Generation means producing Bundled Simulator Cache artifacts outside the Moderator's phone, such as on a development machine, CI worker, or backend job. It must not mean trying to enumerate every cacheable scenario on the Moderator's phone.

22b. On-device fallback generation is allowed only after the Bundled Simulator Cache has no usable lobby evaluation for the selected Simulation Scenario. It may produce a usable local already-decided, degenerate, or probability evaluation only if the same classification pipeline completes successfully.

22c. Failed, incomplete, or operationally suspect generation attempts are omitted from the Bundled Simulator Cache. Any logs for those attempts are implementation/build concerns, not part of the domain cache contract.

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

22i. Bundled Simulator Cache entries are invalidated by changes to rule interpretation, Role behavior, simulator profile behavior, supported scenario scope, Canonical Simulation Scenario construction, already-decided or degenerate classification semantics, Game Result Frequency semantics, or Turn cutoff semantics. Localization, visual presentation, and explanatory copy changes do not invalidate cache entries.

22j. On-device fallback generation only applies when the selected Simulation Scenario is simulator-supported by the current profile. Cache misses and failed fallback generation are nonblocking "could not evaluate" states and must not make balance, already-decided, or degenerate claims.

22k. Simulator profile/version identifies already-decided cache compatibility; it is not evidence that simulation ran. Already-decided evidence remains Role Composition classification only.

22l. Probability cache entries are coherent only when Game Result Frequency and Game Result Frequency by Turn describe the same Game Result set. Game Result Frequency is the row-sum projection of Game Result Frequency by Turn across Turns and Victory Check Windows.

22m. Game Result evidence in a degenerate cache entry explains the Turn 1 endings; it is not presented as balance probability.

22n. Domain docs define Bundled Simulator Cache entries semantically. Serialized schema, file format, compression, and lookup/index layout are implementation concerns.

23. Already-decided classification only uses Faction victory evidence available at Lobby Exit. Degenerate and probability evidence can observe Starting, Possible, Transient, and Latent Factions through completed simulation outcomes.

24. The full Faction model remains the domain contract, but the active Simulator Profile Role Set controls which Simulation Scenarios can be evaluated today. If the current runtime cannot evaluate a full Faction trigger or scenario, it is not simulator-supported/cacheable or it becomes a nonblocking "could not evaluate" state; it must not be mislabeled as already-decided or degenerate.

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

## Follow-Up Leads

1. Update PRD #29 to replace stale on-device-first/cache-as-repeat language with cache-first pre-game Role Composition lookup, on-device fallback, and the Topic 7 Game Result Frequency probability-output contract.

2. Update #38/#39/#40/#41/#59/#60 or create replacement issues so they reflect the new layered cache pipeline and probability-output contract rather than the stale issue split.

3. Defer the cache distribution ADR until realistic full-role cache size is measurable. Bundled cache, static remote cache, and any hybrid distribution choice are still open; on-device fallback is not open.

4. Implement a shared Role Composition canonicalizer. It must sort exact enum names alphabetically, include non-zero counts only, include Thief extras, exclude Actor Setup Cards, and be reused by cache keys and Run Seed Material.

5. Add app-wide max player validation of 30 in Core and update product docs to distinguish supported range from practical sweet spot.

6. Define the already-decided pre-Turn-1 win-condition check using the Topic 2 Faction Beneficiary semantics. The current runtime still only supports two `Team`s, so implementation should bridge carefully rather than pretending full Faction support already exists.

7. Implement Bundled Simulator Cache terminal lobby evaluations for already-decided, degenerate, and probability results, preserving the semantic contract without putting per-run simulation evidence in the app-facing cache.

8. Define deterministic seed hashing at the last-mile PRNG boundary. Store canonical Run Seed Material as string evidence.

9. Address Role Composition implementation loose ends:
   - Actor currently adds `+3` roles in `GameSessionConfig`, but the settled domain model says Actor Setup Cards are outside Role Composition.
   - Actor role selection must leave at least three eligible Actor Setup Cards outside the Role Composition.
   - Current code requires `SimpleVillager >= 1` and `SimpleWerewolf >= 1`, but the settled full-suite domain model requires hard-aligned Villager/Werewolf coverage instead.
   - Current code groups Actor as `RoleGroup.Ambiguous`, but the settled domain model treats Actor as hard-aligned Villager.

10. Clarify open rule ambiguities before full-suite implementation:
    - Angel cutoff timing.
    - Piper "all surviving players" versus "cannot charm self".
    - Any remaining Lovers edge cases not covered by Topic 2 precedence rules.
    - Any remaining New Moon event effects that alter durable voting power, Faction Agent status, or win-condition checks.

11. Keep `Reference Turn Horizon` out of UI until there is a clear Moderator action attached to it.

12. Suggested next skills:
    - `to-issues` if converting this design into GitHub issues.
    - `triage` if updating issue labels/briefs.
    - `tdd` when implementing canonicalization, cache artifacts, and simulator screening.
