# Loose Ends

This file captures design and implementation follow-ups that are too concrete to leave only in conversation, but not yet ready to split into tracked GitHub issues.

## Actor Setup Card Validation

Settled domain rule: Actor Setup Cards are outside the Role Composition, but Actor can only be used when at least three eligible Actor Setup Cards remain available outside the selected Role Composition.

Eligibility currently means hard-aligned Villager Roles with actionable individual powers. Simple Villager, Villager-Villager, Two Sisters, and Three Brothers are not eligible.

Follow-up:

- Update role selection UX so selecting Actor requires three eligible Actor Setup Cards to remain unselected.
- Let the Moderator satisfy Player count by adding plain Villager Roles when needed instead of consuming all eligible Actor Setup Card Roles in the Role Composition.
- Update Core validation so Actor no longer adds `+3` to Role Composition size in the same `roles` list; that behavior is stale relative to the domain model.
- Update role grouping/classification so Actor is no longer treated as an Ambiguous Role where hard-aligned Villager semantics matter.
- Decide whether this becomes one implementation issue or part of a broader setup-artifact issue after Role Composition Space is fully settled.

## Hard-Aligned Coverage Validation

Settled domain rule: Rules-Valid Role Compositions require at least one hard-aligned Villager Role and at least one hard-aligned Werewolf Role, but Simple Villager and Simple Werewolf are not mandatory by role name.

Current code still models `SimpleVillager` and `SimpleWerewolf` as `AtLeast(1)` role-count constraints. That is stale relative to the domain model once the full role suite is considered.

Follow-up:

- Replace mandatory Simple Villager/Simple Werewolf validation with hard-aligned Faction coverage validation.
- Keep single-copy and exact-group role count constraints as domain rules.
- Keep current implemented role catalog and v1 New Moon exclusion as app/product support constraints, not rules-validity constraints.

## Bundled Simulator Cache Implementation

Settled domain rule: Bundled Simulator Cache entries and Local Fallback Cache Records are app-facing compressed lobby evaluations, not per-run simulation evidence. They are identified by Canonical Simulation Scenario plus simulator profile/version. Missing or stale terminal lobby evaluations block Lobby Exit while evaluation is pending; failed, incomplete, 10-second-timed-out, runtime-cancelled, or instruction-limited evaluation becomes a visible "could not evaluate" state that lets the Moderator decide whether to proceed. Failed fallback state is session-only for the current unchanged setup, is not persisted, and can be explicitly retried. Setup changes discard the stale attempt and start evaluation for the new stable Simulation Scenario. App-supported but simulator-unsupported setups do not attempt fallback and do not block Lobby Exit solely because evaluation is unavailable.

Follow-up:

- Add cache artifact read/write tests that prove a terminal lobby evaluation round-trips without changing its meaning.
- Implement cache invalidation around rules, role behavior, simulator profile behavior, supported scenario scope, Canonical Simulation Scenario construction, classification semantics, Game Result Frequency semantics, and Turn cutoff semantics.
- Implement on-device fallback only for simulator-supported Simulation Scenarios with no usable bundled or local terminal lobby evaluation. Successful fallback should materialize only bounded compact Local Fallback Cache Records, while failed, incomplete, 10-second-timed-out, runtime-cancelled, or instruction-limited fallback produces a visible "could not evaluate" state. Do not add a Moderator-facing skip or dismiss action for in-progress fallback. Add an explicit retry action after failure only; retry closes the Lobby Exit safety gate while the 10-second bounded evaluation runs again.
- Decide serialized schema, file format, compression, storage, and lookup layout in implementation issues rather than in domain docs.
