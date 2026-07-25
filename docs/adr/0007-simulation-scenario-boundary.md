# Simulation scenarios separate pre-game inputs from run state

Pre-game simulator and cache identity is defined by a Simulation Scenario, not by Role Composition alone and not by a fully assigned Simulation Start State. A Simulation Scenario includes the Player count, Canonical Role Composition, every Role-required setup artifact that affects simulation—such as Actor Setup Cards and the Public Group Partition—and any other non-default assumption such as New Moon support.

Role Composition remains the domain term for the physical deck selected before a Game Session starts. It includes Thief's extra Character Cards, but excludes Actor Setup Cards, Player names, Seating Order, Status Effects, New Moon Events, physical traits, and simulator profile defaults. Those exclusions are intentional: they keep Role Composition focused on the deck while giving the simulator a larger input boundary when those factors can affect outcomes.

Each simulation run derives a seeded Simulation Start State from the Simulation Scenario. The Simulation Start State is the concrete state the engine can execute. It may include seeded Player assignment and other run-specific derived facts, but it cannot supply a missing Role-required setup artifact: Actor Setup Cards and the Public Group Partition must already be explicit Scenario inputs whenever their Roles are reachable.

## Considered options

- **Role Composition only**: use selected Role counts as the complete pre-game simulator and cache input. Rejected because Actor Setup Cards, the Public Group Partition, New Moon support, Player count when Thief is present, and other assumptions can change simulator behavior without changing the Role Composition.
- **Full Simulation Start State as cache identity**: cache only concrete assigned player states. Rejected for pre-game probability because random assignment is intentionally generated per run; including it in cache identity explodes the state space and mixes scenario identity with run evidence.
- **Ad hoc option bag**: pass Role counts plus loose flags into simulator/cache lookup. Rejected because cache identity becomes easy to under-specify and hard to invalidate consistently.
- **Simulation Scenario boundary**: keep Role Composition as the physical deck concept and define Simulation Scenario as the stable pre-game simulator/cache input. Accepted.

## Amendment: ADR-0017

ADR-0017 adds the committed Deal Pool/Thief Offer partition to target Simulation Scenario and cache identity. The same Role Composition with a different partition is a different scenario. Target seeded derivation assigns only the Deal Pool and retains the two fixed offers; this amendment does not claim that the current simulator API already supports those fields.
