# Domain invariants

This document records short, stable domain facts that can be asserted without carrying their rationale or complete interaction flows.

## Role composition

- A Role Composition is the pre-game multiset of physical Role cards selected for a Game Session.
- Without Thief, a Role Composition contains exactly one Role card per Player.
- With Thief, Role Composition is partitioned into a Player-count Deal Pool that contains exactly one Thief, plus two distinct non-Thief Thief Offer Card instances; indivisible grouped Roles remain wholly in the Deal Pool.
- New Moon Events, Player names, Seating Order, Status Effects, and the Public Group Partition are outside the Role Composition.
- Supported Player Count limits Players, not the number of physical cards used by setup.
- A Rules-Valid Role Composition has hard-aligned Villager and hard-aligned Werewolf coverage in the Deal Pool.

## Physical setup and Role knowledge

- The physical table is authoritative for a live Game Session; the app records table facts but does not create the Physical Deal.
- Players perform the Physical Deal using only the Deal Pool, and Player-specific card ownership is initially unknown to the app.
- The Moderator creates and publicly announces the Public Group Partition; its Player membership remains fixed for the Game Session.
- Every conditional setup required by a Role in the Deal Pool or Thief Offer Cards is complete before Lobby Exit.
- Physical Character Card Ownership, current Role, Moderator knowledge, and public reveal are separate facts.
- A Player's initial Role comes from the dealt Character Card. A Permanent Role Swap changes the current Role and separately defines card handling and visibility.
- Role Identification privately records an observed exact Role. Until observed, no Role is guessed; once recorded, identification is not repeated and does not assign or publicly reveal a Character Card.
- Faction Agent Group Observation privately records the complete non-empty group observed to act together for a Faction; it does not identify exact Roles.
- Role Reveal records a public physical event and never changes the current Role.
- Villager-Villager is public from the Physical Deal because its Character Card is printed on both sides.
- Recorded Role and card-zone facts respect Role Composition multiplicity and one-to-one physical card ownership.
- Public announcements and public history use only publicly revealed facts.

## Simulation boundaries

- Simulator-generated Player assignments never populate a live Game Session.
- Safety-screening and full-probability capabilities have explicit, separately versioned Role sets.
- The Full-Probability Role Set is a subset of the Safety-Screening Role Set.

## Faction state

- Each Player has exactly one Faction Beneficiary at a time.
- A Faction fact may be used only when known; unknown facts are never defaulted or inferred from remaining cards.
- A Player can act as a Faction Agent for one Faction while benefiting from another.
- Infection preserves Role identity, changes the Player's Faction Beneficiary to Werewolf unless an explicit precedence rule applies, and grants Werewolf Faction Agent status.
- A Permanent Role Swap changes the Player's Faction Beneficiary to the new Role's default Faction unless an explicit precedence rule applies.
- Ambiguous Roles default to Villager Faction Beneficiaries unless their Role says otherwise.
- Loner Roles define separate Factions rather than sharing one Loner Faction.

## Outcome resolution

- Win conditions are evaluated only during Victory Check Windows.
- All win conditions in one Victory Check Window use the same resolved Game Session state.
- Multiple true win conditions in one Victory Check Window produce a Shared Victory Outcome.
- A No-Winner Outcome occurs only when every Player is Eliminated and no Faction wins.
- A completed Game Session has exactly one Game Session Outcome.

## Simulation evidence

- A Completed Simulation Run reaches a Game Session Outcome; an Incomplete Simulation Run does not.
- Game Result Frequency is computed only from Completed Simulation Runs.
- “Could not evaluate” is not a Game Session Outcome or Game Result.
