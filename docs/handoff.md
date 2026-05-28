# Issue 29 Simulator Handoff

Date: 2026-05-28

This handoff captures the design grilling session about stale PRD #29, especially the shift from on-device-first pre-game simulation to cache-first pre-game Role Composition lookup. It does not replace the settled glossary in `CONTEXT.md`; use that file as the source of truth for domain terms.

## Starting Problem

PRD #29 had grown stale around the Win Probability Simulator. The current investigation in `docs/artifacts/issue-29-merged-investigation.md` recommended tightening several contracts before implementation, but the main design question reopened here was whether pre-game results should be computed on device with repeat caching, or whether the app should be cache-first for normal pre-game UX.

The user argued that precomputing valid Role Compositions may be tractable because real games have strong validity and playability constraints, especially once player count is capped. The goal became: define the cache-first product contract defensibly without inventing fragile balance heuristics.

## Staged Grilling Progress

The design follow-up is being handled as a staged grilling sequence. Each branch should be treated as settled input for later branches once the user has reviewed and committed that branch's diff.

1. **Faction Model** was completed by the first sub-agent and committed as `c103d1f Document faction model terminology`.

2. **Win Condition Semantics** was completed by the second sub-agent. The user provided the summary below and asked to proceed to Role Composition Space.

3. **Role Composition Space** was completed inline using `$grill-with-docs-batched` after the user clarified that this branch should not be offloaded to another sub-agent.

The planned branch order after Role Composition Space is:

1. Already-Decided / Degenerate Classification
2. Simulation Result Contract
3. Cache Artifact Design

## Resolved Decisions

1. Cache-first applies only to normal pre-game UX. On-device simulation still exists as fallback, implementation substrate, and QA evidence.

2. The app-wide Supported Player Count is 5-30 Players. Product docs may still describe 8-20 as the practical ergonomic sweet spot for physical play.

3. Use **Role Composition** as the canonical term for the pre-game multiset of Role cards, independent of Player assignment or Seating Order.

4. The app should not try to decide whether a Role Composition is generally balanced. It surfaces pre-game Faction probabilities; the Moderator judges balance.

5. The app blocks only two categories: **Already-Decided Role Composition** and **Degenerate Simulation Scenario**.

6. **Already-Decided Role Composition** means a Faction's win condition is already satisfied before Turn 1 begins and before any setup or Night 1 choices.

7. Already-decided detection is rule-based and does not run simulation.

8. **Degenerate Simulation Scenario** means a legal, supported Simulation Scenario whose 1,000-run baseline screening simulation only observes Game Sessions ending by the end of Turn 1.

9. A Turn 1 ending is a completed game outcome, not a simulation failure.

10. Degenerate classification is practical product screening, not mathematical proof over every possible branch.

11. Do not use percentage thresholds such as 50%, 80%, or 90% for degenerate blocking. The screen is defensive: all 1,000 screening runs ended by Turn 1.

12. Use a layered simulation pipeline:
    - 1,000-run `baseline-random-screening` batch for validity screening.
    - 10,000-run `baseline-random-probability` batch only for Simulation Scenarios that pass screening.

13. `baseline-random-screening` and `baseline-random-probability` use the same decision behavior. They differ by profile name, run count, and result interpretation.

14. Offline cache records should have three result types:
    - `already-decided`: no simulation; records the winning Faction and reason.
    - `degenerate`: screening observed only Turn 1 endings; records evidence summary.
    - `probability`: passed screening; records 10,000-run PMF/CDF/overall probabilities.

15. Do not store probability records for already-decided Role Compositions or degenerate Simulation Scenarios. Store classification records instead.

16. **Balanced Role Composition** remains a domain concept for similar starting Faction win probabilities under the baseline decision model, but the app does not block on that.

17. **Initial Faction Count** is the denominator concept for pre-game balance discussions: count starting win-condition beneficiaries, not every conditional outcome the simulator may later produce.

18. Initial Faction Count excludes latent or transient Factions such as cross-team Lovers and Angel's early solo win condition.

19. **Reference Turn Horizon** was retained only as a dormant descriptive metric: `Player count / Initial Faction Count`. It is not part of degenerate blocking and should not be added to lobby UI yet.

20. Angel is special because it has a transient solo win condition and then falls back into the Villager Faction. Do not let Angel inflate Initial Faction Count.

21. Cross-team Lovers are conditional/latent. Track Lovers outcomes if they occur, but do not include them in Initial Faction Count.

22. Prejudiced Manipulator has setup-dependent balance. The baseline simulator profile defaults to an even public group split; only non-default group models need explicit Simulation Scenario material.

23. Run reproducibility uses **Run Seed Material**: a canonical string stored for replay evidence, hashed only at the random-generator boundary.

24. Run Seed Material includes simulator version, profile/strategy, run number, Player count, canonical Role Composition, and any non-default Simulation Scenario assumptions. Example:

```text
sim-v1|baseline-random-screening|players=10|run=17|Seer-1,SimpleVillager-7,SimpleWerewolf-2,WildChild-1
```

25. Run numbers are 1-based within a batch. A single-run simulation uses `run=1`; a 1,000-run batch uses `run=1` through `run=1000`.

26. Canonical Role Composition segments include only non-zero Role counts, sorted alphabetically by exact enum name, not localized display name or UI insertion order.

27. Cache keys use the same canonical Role Composition rule, but identify the profile and run count rather than an individual run. Examples:

```text
sim-v1|baseline-random-screening|players=10|runs=1000|Seer-1,SimpleVillager-7,SimpleWerewolf-2,WildChild-1
sim-v1|baseline-random-probability|players=10|runs=10000|Seer-1,SimpleVillager-7,SimpleWerewolf-2,WildChild-1
```

## Topic 1: Faction Model Settlements

Topic 1 added and clarified these domain concepts in `CONTEXT.md`:

- **Starting Faction**, **Possible Faction**, **Transient Faction**, and **Latent Faction** describe Faction lifecycle in simulations and probability output.
- **Hard-Aligned Role** is a Role whose default allegiance is fixed by its Role, as opposed to setup/runtime choices.
- **Minimum Viable Role Composition** requires at least one hard-aligned Villager Role and at least one hard-aligned Werewolf Role.
- **Cross-Faction Lovers** are a distinct Faction outcome; same-Faction Lovers are not.
- **Actor Setup Cards** are setup artifacts, not part of Role Composition.

Topic 1 also clarified `docs/game-rules.md`:

- Thief sees undealt cards after random distribution; cards are not planned or set aside in advance.
- Actor Setup Cards must be eligible hard-aligned Villager Roles and do not transfer win conditions.

Role Composition now includes Thief undealt cards and excludes Actor Setup Cards. Probability output should list every Possible Faction, including rows that end at 0%.

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
- Faction Win Probability credits every winning Faction in shared wins; Exclusive Outcome Share preserves mutually exclusive outcome tuples.
- Probability output includes every Possible Faction, even 0% / never-came-into-being rows, plus No-Winner.

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
- **Cacheable Simulation Scenario** is simulator-supported and eligible for offline cache generation.
- Classification order is: Rules-Valid Role Composition, App-Supported Role Composition, Simulator-Supported Simulation Scenario, Already-Decided Role Composition, Degenerate Simulation Scenario, then probability simulation.

Topic 3 also clarified role-set and canonicalization language:

- Use **Rules Role Set**, **Implemented Role Set**, **Simulator Profile Role Set**, and **Selectable Role Set** instead of overloaded "supported roles."
- Hard-aligned coverage replaces mandatory Simple Villager/Simple Werewolf by role name. Simple Villager and Simple Werewolf may be 0 in the full-suite domain model as long as hard-aligned Villager and Werewolf coverage exists.
- Canonical Role Composition omits zero-count Roles, uses exact enum identifiers, sorts alphabetically by enum identifier, and never includes Actor Setup Cards.
- Canonical Simulation Scenario includes `players=N` separately because Thief makes Role card count differ from Player count.

## Faction Inventory Notes

A read-only explorer produced this high-level faction inventory from `docs/game-rules.md`:

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
- `CONTEXT.md`: later branches added Faction Beneficiary, Faction Agent, win-condition outcome terminology, and related probability-output terms.
- `docs/game-rules.md`: clarified Thief undealt-card behavior, Actor Setup Card constraints, win-condition semantics from Topic 2, and Town Crier as a New Moon Assignment rather than a Role Composition Role.
- `docs/loose-ends.md`: records implementation follow-ups for Actor Setup Card validation and hard-aligned coverage validation.

## Follow-Up Leads

1. Update PRD #29 to replace stale on-device-first/cache-as-repeat language with cache-first pre-game Role Composition lookup and on-device fallback.

2. Update #38/#39/#40/#41/#59/#60 or create replacement issues so they reflect the new layered cache pipeline rather than the stale issue split.

3. Decide whether this change deserves an ADR. It may, because cache-first pre-game lookup plus on-device fallback is a meaningful architectural/product trade-off against the current PRD wording.

4. Implement a shared Role Composition canonicalizer. It must sort exact enum names alphabetically, include non-zero counts only, include Thief extras, exclude Actor Setup Cards, and be reused by cache keys and Run Seed Material.

5. Add app-wide max player validation of 30 in Core and update product docs to distinguish supported range from practical sweet spot.

6. Define the already-decided pre-Turn-1 win-condition check using the Topic 2 Faction Beneficiary semantics. The current runtime still only supports two `Team`s, so implementation should bridge carefully rather than pretending full Faction support already exists.

7. Design offline cache generation records: `already-decided`, `degenerate`, and `probability`, with versioned schema and simulator/profile identifiers. The `degenerate` record applies to a Simulation Scenario, not just a Role Composition.

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
    - `grill-with-docs-batched` for the remaining staged grilling branches.
    - `grill-with-docs` only when a branch has one central blocking question.
    - `to-issues` if converting this design into GitHub issues.
    - `triage` if updating issue labels/briefs.
    - `tdd` when implementing canonicalization, cache record types, and simulator screening.
