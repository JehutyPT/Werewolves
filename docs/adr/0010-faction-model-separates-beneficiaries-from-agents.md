# Faction model separates beneficiaries from agents

The domain model separates who benefits from a win condition from who acts for, wakes with, is perceived as, or is counted by a Faction. The canonical terms are Faction Beneficiary and Faction Agent.

Each Player has exactly one Faction Beneficiary at a time in the current ruleset. A Player can also be a Faction Agent for a different Faction. This is required for Roles and effects such as White Werewolf, Double Agent, Seer detection, Werewolf targeting, and infection. The legacy `Team` concept remains a codebase artifact, but it is not sufficient as the full win-condition model.

This model keeps Game Results mutually exclusive while still allowing operational mechanics to use the rulebook's practical language, such as checking or targeting "Werewolves." Generic Werewolf operational checks use Werewolf Faction Agent unless a rule explicitly refers to Role or Character Card.

## Considered options

- **Single Team/allegiance field**: store one value for both win-condition membership and operational behavior. Rejected because it cannot represent White Werewolf, Double Agent, infection, detection, and wake-group behavior without special-case leakage.
- **Role Group as Faction**: map Werewolves, Villagers, Ambiguous, Loners, and New Moon to Factions. Rejected because Role Groups are UI/validation categories, not win conditions; Loners do not share one Faction, and Ambiguous/New Moon effects do not define one shared victory condition.
- **Multiple beneficiary Factions per Player**: allow a Player to win with more than one Faction. Rejected for the current ruleset because the resolved rulings can be modeled as exclusive beneficiary replacement, and multiple beneficiaries would complicate win resolution and probability rows.
- **Beneficiary only, no agent concept**: model win-condition membership but handle operational behavior through role-specific branches. Rejected because repeated detection, targeting, waking, and counting checks would duplicate the same distinction across the engine.
- **Separate Faction Beneficiary and Faction Agent**: accepted because it models win conditions and operational mechanics explicitly without collapsing them into one field.
