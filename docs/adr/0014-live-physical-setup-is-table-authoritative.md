# Live physical setup is table-authoritative

Production Game Sessions record a physical game; they do not generate one. The Moderator records the selected Role Composition and facts observed during setup or play, and the app validates and persists those confirmed facts without shuffling, dealing, randomly assigning, or deducing an unknown live Player Role from missing information. Players perform the Physical Deal. When a Player actually holds Thief, that Player chooses from the observed undealt cards; the Moderator chooses Actor Setup Cards and the Public Group Partition; and the app records those results. Explicit Core-committed rules transitions may change a known current Role but are not inference about an unknown deal.

Simulator runs are the sole exception. They derive seeded synthetic assignments, undealt cards, and profile-default setup inside a Simulation Start State. Simulator-generated facts never populate, overwrite, or become recommendations for a live Game Session.

## Consequences

Live Physical Character Card Ownership and current Roles begin unknown to the app and are learned progressively. Recorded observations must respect Role Composition multiplicity, one-to-one Player/card ownership, unique card instances, prior identifications, public reveals, and confirmed dealt or undealt zones. Rehydration restores only confirmed facts and never fills gaps from a simulator profile. Actor Setup Cards and the Public Group Partition are committed before Lobby Exit; Thief's undealt cards and choice are recorded during Night 1 when the Role is actually in play. Correction of a bad recorded fact is a separate Moderator-flow decision.
