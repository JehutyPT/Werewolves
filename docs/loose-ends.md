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
