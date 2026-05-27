# Issue 29 Simulator Handoff

Date: 2026-05-26

This handoff captures the design grilling session about stale PRD #29, especially the shift from on-device-first pre-game simulation to cache-first pre-game Role Composition lookup. It does not replace the settled glossary in `CONTEXT.md`; use that file as the source of truth for domain terms.

## Starting Problem

PRD #29 had grown stale around the Win Probability Simulator. The current investigation in `docs/artifacts/issue-29-merged-investigation.md` recommended tightening several contracts before implementation, but the main design question reopened here was whether pre-game results should be computed on device with repeat caching, or whether the app should be cache-first for normal pre-game UX.

The user argued that precomputing valid Role Compositions may be tractable because real games have strong validity and playability constraints, especially once player count is capped. The goal became: define the cache-first product contract defensibly without inventing fragile balance heuristics.

## Resolved Decisions

1. Cache-first applies only to normal pre-game UX. On-device simulation still exists as fallback, implementation substrate, and QA evidence.

2. The app-wide Supported Player Count is 5-30 Players. Product docs may still describe 8-20 as the practical ergonomic sweet spot for physical play.

3. Use **Role Composition** as the canonical term for the pre-game multiset of Role cards, independent of Player assignment or Seating Order.

4. The app should not try to decide whether a Role Composition is generally balanced. It surfaces pre-game Faction probabilities; the Moderator judges balance.

5. The app blocks only two categories: **Already-Decided Role Composition** and **Degenerate Role Composition**.

6. **Already-Decided Role Composition** means a Faction's win condition is already satisfied before Turn 1 begins and before any setup or Night 1 choices.

7. Already-decided detection is rule-based and does not run simulation.

8. **Degenerate Role Composition** means a legal Role Composition whose 1,000-run baseline screening simulation only observes Game Sessions ending by the end of Turn 1.

9. A Turn 1 ending is a completed game outcome, not a simulation failure.

10. Degenerate classification is practical product screening, not mathematical proof over every possible branch.

11. Do not use percentage thresholds such as 50%, 80%, or 90% for degenerate blocking. The screen is defensive: all 1,000 screening runs ended by Turn 1.

12. Use a layered simulation pipeline:
    - 1,000-run `baseline-random-screening` batch for validity screening.
    - 10,000-run `baseline-random-probability` batch only for Role Compositions that pass screening.

13. `baseline-random-screening` and `baseline-random-probability` use the same decision behavior. They differ by profile name, run count, and result interpretation.

14. Offline cache records should have three result types:
    - `already-decided`: no simulation; records the winning Faction and reason.
    - `degenerate`: screening observed only Turn 1 endings; records evidence summary.
    - `probability`: passed screening; records 10,000-run PMF/CDF/overall probabilities.

15. Do not store probability records for already-decided or degenerate Role Compositions. Store classification records instead.

16. **Balanced Role Composition** remains a domain concept for similar starting Faction win probabilities under the baseline decision model, but the app does not block on that.

17. **Initial Faction Count** is the denominator concept for pre-game balance discussions: count starting win-condition beneficiaries, not every conditional outcome the simulator may later produce.

18. Initial Faction Count excludes latent or transient Factions such as cross-team Lovers and Angel's early solo win condition.

19. **Reference Turn Horizon** was retained only as a dormant descriptive metric: `Player count / Initial Faction Count`. It is not part of degenerate blocking and should not be added to lobby UI yet.

20. Angel is special because it has a transient solo win condition and then falls back into the Villager Faction. Do not let Angel inflate Initial Faction Count.

21. Cross-team Lovers are conditional/latent. Track Lovers outcomes if they occur, but do not include them in Initial Faction Count.

22. Prejudiced Manipulator has setup-dependent balance. For now, simplify cache generation by assuming an even public group split.

23. Run reproducibility uses **Run Seed Material**: a canonical string stored for replay evidence, hashed only at the random-generator boundary.

24. Run Seed Material includes simulator version, profile/strategy, run number, and canonical Role Composition. Example:

```text
sim-v1|baseline-random-screening|run=17|Seer-1,SimpleVillager-7,SimpleWerewolf-2,WildChild-1
```

25. Run numbers are 1-based within a batch. A single-run simulation uses `run=1`; a 1,000-run batch uses `run=1` through `run=1000`.

26. Canonical Role Composition segments include only non-zero Role counts, sorted by exact enum name, not localized display name or UI insertion order.

27. Cache keys use the same canonical Role Composition rule, but identify the profile and run count rather than an individual run. Examples:

```text
sim-v1|baseline-random-screening|runs=1000|Seer-1,SimpleVillager-7,SimpleWerewolf-2,WildChild-1
sim-v1|baseline-random-probability|runs=10000|Seer-1,SimpleVillager-7,SimpleWerewolf-2,WildChild-1
```

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

- `CONTEXT.md`: added and clarified Supported Player Count, Role Composition, Actor Setup Cards, Already-Decided Role Composition, Degenerate Role Composition, Balanced Role Composition, Faction lifecycle terms, Initial Faction Count, Reference Turn Horizon, and Run Seed Material.
- `docs/game-rules.md`: clarified Thief undealt-card behavior and Actor set-aside card constraints.

## Follow-Up Leads

1. Update PRD #29 to replace stale on-device-first/cache-as-repeat language with cache-first pre-game Role Composition lookup and on-device fallback.

2. Update #38/#39/#40/#41/#59/#60 or create replacement issues so they reflect the new layered cache pipeline rather than the stale issue split.

3. Decide whether this change deserves an ADR. It may, because cache-first pre-game lookup plus on-device fallback is a meaningful architectural/product trade-off against the current PRD wording.

4. Implement a shared Role Composition canonicalizer. It must sort exact enum names, include non-zero counts only, and be reused by cache keys and Run Seed Material.

5. Add app-wide max player validation of 30 in Core and update product docs to distinguish supported range from practical sweet spot.

6. Define the already-decided pre-Turn-1 win-condition check for current two-Faction runtime first, then extend as Faction support expands.

7. Design offline cache generation records: `already-decided`, `degenerate`, and `probability`, with versioned schema and simulator/profile identifiers.

8. Define deterministic seed hashing at the last-mile PRNG boundary. Store canonical Run Seed Material as string evidence.

9. Clarify remaining setup-dependent assumptions:
   - Prejudiced Manipulator uses even split for now.
   - Thief extra undealt Character Cards are part of Role Composition.
   - Actor Setup Cards are a separate setup artifact, not part of Role Composition, and need their own future UI/UX flow.
   - Wolf Hound choice is simulated and does not help satisfy hard-aligned Role requirements.

10. Clarify open rule ambiguities before full-suite implementation:
    - Angel cutoff timing.
    - Piper "all surviving players" versus "cannot charm self".
    - Lovers involving Loners, infected players, Wolf Hound, or Double Agent.
    - Infection overriding solo or Lovers objectives.

11. Keep `Reference Turn Horizon` out of UI until there is a clear Moderator action attached to it.

12. Suggested next skills:
    - `grill-with-docs` if continuing to sharpen PRD/ADR language.
    - `to-issues` if converting this design into GitHub issues.
    - `triage` if updating issue labels/briefs.
    - `tdd` when implementing canonicalization, cache record types, and simulator screening.
