# Game Rules Clarifications

This document records settled rule interactions that are too subtle for `docs/domain/game-rules.md`.

## Allegiance and Operational Status

- A Player has exactly one **Faction Beneficiary** at a time. Elimination-style win conditions evaluate living Players by **Faction Beneficiary**, not by original Role, Character Card, or who they wake with.
- A **Faction Agent** is operational: the Player wakes with, acts for, is perceived as, or is counted by a Faction for mechanics without necessarily benefiting from that Faction's win condition.
- A **Permanent Role Swap** changes the Player's Role and, by default, changes the Player's **Faction Beneficiary** to the new Role's default Faction unless an explicit precedence rule says otherwise.
- Generic rules that check, target, count, or react to "Werewolves" use Werewolf **Faction Agents** unless the rule explicitly says Role or Character Card.

## Initial Beneficiary Closure

- **Initial Beneficiary Closure** is the automatic Night 1 boundary after every scheduled observation, choice, and Core transition that can establish or override an initial **Faction Beneficiary** for the admitted Role Composition has committed, and before a Beneficiary-dependent rule consumes the result.
- A **Faction Agent Group Observation** establishes Agent membership only. Core first places every explicit Beneficiary fact, Agent-only exception, and transition at its effective historical boundary. Once the complete initial group and the committed determining inputs for every applicable Beneficiary exception are available, group membership supplies a Werewolf **Faction Beneficiary** candidate and absence supplies a Villager candidate only for Players still unresolved at the initial collective boundary.
- Closure evaluates deferred rules at their original historical boundaries, then applies later explicit facts, Agent-only exceptions, and beneficiary precedence in chronological order. Thief choice is effective before Cupid and the collective Werewolf call; Wolf Hound choice is effective after Cupid but before the collective call; Accursed Wolf-Father infection is effective after the collective call. Closure preserves those boundaries even when the determining facts become known later.
- A Role Composition from which a Role with a different initial Beneficiary is reachable is not App-Supported until that Role's establishment path is available. Angel's existing no-identification flow does not satisfy that prerequisite.
- Closure atomically appends one batch containing only newly entailed residual and deferred facts, each retaining its effective historical boundary. Previously committed explicit facts are neither rewritten nor re-appended. Projecting all committed facts in effective-boundary order yields the final precedence-resolved Known Faction State. Incomplete prerequisites commit no residual or deferred Beneficiary facts. Closure never writes a provisional value, creates a correction, asks the Moderator for a generic Faction fact, or infers an exact Role or Beneficiary from remaining Character Cards.

## Cross-Faction Lovers

- Cupid links the selected pair at its ordinary early Night 1 call. Classification uses their **Faction Beneficiaries** at that historical moment. If either live fact is still unknown, the relationship remains unclassified until Initial Beneficiary Closure; it is never provisionally classified as same-Faction or Cross-Faction. Closure computes each Lover's pre-Lovers Beneficiary at the link boundary, applies Cross-Faction Lovers precedence when those candidates differ, and exposes only the final atomic result before the first Dawn **Victory Check Window**. If the facts differ, both become **Cross-Faction Lovers**, whose win condition is being the last two Players alive.
- Wolf Hound and Simple Villager are same-Faction when Cupid links them before Wolf Hound's choice, even if Wolf Hound later chooses Werewolf. Wolf Hound and Simple Werewolf are Cross-Faction at that link boundary; a later Werewolf choice cannot erase the resulting Cross-Faction Lovers precedence.
- Same-Faction **Lovers** remain only a **Status Effect**. They do not change either Player's **Faction Beneficiary**.
- Cross-Faction Lovers' beneficiary change takes precedence over later beneficiary-changing effects, including infection, Double Agent, Wild Child transformation, Wolf Hound choice, Thief swap, Specter, and Backfire. Those effects can still change Role, **Faction Agent** status, private information, or other operational state.
- Devoted Servant cannot be used by a Lover and does not transfer the **Vote Target**'s relationship state; see [Devoted Servant Swap Boundary](#devoted-servant-swap-boundary).
- If Miracle leaves only one previously eliminated Cross-Faction Lover alive, that revived Player is no longer a Cross-Faction Lover. The Cross-Faction Lovers **Latent Faction** does not continue with one surviving member, and Miracle's Simple Villager **Permanent Role Swap** applies unless another explicit precedence rule says otherwise.

## Werewolf Faction Agent Rulings

- Werewolf group attacks cannot target Werewolf **Faction Agents**.
- White Werewolf is a Werewolf **Faction Agent** for night targeting and detection while remaining a White Werewolf **Faction Beneficiary** for win conditions. White Werewolf's solo attack targets another Werewolf **Faction Agent**.
- Big Bad Wolf's extra attack is disabled once any non-temporary Werewolf **Faction Agent** has been Eliminated.
- Temporary Werewolf **Faction Agents** created by Full Moon Rising do not count for Big Bad Wolf disablement.
- During Full Moon Rising, Hunter, Witch, and Seer are temporary Werewolf **Faction Agents** for the next Night only. Their Roles and **Faction Beneficiaries** do not change. While active, the temporary status applies to operational Werewolf **Faction Agent** checks such as Seer, Fox, Bear Tamer, Knight with the Rusty Sword, and night targeting checks.
- Infection preserves the Player's Role and Role Powers, changes their **Faction Beneficiary** to Werewolf unless an explicit beneficiary-precedence rule applies, and makes them a Werewolf **Faction Agent**.

## Fox Checks

- Each Night while the Fox power is available, the Fox may either decline or choose any living Player, including the Fox, as the center of a check.
- A check inspects a duplicate-free set: the chosen Player plus the nearest living Player clockwise and counterclockwise around the circular **Seating Order**, skipping Eliminated Players. With at least three living Players, the set has three members. With exactly two, both directions reach the same other Player and the set contains both living Players.
- A one-living-Player state cannot reach another Fox call because the preceding **Victory Check Window** ends the Game Session. The Fox therefore never becomes unable to act solely because the living roster is too small.
- Feedback is immediate and evaluates current Werewolf **Faction Agent** state. A positive check gives the affirmative sign and preserves the power. A performed negative check gives the negative sign and removes the Fox power permanently.
- Declining is not a check: it gives no affirmative or negative sign and does not remove the power. If the power is already lost or suppressed, no check or feedback occurs.

## Rusty Sword Disease

- The Knight with the Rusty Sword triggers only when a physical Werewolf attack actually Eliminates that Player at Dawn. A prevented, resisted, healed, replaced, or cancelled Elimination does not trigger the power. The collective Werewolf attack, Big Bad Wolf's extra attack, and White Werewolf's solo attack qualify; infection and later reaction Eliminations do not.
- Select the diseased Player only after the complete **Elimination Cascade** containing the Knight's Elimination has drained. Start at the seat immediately left of the Knight, which is clockwise in the canonical circular **Seating Order**, and wrap at most once. The Knight's fixed seat remains the reference even though the Knight has been Eliminated; skip Eliminated Players and Players who are not eligible Werewolf **Faction Agents**.
- Eligibility is evaluated at that post-cascade snapshot. A Player whose infection successfully resolved during the triggering Night and a Wild Child who became an Agent during the cascade are current Agents and can be selected. A temporary Werewolf **Faction Agent** from Full Moon Rising during the triggering Night remains eligible for this Rusty Sword check even if that Night-scoped status would otherwise expire at Dawn.
- If no eligible Player survives the cascade, no disease is scheduled. Otherwise, snapshot the selected Player's identity. Later changes to Seating Order, Role, or Werewolf **Faction Agent** status neither cancel nor move the disease.
- The disease does not itself Eliminate the selected Player until the following Dawn. Then resolve it exactly once as an Elimination in that Dawn's initial concurrent set, before its reactions and **Victory Check Window**. If the selected Player is not living then, the disease expires without effect; it never retargets. If another same-Dawn cause also Eliminates that Player, the Player is Eliminated only once. If a **Victory Check Window** ends the Game Session before the disease resolves, it has no later effect.

## Piper Charm Targets and Outcome

- An eligible Piper charm target is a living Player other than the Piper who does not already have the **Charmed** Status Effect. Already-Charmed Players cannot be selected again, but they still join the Charmed-Players wake interval after the Piper action.
- The Piper action is mandatory and selects distinct Players. When at least two eligible targets exist, the Piper must select exactly two. When exactly one exists, that Player is the only available selection and must be Charmed; the Piper does not select that Player twice or decline. When none exist, the action Charms no Player and makes no state change.
- Ordinarily, a living Piper **Faction Beneficiary** cannot reach a later Night call with zero eligible targets: the preceding pre-Night **Victory Check Window** would already find every other living Player Charmed and end the Game Session. A zero-target call remains reachable when a Player still holds and can use the Piper Role but no longer benefits from the Piper Faction, such as a Piper who became a Cross-Faction Lover.
- Applying **Charmed** never triggers an immediate win check. The next **Victory Check Window** is at Dawn after Night Eliminations and their complete **Elimination Cascade**. The Piper Faction predicate is true there only if a living Piper **Faction Beneficiary** exists and every other living Player in that resolved state is Charmed. Eliminating the Piper before that window therefore prevents a Piper Faction win; eliminating a newly Charmed target does not, provided every surviving non-Piper Player is Charmed. Any other Faction predicate true in the same window produces a **Shared Victory Outcome** under the general outcome rule.

## Prejudiced Manipulator Public Groups and Outcome

- The Moderator creates one **Public Group Partition** before the Game Session: every Player belongs to exactly one of two publicly announced, non-empty groups. The groups may be unequal, including a one-Player group; equal size is not a Rules-Validity requirement.
- Membership belongs to Player identity and remains fixed for the full Game Session. Elimination changes who is living, not the stored partition; Role, Status Effect, Faction Beneficiary, and Faction Agent changes do not move a Player between groups.
- The **Opposing Public Group** is the group that does not contain the current living Prejudiced Manipulator **Faction Beneficiary**. If a **Permanent Role Swap** gives the Role and its default beneficiary to another living Player, that Player uses the existing partition and the group opposite their own membership.
- At a **Victory Check Window**, the Prejudiced Manipulator Faction predicate is true only if such a living beneficiary exists and no living Player remains in their Opposing Public Group. An Eliminated Manipulator cannot win posthumously, including when the Manipulator and the final opposing Player are Eliminated in the same cascade. Any other Faction predicate true in the same window still produces a **Shared Victory Outcome** under the general outcome rule.

## White Werewolf Solo-Action Cadence

- The White Werewolf has no solo **Night Action** on Night 1. The action is scheduled on Nights 2, 4, 6, and every later even-numbered Game Session Night.
- This is an absolute Night-number cadence, not a count of successful or attempted uses. Declining the optional action, having no legal target, or having the attack prevented or made ineffective does not postpone it. A Player who acquires the White Werewolf Role later in the Game Session inherits the same global schedule rather than starting a new cadence.
- On an eligible Night, the order is the collective Werewolf operation, Fox, the Accursed Wolf-Father's infection choice, the White Werewolf's solo action, then the Big Bad Wolf's extra action. A Little Girl spying interval ends with the collective operation.
- The solo action can target any other living Player who is a Werewolf **Faction Agent** when that call begins. An infection choice confirmed earlier in the same Night does not make its target eligible: the target becomes a Werewolf **Faction Agent** for subsequent Nights, not during the current Night's remaining calls.
- Night attacks resolve at Dawn. A Werewolf targeted by the White Werewolf remains living through the rest of the Night call order and can complete any later same-Night action before the attack resolves.

## Wolf Hound Identity

- Wolf Hound begins Night 1 as a Villager **Faction Beneficiary**. That fact remains effective through Cupid's earlier call until Wolf Hound's own choice commits.
- Wolf Hound's final Night 1 choice does not cause a **Permanent Role Swap**. The Player's runtime Role and physical Character Card remain Wolf Hound in both branches.
- The Villager branch makes the Player a Villager **Faction Beneficiary** and not a Werewolf **Faction Agent**. The Werewolf branch makes the Player a Werewolf **Faction Beneficiary** and Werewolf **Faction Agent**, so they join the collective Werewolf action later on Night 1 and on subsequent Nights.
- Generic Werewolf detection, targeting, waking, and counting follow the chosen **Faction Agent** state. Exact-Role mechanics continue to identify the Player as Wolf Hound.
- Existing beneficiary-precedence rules still apply. In particular, a Cross-Faction Lover keeps the Cross-Faction Lovers **Faction Beneficiary** after either Wolf Hound choice, while the Werewolf branch still grants Werewolf **Faction Agent** status.
- On Elimination, revealing the Character Card identifies Wolf Hound but does not reveal the private Villager-or-Werewolf choice.

## Thief Choice and Acquired-Role Timing

- The Night 1 call uses the holder and two offers already established by the [physical setup rule](./game-rules.md#setup); the offers are not selected or entered again during the call.
- After an offer is selected, continue forward through the canonical Night 1 call order. The acquired Role receives ordinary behavior whose call is still ahead, including first-Night-only behavior; no earlier call or pre-game setup is replayed.

## Devoted Servant Swap Boundary

- Devoted Servant reacts to a non-tied standard or **Consecutive Vote** fixing one living **Vote Target**, not to an already-established **Elimination**. The interaction runs before public Role Reveal, pardon, immunity, ordinary Elimination, or any other target-Role consequence. A tie does not trigger it, including a tie later replaced by Scapegoat Elimination, and neither do Dawn victims or other non-Vote Eliminations.
- If nobody acts before the Vote Target is revealed, Vote resolution resumes against the unchanged target and no Devoted Servant is identified. Use begins only when an eligible Servant publicly reveals their own Character Card.
- To Use, Devoted Servant reveals and discards that card, then immediately takes and reads the Vote Target's card without revealing it to the table. The Permanent Role Swap occurs at that interception point with fresh power state; the former Role is public history and the acquired current Role remains hidden from the table. The transferred Role's target-side reveal, protection, pardon, immunity, and on-Elimination behavior do not run. In particular, an unused Village Idiot pardon transfers fresh to the Servant, while the original Vote Target is Eliminated without an Idiot reveal, pardon spend, or voting-power loss.
- The acquired Role cannot act or trigger again during the current Day and becomes available from the next Night onward. An acquired Village Idiot pardon therefore cannot protect the Servant during a same-Day Consecutive Vote.
- The Vote Target's Status Effects and relationships are not inherited. A Lover cannot use Devoted Servant, so no Lover relationship crosses a successful swap.
- The swap deliberately clears Charmed, Sheriff, and Town Crier as explicit parts of the Servant's old identity. This is specific to Devoted Servant, not a universal reset of every Status Effect.
- Infection remains attached to the Player beneath that identity and continues to make them a Werewolf **Faction Agent** after the swap. An in-force Scapegoat voting restriction also follows the continuing Player and therefore remains in force.

## Role Powers and New Moon Assignments

- A **Role Power** is any gameplay capability granted by a Role beyond Role identity, the physical Character Card, and default **Faction Beneficiary**. It includes chosen Night or Day actions, automatic reactions, passive protections, immunities or detections, and Role-granted recognition or communication.
- Role identity, public-from-deal status, physical reveal behavior, and information already learned are not **Role Powers**. Villager-Villager therefore remains publicly known, and Sisters or Brothers remember one another after suppression, although no later Role-granted communication interval may begin.
- If Elder is Eliminated by the village vote, **Role Power Suppression** begins after that Vote's complete **Elimination Cascade** and before any **Consecutive Vote**. A Hunter who is the Elder's Lover and is Eliminated by heartbreak during that cascade still receives the final shot settled by the Hunter cascade rules.
- Once active, **Role Power Suppression** affects every current and future Villager **Role**, including Actor, regardless of the Player's **Faction Beneficiary**. Later **Permanent Role Swaps** into Villager Roles enter suppressed, and Actor cannot select or newly use or trigger an Actor Setup Card power.
- **Role Power Suppression** prevents new power effects from beginning or triggering; it does not rewrite history. Learned information, Lovers created by Cupid, an already-committed **Consecutive Vote**, an in-force Scapegoat voting restriction, and a scheduled Rusty Sword disease remain in force.
- Suppression neither consumes nor restores a **One-Use Resource**. A suppressed power cannot commit its resource, while every resource committed before suppression remains spent even if its resulting effect is still pending.
- Double Agent can be assigned only to a living non-Werewolf **Faction Agent**.
- Double Agent becomes a Werewolf **Faction Beneficiary** while remaining outside the Werewolf night group. If the chosen Player is already a Cross-Faction Lover, Cross-Faction Lovers precedence keeps their existing **Faction Beneficiary**; the Player only gains knowledge of the Werewolf **Faction Agents**.

## Actor Borrowed-Power Timing

- The selected power is active immediately and expires at the Actor call at the start of the next Night, before Actor may select again. That expiry occurs even when Actor declines the new call or suppression prevents a new selection.
- Actor is called before every eligible source power. A borrowed chosen power therefore executes at its source power's ordinary later relative call, while a passive or reactive power waits for its ordinary trigger during the active interval. No earlier call is replayed, and selection does not execute a later action immediately.
- A native Role's one-time Night 1 setup restriction does not make its unused Actor Setup Card expire. Actor may select Cupid or Stuttering Judge on any Night: Cupid receives its corresponding later call that Night and creates Lovers, while Stuttering Judge establishes its signal at the corresponding later call for possible use during the following Day. Each remains a single Actor-card use.
- When Actor selects Cupid, the Lovers' Faction relationship is determined from their Faction Beneficiaries at the moment they are linked. On Night 1, live classification may wait for Initial Beneficiary Closure; on later Nights the required Faction state is already complete.
- Actor borrows the complete source-power contract, not only its benefit. A borrowed Witch begins with both potions and commits each normally; borrowed Elder provides its resistance and also starts **Role Power Suppression** after the complete cascade if the village Vote Eliminates Actor; borrowed Village Idiot cancels the qualifying Vote Elimination and permanently removes Actor's voting right without changing Actor's Role or borrowing the Village Idiot Character Card reveal.
- Chosen Night powers use their settled later calls; Little Girl uses only the collective Werewolf spying interval; Bear Tamer evaluates at its Dawn trigger; and Hunter, Elder, Scapegoat, Village Idiot, and Knight with the Rusty Sword use their settled reactive or passive triggers while the borrowed power is active.
- Expiry or later suppression does not undo a completed or committed result. Learned information, Lovers, spent potions, a committed **Consecutive Vote**, lost voting rights, a Scapegoat restriction, and a scheduled Rusty Sword disease remain in force under their ordinary rules.

## Night Action Resolution

- A **One-Use Resource** is consumed when the Moderator confirms the action that commits it. Later prevention, redundancy, or failure to change resolved state does not refund it. This general rule applies to both Witch potions, the Accursed Wolf-Father's infection, and any future one-use Role resource unless that resource explicitly defines an exception.
- The White Werewolf's solo attack is resolved like any other physical Werewolf attack. Its distinct cadence and requirement to target another Werewolf **Faction Agent** remain unchanged, but otherwise the same Defender protection, Elder resistance, Witch disclosure, and Witch healing rules apply.
- Defender protection applies to the collective Werewolf attack, Big Bad Wolf's extra attack, and White Werewolf's solo attack. It lasts for the entire Night and blocks every applicable physical Werewolf attack against the protected Player rather than being consumed by one hit. Defender protection resolves before the Elder's resistance, so a blocked physical attack does not spend that resistance. Defender protection does not apply to the Accursed Wolf-Father's infection.
- Defender's consecutive-target restriction compares only two Nights that both contain actual protection by the same current Defender power instance. Any Night without protection—including suppression, unavailability, or a rules-valid no-target path—or acquisition of a fresh Defender power through Permanent Role Swap resets the sequence, so an older target is eligible on the next active call.
- Big Bad Wolf's extra attack must target a different living non-Werewolf **Faction Agent** from the Player selected by the collective Werewolf attack.
- The collective Werewolf attack, Big Bad Wolf's extra attack, White Werewolf's solo attack, and Accursed Wolf-Father infection are all qualifying Werewolf-sourced attacks for the Elder's one-time resistance. When that resistance applies, the attack spends it. A physical attack leaves the Elder alive; infection leaves the Elder alive and uninfected, while the already-confirmed infection use remains spent under the general **One-Use Resource** rule. Once the resistance is spent, a later qualifying attack resolves normally.
- When the Witch acts, the Moderator shows every Player targeted that Night by the collective Werewolf attack, Big Bad Wolf's extra attack, or White Werewolf's solo attack. Targets remain visible even when Defender protection, Elder resistance, or Accursed Wolf-Father infection means they would not be physically eliminated. If the healing potion remains available, the Witch may commit it to at most one shown target; a redundant or ineffective choice still spends it under the general **One-Use Resource** rule.
- The Witch's healing potion restores exactly one Elder life lost to the selected physical attack, capped at the Elder's original two lives. Healing a fresh Elder after the first hit restores the Elder's resistance. Healing an Elder whose resistance was already spent after a later lethal hit leaves the Elder alive with that resistance still spent. Healing never grants a third life.
- If a fresh Elder resists a confirmed Accursed Wolf-Father infection and the Witch heals that collective target during the same Night, the potion restores the Elder's resistance. The Elder remains alive and uninfected with their resistance available again, while both the infection use and healing potion remain spent. If an infection successfully takes hold on an Elder whose resistance was already spent, the healing potion does not remove that infection.
- The Little Girl's spying interval is exactly the collective Werewolf group's wake-to-sleep interval. It does not extend into later individual actions by Accursed Wolf-Father, White Werewolf, or Big Bad Wolf.

## Elimination Cascades

- Every actual Elimination of the Hunter triggers exactly one final-shot action regardless of its reason. Prevention, cancellation, immunity, or any other outcome that leaves the Hunter alive does not trigger a shot, and the same Hunter Elimination cannot trigger more than once.
- Hunter is a single-copy Role. An **Elimination Cascade** can therefore contain at most one Hunter final shot, so no ordering rule between multiple Hunter shots is required.
- When one rules step produces multiple actual Eliminations, all of those initially concurrent Eliminations take effect before any resulting Hunter final shot, Lovers heartbreak, or other Elimination reaction. Those Players are no longer living targets when the reactions begin.
- Resolve every Lovers heartbreak caused by the current set of Eliminations before prompting for any Hunter final shot caused by that set. A Hunter Eliminated by heartbreak still receives exactly one final shot, while a Player Eliminated by heartbreak is no longer a legal target for a later shot.
- Every actual Elimination caused by a reaction remains in the same **Elimination Cascade**. Apply the same ordering again to each newly caused Elimination and continue until no new Elimination or required reaction remains. Only then may the next **Victory Check Window** begin.
- A Hunter's final shot targets one other living Player. If that shot Eliminates a Lover whose partner is still living, the partner's heartbreak Elimination joins and completes within the same **Elimination Cascade**.

## Consecutive Day Votes

- Each non-tied first Vote or **Consecutive Vote** supplies its own Devoted Servant window while the power remains available. If nobody acts during the first window, the power remains available for the Consecutive Vote; Use spends it. A tie supplies no window even when Scapegoat later replaces the tied result with an Elimination.
- Once the Stuttering Judge gives the valid once-per-game signal during the first Vote of a Day, a **Consecutive Vote** occurs that Day after the first Vote and any resulting **Elimination Cascade** have finished, regardless of the first Vote's outcome. An ordinary Elimination, a tie with no Elimination, a Village Idiot pardon, and a tie replaced by Scapegoat Elimination all lead to the **Consecutive Vote**.
- The guaranteed **Consecutive Vote** is an explicit exception to the physical Village Idiot rule that would otherwise end voting for the Day after the pardon. No new Debate or **Victory Check Window** occurs between the two Votes.
- If the first Vote targets an unrevealed Village Idiot, the pardon cancels that Vote's Elimination, reveals the Village Idiot, and removes their voting right immediately. The revealed Village Idiot cannot vote in the **Consecutive Vote** but remains a legal target; if selected again, they are Eliminated normally because the pardon has already been spent.
- The first Vote is fully resolved rather than replaced or retried. Its outcome, all state changes, and its complete **Elimination Cascade** remain in force when the **Consecutive Vote** begins.
- The **Consecutive Vote** uses the living roster and current voting rights after the first Vote's **Elimination Cascade**. A valid Stuttering Judge signal has already committed that Vote, so eliminating the Stuttering Judge during the first Vote's cascade does not cancel it.
- If a first-Vote tie Eliminates the Scapegoat, the **Consecutive Vote** follows after that Elimination Cascade. The Scapegoat's voter restriction begins on the following Day and does not restrict the same-Day **Consecutive Vote**.
- If a committed **Consecutive Vote** has no eligible voter, no physical Vote or Elimination occurs. This is not a tie and cannot trigger Scapegoat.
- Only Elimination by the first Vote of Day 1 qualifies the Angel's Day 1 win condition. If that first Vote Eliminates the Angel, eligibility remains pending while the **Consecutive Vote** and its cascade resolve; the shared pre-Night **Victory Check Window** occurs only after both Votes. Elimination by the **Consecutive Vote** does not qualify the Angel.

## Sisters and Brothers Communication

- Under the [physical rules](./game-rules.md#the-villagers), Two Sisters and Three Brothers recognize one another on Night 1, and the Moderator may allow brief silent communication on later Nights at their discretion.
- Any later communication involves the living current Role holders, including holders created by a **Permanent Role Swap**, rather than only the Players who recognized one another on Night 1.

## Victory Timing and Outcomes

- Win conditions are evaluated only during **Victory Check Windows**: at Dawn after Night Eliminations, their related cascades, and the Dawn Main Action Loop (Bear Tamer, Gypsy, then Town Crier) are resolved, and before the next Night after Day vote resolution and related cascades are resolved. The pre-Night Victory Check Window is not a separate Dusk Phase.
- During one **Victory Check Window**, all win-condition predicates are evaluated against the same resolved Game Session state. If multiple Factions' predicates are true, the Game Session ends with a **Shared Victory Outcome** rather than applying a priority order.
- Angel's transient Faction can win if the Angel is Eliminated by the first Vote of Day 1 or during Night 1 or Night 2. Elimination by a **Consecutive Vote** on Day 1 does not qualify. That eligibility remains active through the Dawn **Victory Check Window** that resolves Night 2. If Angel has not won by then, the transient Faction expires immediately after that window and the Angel becomes a Simple Villager.
- A **No-Winner Outcome** occurs only when no Faction win condition is true in the **Victory Check Window** and every Player is Eliminated.

## Werewolf Control Shortcut

- The Werewolf Faction's full win condition is eliminating all other **Faction Beneficiaries**.
- **Werewolf Control Shortcut** is only a Villager-vs-Werewolf endgame shortcut. It applies only when every living non-Werewolf **Faction Beneficiary** is a Villager **Faction Beneficiary**.
- When the shortcut applies, Werewolves are treated as winning once living Werewolf **Faction Beneficiaries** have **Durable Voting Power** control over the Day vote.
- **Durable Voting Power** is stable voting weight already in force. It includes permanent voting changes such as Sheriff double vote, Village Idiot vote loss, and Little Rascal triple vote.
- **Durable Voting Power** excludes temporary one-window vote effects and role-triggered vote restrictions, such as Scapegoat next-day restrictions and temporary New Moon Event vote rules.
