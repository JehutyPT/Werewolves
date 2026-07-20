# Domain invariants

This document records stable domain rules that implementation and tests can assert without carrying the rationale for why those rules were chosen. Use `CONTEXT.md` for vocabulary, `docs/domain/game-rules-clarifications.md` for rule disambiguation, and `docs/adr/` for architectural tradeoffs.

## Role composition

- A Role Composition is the pre-game multiset of physical Role cards selected for the deck.
- A Role Composition contains one Role card per Player, plus Thief's two extra Character Cards when Thief is present.
- Actor Setup Cards are setup artifacts, not part of the Role Composition.
- New Moon Events, Player names, Seating Order, Status Effects, Sheriff, Lovers, Charmed state, Prejudiced Manipulator groups, and physical traits are outside the Role Composition.
- Supported Player Count caps Players, not total physical cards.
- Rules-Valid Role Compositions require hard-aligned Villager and hard-aligned Werewolf coverage; Simple Villager and Simple Werewolf are not mandatory by role name.

## Live physical setup authority

- The Moderator selects and records the live Role Composition; the app validates it and never generates it.
- Players perform the Physical Deal. The live app never shuffles, deals, randomly assigns, or deduces an unknown Player-specific Role from missing information.
- Thief's undealt cards are physical results recorded by the Moderator and validated against the Role Composition; the app does not preselect them or the Thief Player's choice.
- Actor Setup Cards are face-up cards chosen by the Moderator and recorded by the app; the app never generates the live inventory.
- The Public Group Partition is created and publicly announced by the Moderator; the live app validates but never balances, defaults, or generates it.
- Role Composition, Actor Setup Cards, and Public Group Partition are lobby/configuration inputs; they do not imply a Core Setup phase or setup-time Moderator Instruction.
- Rehydration restores only confirmed live setup facts and never fills missing facts from simulator state or profile defaults.

## Character Cards, Role knowledge, and reveal

- Physical Character Card Ownership, current Role, Moderator knowledge, and public reveal are separate facts.
- A live Game Session knows the Role Composition but does not initially know which Player owns each Character Card or current Role.
- Initial current Role comes from the dealt Character Card. A Permanent Role Swap changes current Role and separately defines physical-card handling and visibility.
- Role Identification records a private exact-Role observation and never assigns a Character Card or publicly reveals the Role.
- Faction Agent Group Observation records operational group membership and cannot identify or mutate an exact Role.
- Role Reveal always records a public physical event and public-knowledge transition, even when the current Role was already Moderator-known; it never changes the current Role.
- For a pending Dawn or Vote victim, any explicit pre-reveal interception runs first, generic public reveal commits second, and the actual Elimination or replacement effect commits third; the resulting Elimination Cascade then drains before navigation.
- A known-role reveal uses a Continue acknowledgment after the physical event. An unknown-role reveal supplies a complete mapping for exactly the requested Players.
- Every recorded identification, reveal, card-zone fact, and mapping must respect Role Composition multiplicity, one-to-one Player/card ownership, unique physical card instances, prior observations, and confirmed dealt or undealt zones.
- Exact-Role identification cardinality follows the active dealt card zone, never Role Composition count alone. The Thief undealt/no-holder rules remain an explicit #147 decision before affected calls are implemented.
- An unidentified Role is not implicitly Simple Villager and supplies no guessed Faction Beneficiary, Faction Agent, trigger, or outcome fact.
- The rules give every Player one actual Faction Beneficiary, but persisted live Known Faction State may be unknown. Beneficiary and Agent queries return an unresolved result until an observation or Core-authored transition establishes the required fact; they never substitute a default or infer from remaining inventory.
- A rule that requires an unresolved Role fact must obtain an observed response before resolving; it cannot guess from remaining inventory or proceed from a default allegiance. A Core-authored transition may commit a new known Role only when an explicit rule requires it.
- Public announcements, public logs, and public roster projections use only publicly revealed facts.
- A Permanent Role Swap preserves prior public history and explicitly defines whether the new current Role is private or public.

## Simulation input

- A Simulation Scenario is the pre-game simulator/cache input boundary.
- A Simulation Scenario includes Player count, Canonical Role Composition, and setup artifacts or non-default assumptions that affect simulation.
- A Simulation Start State is the fully defined state from which a simulator batch runs.
- Pre-game simulation derives each seeded Simulation Start State from the Simulation Scenario.
- Mid-game projection uses the same simulation mechanism from a later fully defined Game Session state.
- Seeded synthetic assignment, undealt cards, and profile-default setup exist only inside Simulation Start State and never populate a live Game Session.

## Simulator capability

- Safety-screening Role admission and dormant full-probability Role admission are explicit, separate, and versioned.
- The Full-Probability Role Set is a subset of the Safety-Screening Role Set because full evaluation includes the same earlier gates; Safety-Screening membership does not imply Full-Probability membership.
- Scenario support, setup support, headless-response policy, compatibility identity, lookup, and stale-record rejection are qualified by the requested Simulator Capability.
- A Role admitted to safety screening is not thereby supported for full probability evaluation, build-time enumeration, bundled-cache generation, probability output, or broader strategy use.
- Safety screening completes only when all 1,000 attempted runs complete; any Incomplete Simulation Run produces Could Not Evaluate.
- Already-Decided and Degenerate results may persist as compact terminal records. A successful non-degenerate screening pass is session-local and is not persisted.
- Compatibility identity changes whenever Role admission, setup defaults, Role or outcome semantics, or baseline response behavior can change screening evidence.
- Stale or capability-mismatched terminal records are rejected; the existing probability bundle is not silently re-keyed for a different safety capability.
- A legacy `core-simulator@1` probability record may satisfy the current `safety-screening@<version>` capability only through an explicit bridge for a scenario supported by both that legacy producer/profile and the current Safety-Screening capability with unchanged Role, setup, outcome, and headless-response semantics.
- Role support v1 does not require a 10,000-run probability batch, Bundled Simulator Cache generation, probability records, or probability output.

## Faction state

- Each Player has exactly one Faction Beneficiary at a time in the current ruleset.
- A live Game Session stores whether each Faction Beneficiary and Faction Agent fact is unknown or legitimately known; domain truth and app knowledge are not the same thing.
- A rules step that requires an unknown Faction fact must pause and obtain it before resolving. #147 settles the exact private observation contract and #120 implements the shared correlated runtime exchange; unknown never means Villager, non-Agent, or any other default.
- A Player can be a Faction Agent for one Faction while benefiting from another Faction's win condition.
- Infection changes Faction Agent status, not Faction Beneficiary.
- Permanent Role Swaps change the Player's Faction Beneficiary to the new Role's default Faction unless an explicit precedence rule says otherwise.
- Ambiguous Roles default to Villager Faction beneficiaries unless their Role definition explicitly says otherwise.
- Loner Roles do not share a Loner Faction; each Loner Role defines its own Faction lifecycle.

## Outcome resolution

- Win conditions are evaluated only during Victory Check Windows.
- All win-condition predicates in one Victory Check Window are evaluated against the same resolved Game Session state.
- Multiple true win-condition predicates in the same Victory Check Window produce a Shared Victory Outcome.
- A No-Winner Outcome occurs only when no Faction win condition is true and every Player is Eliminated.
- A completed Game Session ends with exactly one Game Session Outcome.

## Simulation evidence

- A Completed Simulation Run reaches a Game Session Outcome.
- An Incomplete Simulation Run does not reach a Game Session Outcome and does not contribute to Game Result Frequency.
- Game Result Frequency is computed only from Completed Simulation Runs.
- Shared Victory Outcomes and No-Winner Outcomes are Game Results, not side channels.
- "Could not evaluate" is a product evaluation state, not a Game Session Outcome or Game Result.
