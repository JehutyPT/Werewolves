# Live physical setup is table-authoritative

Production Game Sessions record a physical game; they do not generate one. The Moderator records the selected Role Composition and facts observed during setup or play, and the app validates and persists those confirmed facts without shuffling, dealing, randomly assigning, or deducing an unknown live Player Role from missing information. Players perform the Physical Deal. When a Player actually holds Thief, that Player chooses from the observed undealt cards; the Moderator chooses Actor Setup Cards and the Public Group Partition; and the app records those results. Explicit Core-committed rules transitions may change a known current Role but are not inference about an unknown deal.

Simulator runs may use seeded synthetic state; [ADR-0007](./0007-simulation-scenario-boundary.md) owns the Simulation Scenario and Simulation Start State boundary. Simulator-generated facts never populate, overwrite, or become recommendations for a live Game Session.

## Consequences

Live Physical Character Card Ownership and current Roles begin unknown to the app and are learned progressively. Recorded observations must respect Role Composition multiplicity, one-to-one Player/card ownership, unique card instances, prior identifications, public reveals, and confirmed dealt or undealt zones. Rehydration restores only confirmed facts and never fills gaps from a simulator profile. Actor Setup Cards and the Public Group Partition must be complete before Lobby Exit; Thief's undealt cards and choice are recorded during Night 1 when the Role is actually in play. The [Moderator submission and commitment flow](../contracts/moderator-interaction.md#submission-and-commitment) defines how observed facts are accepted, corrected, and recovered.

Role Lock-In and Actor Setup Card acceptance are replaceable staging boundaries rather than the final game-start boundary. Because neither the Physical Deal nor an in-game response has occurred, the Moderator may supersede either complete artifact before Lobby Exit; Lobby Exit remains irreversible. The [pre-game configuration contract](../contracts/moderator-interaction.md#pre-game-configuration) owns the precise edit authority. A replacement is new Simulation Scenario input and cannot retain evaluation for the superseded scenario.

## Amendment: ADR-0017

[ADR-0017](./0017-thief-offer-is-committed-before-the-physical-deal.md) supersedes the Thief-specific setup clauses above: the Moderator commits the Deal Pool and offers before the Physical Deal, and Players deal only the pool. The [physical setup rule](../domain/game-rules.md#setup) defines which cards may enter each zone. Simulation may assign the committed Deal Pool while retaining the offers, but it still cannot populate or alter a live Game Session.
