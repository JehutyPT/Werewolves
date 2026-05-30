# Game Rules Clarifications

This document records domain rule rulings for interactions that are too detailed for `CONTEXT.md`. It clarifies `docs/game-rules.md`; it is not an architecture note, product requirement, or implementation issue.

## Allegiance and Operational Status

- A Player has exactly one **Faction Beneficiary** at a time. Elimination-style win conditions evaluate living Players by **Faction Beneficiary**, not by original Role, Character Card, or who they wake with.
- A **Faction Agent** is operational: the Player wakes with, acts for, is perceived as, or is counted by a Faction for mechanics without necessarily benefiting from that Faction's win condition.
- A **Permanent Role Swap** changes the Player's Role and, by default, changes the Player's **Faction Beneficiary** to the new Role's default Faction unless an explicit precedence rule says otherwise.
- Generic rules that check, target, count, or react to "Werewolves" use Werewolf **Faction Agents** unless the rule explicitly says Role or Character Card.

## Cross-Faction Lovers

- When Cupid links two Players with different current **Faction Beneficiaries**, both immediately become **Cross-Faction Lovers**. Cross-Faction Lovers are a **Latent Faction** whose win condition is being the last two Players alive.
- Same-Faction **Lovers** remain only a **Status Effect**. They do not change either Player's **Faction Beneficiary**.
- Cross-Faction Lovers' beneficiary change takes precedence over later beneficiary-changing effects, including Double Agent, Wild Child transformation, Wolf Hound choice, Thief swap, Specter, and Backfire. Those effects can still change Role, **Faction Agent** status, private information, or other operational state.
- A Lover cannot use Devoted Servant's swap ability.
- If Devoted Servant successfully swaps with an eliminated Player, the Servant takes the eliminated Player's Role and the new Role's default **Faction Beneficiary**. The eliminated Player's Status Effects and relationship state, including Lover state, are not inherited.
- If Miracle leaves only one previously eliminated Cross-Faction Lover alive, that revived Player is no longer a Cross-Faction Lover. The Cross-Faction Lovers **Latent Faction** does not continue with one surviving member, and Miracle's Simple Villager **Permanent Role Swap** applies unless another explicit precedence rule says otherwise.

## Werewolf Faction Agent Rulings

- Werewolf group attacks cannot target Werewolf **Faction Agents**.
- White Werewolf is a Werewolf **Faction Agent** for night targeting and detection while remaining a White Werewolf **Faction Beneficiary** for win conditions. White Werewolf's solo attack targets another Werewolf **Faction Agent**.
- Big Bad Wolf's extra attack is disabled once any non-temporary Werewolf **Faction Agent** has been Eliminated.
- Temporary Werewolf **Faction Agents** created by Full Moon Rising do not count for Big Bad Wolf disablement.
- During Full Moon Rising, Hunter, Witch, and Seer are temporary Werewolf **Faction Agents** for the next Night only. Their Roles and **Faction Beneficiaries** do not change. While active, the temporary status applies to operational Werewolf **Faction Agent** checks such as Seer, Fox, Bear Tamer, Knight with the Rusty Sword, and night targeting checks.
- Infection changes a Player's Werewolf **Faction Agent** status but does not change their **Faction Beneficiary**.

## Role Powers and New Moon Assignments

- If Elder is Eliminated by the village vote, all Villager **Roles**, including Actor, lose their special powers for the rest of the Game Session regardless of those Players' **Faction Beneficiaries**.
- Elder's village-vote penalty is continuing suppression. Later **Permanent Role Swaps** into Villager Roles enter with those Villager Role powers already suppressed.
- Double Agent can be assigned only to a living non-Werewolf **Faction Agent**.
- Double Agent becomes a Werewolf **Faction Beneficiary** while remaining outside the Werewolf night group. If the chosen Player is already a Cross-Faction Lover, Cross-Faction Lovers precedence keeps their existing **Faction Beneficiary**; the Player only gains knowledge of the Werewolf **Faction Agents**.

## Victory Timing and Outcomes

- Win conditions are evaluated only during **Victory Check Windows**: at Dawn after Night eliminations and related cascades are resolved, and before the next Night after Day vote resolution and related cascades are resolved. The pre-Night Victory Check Window is not a separate Dusk Phase.
- During one **Victory Check Window**, all win-condition predicates are evaluated against the same resolved Game Session state. If multiple Factions' predicates are true, the Game Session ends with a **Shared Victory Outcome** rather than applying a priority order.
- Angel's transient Faction can win if the Angel is Eliminated by the Day 1 vote or during Night 1 or Night 2. That eligibility remains active through the Dawn **Victory Check Window** that resolves Night 2. If Angel has not won by then, the transient Faction expires immediately after that window and the Angel becomes a Simple Villager.
- A **No-Winner Outcome** occurs only when no Faction win condition is true in the **Victory Check Window** and every Player is Eliminated.

## Werewolf Control Shortcut

- The Werewolf Faction's full win condition is eliminating all other **Faction Beneficiaries**.
- **Werewolf Control Shortcut** is only a Villager-vs-Werewolf endgame shortcut. It applies only when every living non-Werewolf **Faction Beneficiary** is a Villager **Faction Beneficiary**.
- When the shortcut applies, Werewolves are treated as winning once living Werewolf **Faction Beneficiaries** have **Durable Voting Power** control over the Day vote.
- **Durable Voting Power** is stable voting weight already in force. It includes permanent voting changes such as Sheriff double vote, Village Idiot vote loss, and Little Rascal triple vote.
- **Durable Voting Power** excludes temporary one-window vote effects and role-triggered vote restrictions, such as Scapegoat next-day restrictions and temporary New Moon Event vote rules.
