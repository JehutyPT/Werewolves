# Victory Check Windows are resolution boundaries

Win conditions are evaluated only during Victory Check Windows. A Victory Check Window occurs after Night resolution, related cascades, and all resulting Dawn reactions are complete, and before the next Night after Day vote resolution and related cascades are complete. The latter boundary may be called "dusk" conversationally, but it is not a separate app phase.

Within one Victory Check Window, all win-condition predicates are evaluated against the same resolved Game Session state before deciding the Game Session Outcome. If multiple Factions' predicates are true in the same window, the Game Session ends with a Shared Victory Outcome. A No-Winner Outcome is considered only when no Faction win condition is true and every Player is Eliminated.

This gives the engine a small number of auditable transaction boundaries for outcome resolution. It prevents hook/listener ordering from accidentally deciding winners and keeps role-specific cascades from triggering mid-resolution victory checks.

## Considered options

- **Immediate checks after every state mutation**: evaluate wins whenever a Player is Eliminated, revived, converted, charmed, or otherwise changed. Rejected because hook/listener ordering and intermediate cascade states could accidentally decide outcomes.
- **One check per Turn**: evaluate only after a full Night-Dawn-Day cycle. Rejected because the rules need Dawn endings and post-vote endings to resolve without waiting for another phase.
- **Separate Dusk phase**: model the post-Day boundary as a full app phase. Rejected because the app needs an outcome boundary before the next Night, not a separate Moderator-facing phase.
- **Priority-ordered wins**: pick one winning Faction when multiple predicates are true. Rejected because simultaneous true predicates should produce a Shared Victory Outcome rather than arbitrary precedence.
- **Victory Check Windows with simultaneous predicate evaluation**: accepted because it preserves resolved-state semantics and keeps outcome resolution auditable.
