# Domain invariants

This document records stable domain rules that implementation and tests can assert without carrying the rationale for why those rules were chosen. Use `CONTEXT.md` for vocabulary, `docs/domain/game-rules-clarifications.md` for rule disambiguation, and `docs/adr/` for architectural tradeoffs.

## Role composition

- A Role Composition is the pre-game multiset of physical Role cards selected for the deck.
- A Role Composition contains one Role card per Player, plus Thief's two extra Character Cards when Thief is present.
- Actor Setup Cards are setup artifacts, not part of the Role Composition.
- New Moon Events, Player names, Seating Order, Status Effects, Sheriff, Lovers, Charmed state, Prejudiced Manipulator groups, and physical traits are outside the Role Composition.
- Supported Player Count caps Players, not total physical cards.
- Rules-Valid Role Compositions require hard-aligned Villager and hard-aligned Werewolf coverage; Simple Villager and Simple Werewolf are not mandatory by role name.

## Simulation input

- A Simulation Scenario is the pre-game simulator/cache input boundary.
- A Simulation Scenario includes Player count, Canonical Role Composition, and setup artifacts or non-default assumptions that affect simulation.
- A Simulation Start State is the fully defined state from which a simulator batch runs.
- Pre-game simulation derives each seeded Simulation Start State from the Simulation Scenario.
- Mid-game projection uses the same simulation mechanism from a later fully defined Game Session state.

## Faction state

- Each Player has exactly one Faction Beneficiary at a time in the current ruleset.
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
