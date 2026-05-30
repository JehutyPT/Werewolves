# The Werewolves of Miller's Hollow: The Pact - Core Rules & Roles (Excluding Building Dependencies)

## Game Objective

*   **Villagers:** Eliminate all non-Villager Faction Beneficiaries.
*   **Werewolves:** Eliminate all non-Werewolf Faction Beneficiaries. When every living non-Werewolf Faction Beneficiary is a Villager Faction Beneficiary, the Werewolf Control Shortcut treats the Werewolves as winning once their Durable Voting Power controls the Day vote because the remaining game is checkmate.
*   **Loners:** Achieve their specific unique objective.
*   **Ambiguous:** Win with the Faction they are a Faction Beneficiary of at the relevant Victory Check Window. Their Faction Beneficiary can change.

A Game Session usually ends with one winning Faction. If multiple win conditions are true in the same Victory Check Window, the Game Session ends with a Shared Victory Outcome. If no win condition is true and every Player is Eliminated, the Game Session ends with a No-Winner Outcome.

In the current ruleset, each Player has exactly one Faction Beneficiary at a time. Status Effects and Permanent Role Swaps can replace that beneficiary link when their rules say so.

## Setup

1.  Select a non-playing Moderator.
2.  Players draw their Character Card face-down (e.g., from a shuffled deck or bag). The Moderator does *not* initially know Player Roles unless revealed by game actions.
3.  Players secretly look at their Character Card.
4.  (If applicable) Perform specific physical Role setup steps (e.g., dealing extra Character Cards for Thief setup, dividing groups for Prejudiced Manipulator).
5.  The Moderator informs the helper app which Roles are included in the Role Composition.
6.  The Moderator starts the Game Session in the helper app, providing Player names.
7.  (If applicable) The app may prompt the Moderator for initial known information (e.g., Sheriff election, initial Role reveals like Thief/Cupid during Night 1).
8.  (If using New Moon Events) Shuffle the physical New Moon Event deck and place it face down.

## Game Flow

The Game Session alternates between Night Phase and Day Phase. The helper app guides the Moderator through the phases and prompts for input when information needs to be recorded.

Win conditions are evaluated during Victory Check Windows: after Night eliminations and related cascades are resolved at Dawn, and after Day vote resolution and related cascades are resolved before the village is sent to sleep. The pre-Night Victory Check Window is not a separate phase.

### Night Phase

1.  **Village Sleeps:** Moderator instructs all Players to close their eyes.
2.  **Role Actions:** Moderator calls Roles/groups one by one in the specified order (see Turn Order Summary), guided by the helper app.
3.  **Role Identification:** For Roles called on Night 1 (Thief, Cupid, Seer, etc.), the app will prompt the Moderator to identify which Player performed the action, thereby recording that Player's Role in the app.
4.  Called Players open their eyes, silently perform their action (often pointing), and close their eyes again. The Moderator inputs the results of actions (targets, choices) into the helper app when prompted.
5.  Special effects from active New Moon Events might modify this phase, and the app will reflect these modifications in its prompts.

### Day Phase

1.  **Village Wakes:** Moderator instructs all Players to open their eyes (guided by the app).
2.  **(If using New Moon Events, after Day 1)** Draw the top physical New Moon Event card. The Player most recently eliminated (or another designated Player) reads it aloud. The Moderator inputs the drawn New Moon Event into the app, which then applies its effects and adjusts subsequent prompts.
3.  **Victims Revealed:** Based on recorded Night Actions, the app informs the Moderator which Player(s) were eliminated. The Moderator announces this to the Players.
4.  **Role Reveal on Elimination:** When a Player is eliminated (night or day), they reveal their physical Character Card. The Moderator inputs the revealed Role into the helper app, updating the app's knowledge of the Game Session state.
5.  Special Role effects triggered by victim reveal occur now (e.g., Bear Tamer). The app may prompt for related actions or information. New Moon Event effects might alter this step (e.g., Burial prevents Role reveal, Miracle saves victim).
6.  **Debate:** Players discuss suspicions. The app tracks the phase but doesn't directly participate. New Moon Event effects might alter this step, and the app may remind the Moderator of active rules (e.g., Eclipse, Good Manners, Not Me - Nor Wolf).
7.  **Vote:**
    *   Moderator calls for a vote (guided by the app). New Moon Event effects might replace or modify the standard vote; the app will prompt for the appropriate input format (standard votes, accusations, friend votes, etc.).
    *   Standard Vote: All living Players simultaneously point at one Player they wish to eliminate. Moderator inputs the vote counts into the app.
    *   The app calculates the result (considering Sheriff's double vote, ties).
    *   Ties may trigger specific Roles (Scapegoat - app prompts for Scapegoat's decision) or result in no elimination.
    *   The app indicates the eliminated Player. The Player reveals their Character Card (unless Executioner is active). Moderator inputs the revealed Role into the app.
    *   Special Role effects triggered by elimination occur now (e.g., Hunter's shot - app prompts for target; passing Sheriff Status Effect - app prompts for successor).
8.  **Resolve Victory Check Window:** Based on the known Roles and Player Status Effects, the app checks whether a win condition has been met and informs the Moderator. If not, proceed to the next Night Phase (app prompts accordingly).

## Character Roles

*(Note: Some Roles depend on specific expansions/components like New Moon Events, as indicated)*

### The Werewolves

*Goal: Eliminate all non-Werewolf Faction Beneficiaries. When every living non-Werewolf Faction Beneficiary is a Villager Faction Beneficiary, the Werewolf Control Shortcut also treats Werewolves as winning once they have Durable Voting Power control.*

*   **Simple Werewolf:** Each night, wakes with other Werewolf Faction Agents to collectively choose one victim to eliminate. Cannot choose a Werewolf Faction Agent.
*   **Big Bad Wolf:** Each night, wakes and eliminates with other Werewolf Faction Agents. Additionally, wakes again alone and eliminates a second non-Werewolf Faction Agent victim, *unless* any non-temporary Werewolf Faction Agent has already been eliminated. Temporary Werewolf Faction Agents from Full Moon Rising do not count for this condition.
*   **Accursed Wolf-Father:** Each night, wakes and eliminates with other Werewolf Faction Agents. Once per game, instead of eliminating the chosen victim, may choose to infect them. The infected Player gains the Infected Status Effect, secretly becomes a Werewolf Faction Agent, keeps their Faction Beneficiary, keeps any previous night abilities, and wakes with the Werewolf Faction Agents on subsequent nights.

### The Villagers

*Goal: Eliminate all non-Villager Faction Beneficiaries.*

*   **Simple Villager:** No special abilities. Relies on deduction and persuasion.
*   **Villager-Villager:** A Simple Villager Character Card with identical art on both sides, proving the Player's Role if revealed (e.g., by the Seer).
*   **Seer:** Each night, chooses one Player and is given thumbs up or down if they are currently a Werewolf Faction Agent, meaning they act with, wake with, or are perceived with the Werewolves for operational mechanics. This includes the White Werewolf, any Ambiguous Roles currently acting with the Werewolves, and infected Villagers. It does not include a Double Agent, who is a Werewolf Faction Beneficiary but not a Werewolf Faction Agent. Must be discreet. (Effect modified by Somnambulism and Full Moon Rising events).
*   **Cupid:** On the first night only, chooses two Players (can be self) to be the Lovers. If one Lover is Eliminated, the other is immediately Eliminated by heartbreak. Lovers cannot vote against each other. **Special Goal:** If the Lovers have different Faction Beneficiaries when linked, both immediately become Cross-Faction Lovers and win only by being the last two Players alive. Same-Faction Lovers remain only a Status Effect. Cross-Faction Lovers keep their Faction Beneficiary even if later effects change their Role, Faction Agent status, or private information. If both Lovers are Eliminated and only one is revived by Miracle, the revived Player is no longer a Cross-Faction Lover.
*   **Witch:** Has two single-use potions:
    *   **Healing Potion:** Can save the Player targeted by Werewolf Faction Agents that night. Can be used on self.
    *   **Poison Potion:** Can eliminate one Player during the night.
    *   Can use one or both potions in the same night after being informed of the Werewolf Faction Agents' victim.
*   **Hunter:** When eliminated (by vote or night attack), must immediately choose another Player to eliminate with a final shot.
*   **Little Girl:** Can discreetly try to spy (peek) during the Werewolf Faction Agents' turn at night. Cannot be targeted by the Defender.
*   **Defender:** Each night, chooses one Player to protect from the Werewolf Faction Agents' attack for that night only. Cannot protect the same Player two nights in a row. Can protect self. Protection does not work against Witch's poison, Hunter's shot, Piper's charm, or Wolf-Father's infection.
*   **Elder:** Survives the first Werewolf Faction Agent attack against them (Moderator doesn't reveal Character Card). Is eliminated by the second Werewolf Faction Agent attack, or the first time by village vote, Hunter's shot, or Witch's poison. If the Elder is eliminated by the village vote, all Villager Roles, including Actor, lose their special powers for the rest of the game, regardless of those Players' Faction Beneficiaries. This suppression also applies to later Permanent Role Swaps into Villager Roles. Not affected by Wolf-Father infection on the first attempt.
*   **Scapegoat:** If the day's vote results in a tie, the Scapegoat is eliminated instead of the tied Players. If eliminated, the Scapegoat chooses which Player(s) may or may not vote the following day.
*   **Village Idiot:** If the village votes to eliminate the Idiot, they reveal their Character Card and are proven innocent. They remain in the game but can no longer vote. The vote that targeted them is cancelled (no elimination that turn). Still vulnerable to night eliminations.
*   **Two Sisters:** On the first night, wake to recognize each other. May be allowed brief silent communication periods on subsequent nights at Moderator's discretion.
*   **Three Brothers:** On the first night, wake to recognize each other. May be allowed brief silent communication periods on subsequent nights at Moderator's discretion.
*   **Fox:** Each night, may choose a Player. Moderator points to that Player and their immediate neighbors. If at least one Werewolf Faction Agent is among the three, the Moderator gives the Fox an affirmative sign. If no Werewolf Faction Agents are present, the Fox loses their power permanently.
*   **Bear Tamer:** Each morning, after victims are revealed, if a Werewolf Faction Agent is currently sitting immediately next to the Bear Tamer, the Moderator makes a growling sound. (Eliminated Players should move away).
*   **Stuttering Judge:** Once per game, can signal the Moderator (using a pre-agreed sign shown on the first night) during a day vote. If signaled, there will be two consecutive elimination votes that day.
*   **Knight with the Rusty Sword:** If eliminated by Werewolf Faction Agents, the first Werewolf Faction Agent to their left is also eliminated the *following* night due to disease (revealed in the morning).
*   **Actor:** A hard-aligned Villager Role. Each night, chooses one of three face-up Actor Setup Cards (chosen by Moderator during setup from eligible Roles not already included in the Role Composition; must be hard-aligned Villager Roles with actionable individual powers, i.e. excluding Simple Villager, Villager-Villager, Two Sisters, and Three Brothers). Uses that Character Card's power until the next night. Once a Character Card is used, it's removed from play. If the Elder's village-vote penalty is active, Actor loses access to Actor Setup Card powers.

### The Ambiguous

*Goal: Win with their current Faction Beneficiary; their Faction Beneficiary can change.*

*   **Thief:** On the first night, shown the two Character Cards left undealt after random Role distribution. These Character Cards are not chosen or set aside specifically for the Thief. Must swap their Thief Character Card for one of them. If both available Character Cards are Werewolf Roles, *must* take one. This is a Permanent Role Swap: the Thief plays as the chosen Role for the rest of the Game Session, and their Faction Beneficiary becomes that Role's default Faction unless an explicit precedence rule says otherwise. (Requires 2 extra Cards added during setup).
*   **Devoted Servant:** Before an eliminated Player's Character Card is revealed (after the vote), the Servant can reveal their own Character Card. They take the eliminated Player's Character Card (without revealing it) and Role, discarding the Servant Character Card. Their Faction Beneficiary becomes the new Role's default Faction. Any Status Effects and relationship state affecting the eliminated Player (infected, charmed, Sheriff, Lover, etc.) are removed; the Servant starts fresh with the new Role's ability reset. Cannot use if they are a Lover. If the Servant has the Infected Status Effect, it remains infected.
*   **Wild Child:** On the first night, chooses another Player as their role model. Remains a Villager Faction Beneficiary as long as the model is alive. If the model is eliminated, the Wild Child immediately becomes a Werewolf Faction Beneficiary and Werewolf Faction Agent, and wakes with the Werewolf Faction Agents on subsequent nights.
*   **Wolf Hound:** On the first night, decides privately whether to play as a Simple Villager or as a Werewolf for the entire Game Session. If Werewolf, they are a Werewolf Faction Beneficiary and Werewolf Faction Agent, and wake with the Werewolf Faction Agents each night.

### The Loners

*Goal: Achieve their unique objective.*

*   **White Werewolf:** Wakes and eliminates with other Werewolf Faction Agents while remaining a White Werewolf Faction Beneficiary. Every second night, wakes again alone and may eliminate one other Werewolf Faction Agent. The White Werewolf Faction wins when the White Werewolf is the sole surviving Player.
*   **Angel:** If eliminated by the Day 1 vote or during Night 1 or Night 2, the Angel Faction wins alone at the next Victory Check Window. If they do not win by the Dawn Victory Check Window that resolves Night 2, they immediately become a Simple Villager.
*   **Piper:** Each night, charms two Players (cannot charm self). Moderator taps Charmed Players. Charmed Players continue playing normally with their Roles and Faction Beneficiaries but secretly have the Charmed Status Effect. The Piper wins alone if all surviving non-Piper Players are Charmed. Charm is not blocked by Defender/Witch. Charm is not passed between Lovers.
*   **Prejudiced Manipulator:** Before the Game Session, Moderator divides Players into two publicly known groups (based on an arbitrary criterion). The Prejudiced Manipulator wins when no living Players remain in the opposing public group, regardless of those Players' Faction Beneficiaries. Has no special night power.

### Roles Specific to New Moon (Require New Moon Events)

*   **Gypsy:** Each night, uses a "Spiritualism" card provided by the Moderator to ask a yes/no question. The next day, a designated Player (chosen by Gypsy the previous night) asks the question aloud, and the Moderator answers truthfully (yes/no) based on the Game Session state, as if answered by the first Player eliminated. Requires New Moon Event cards.

### Status Effects, Relationships, and New Moon Event Assignments

*   **Sheriff:** Elected by Player vote (usually Day 1, relative majority). Sheriff's vote counts as two. If the Sheriff is eliminated, they choose their successor before Elimination. Cannot refuse the Sheriff Status Effect.
*   **Lovers:** Relationship chosen by Cupid on Night 1. Same-Faction Lovers remain only a Status Effect; Cross-Faction Lovers change Faction Beneficiaries. See Cupid description.
*   **Charmed:** Status Effect chosen by Piper. See Piper description.
*   **(New Moon Assignment)** **Town Crier:** Designated by the Sheriff. Receives a hand of New Moon Event cards (non-Spiritualism) from the Moderator. Each morning, may choose to play one card, reading it aloud as a public announcement. The Sheriff can replace the Town Crier. Requires New Moon Event cards.
*   **(New Moon Event)** **Executioner:** Elected when the New Moon Event card is drawn. Knows the identity of Players eliminated by vote but does not reveal their Character Cards. Can choose to reveal this info verbally. If eliminated, appoints a successor. (Permanent effect).
*   **(New Moon Event)** **Double Agent:** Chosen when the New Moon Event card is drawn. A living non-Werewolf Faction Agent is secretly shown the Werewolf Faction Agents by the Moderator. They become a Werewolf Faction Beneficiary while remaining outside the Werewolf night group. If the chosen Player is already a Cross-Faction Lover, they keep their Cross-Faction Lovers Faction Beneficiary and only gain knowledge of the Werewolf Faction Agents. (Permanent effect).
*   **(New Moon Event)** **Little Rascal:** The youngest Player physically present leaves the room when the New Moon Event card is drawn. Misses debates/votes for one full day/night cycle. Returns the next morning; their vote counts triple from then on. (Temporary effect on Player presence, permanent effect on vote weight).

## New Moon Event Effects

*(Note: These New Moon Event cards are drawn once per day, usually after the first day, and their effects apply as described.)*

*   **Full Moon Rising:** (Temporary Night Effect) For the *next* night only: Werewolf Faction Agents act as Seers (each spies on one Player, and is told that Player's Role if it is known to the Moderator at the time). The Hunter, Witch, and Seer temporarily act as Werewolf Faction Agents for that Night Phase (wake together, eliminate one Player). Their Roles and Faction Beneficiaries do not change. They count for operational Werewolf Faction Agent checks while the temporary effect is active, but do not count for Big Bad Wolf disablement.
*   **Somnambulism:** (Permanent Effect) From now on, when the Seer uses their power, the Moderator publicly announces the *Role* seen, but not *who* was seen. This can stack with **Full Moon Rising**
*   **Enthusiasm:** (Conditional Day Effect) If the *next* Player eliminated by the village vote *is* a Werewolf Faction Agent, a second immediate vote occurs without debate.
*   **Backfire:** (Temporary Night Effect) For the *next* night only: If Werewolf Faction Agents target a Simple Villager, they undergo a Permanent Role Swap to a Werewolf Role instead of being Eliminated (Moderator secretly swaps Character Card). If they target anyone else, the victim survives, and the first Werewolf Faction Agent to the victim's left is eliminated. No effect if Werewolf Faction Agents don't agree on a victim.
*   **Nightmare:** (Replaces Day Vote) Immediately, Players awake. Starting Player (left of last eliminated) accuses one Player. Continues clockwise. Player with most accusations is eliminated. No debate.
*   **Influences:** (Modifies Day Vote) The next vote is sequential. Last eliminated Player chooses first voter. First voter points at target. Player to their left points, and so on. Standard vote resolution applies.
*   **Executioner:** (Permanent New Moon Event Assignment) Village elects an Executioner. Henceforth, Players eliminated by the vote do not reveal their Character Cards; only the Executioner knows their Role (and can choose to lie/tell truth). If eliminated, Executioner names successor.
*   **Double Agent:** (Permanent New Moon Event Assignment) Village sleeps. First eliminated Player chooses a living non-Werewolf Faction Agent. Moderator wakes this Player, points out the Werewolf Faction Agents. The chosen Player becomes a Werewolf Faction Beneficiary while remaining outside the Werewolf night group. If the chosen Player is already a Cross-Faction Lover, they keep their Cross-Faction Lovers Faction Beneficiary and only gain knowledge of the Werewolf Faction Agents.
*   **Great Distrust:** (Replaces Day Vote) Each Player simultaneously points/indicates their 3 "best friends" (using fingers/tokens). Any Player receiving *zero* "friend" votes is eliminated.
*   **Spiritualism (1-5):** (Day Action) A designated Player (Medium) asks the spirit of the first Player eliminated by Werewolf Faction Agents *one* question from the specific Spiritualism card drawn. Moderator answers "Yes" or "No" truthfully. (See specific cards for questions).
*   **Not Me - Nor Wolf:** (Temporary Rule) Until the next vote, Players cannot say the words "wolf" or "me". Violators lose their vote for that turn.
*   **Miracle:** (Victim Effect) The last Player targeted by Werewolf Faction Agents is not eliminated. They remain alive but undergo a Permanent Role Swap to Simple Villager, losing previous Role, powers, and Faction Beneficiary unless an explicit precedence rule says otherwise.
*   **Dissatisfaction:** (Conditional Day Effect) If the *next* Player eliminated by the village vote is *not* a Werewolf Faction Agent, a second immediate vote occurs without debate.
*   **The Little Rascal:** (Temporary Player Removal/Permanent Vote Modifier) The youngest Player leaves the game area for one day/night cycle. Returns next morning, their vote counts as triple thereafter.
*   **Punishment:** (Conditional Day Elimination) Last eliminated Player designates a target. Target is eliminated unless at least 2 other Players vouch by kissing them.
*   **Eclipse:** (Temporary Debate Rule) Players turn their backs to the circle center for the debate phase. Cannot look at each other. Violators lose their vote. Return to normal for the vote itself.
*   **The Specter:** (Night Effect) Moderator touches the next victim chosen by Werewolf Faction Agents, who opens eyes while Werewolf Faction Agents remain awake. Victim undergoes a Permanent Role Swap to a Werewolf Role, chooses one of the *original* Werewolf Faction Agents to be immediately eliminated, and their Faction Beneficiary becomes the new Role's default Faction unless an explicit precedence rule says otherwise. Moderator swaps Character Cards before morning.
*   **Good Manners:** (Temporary Debate Rule) Players must speak in turn during debate, no interruptions. Moderator enforces. Violators lose their vote for this turn.
*   **Burial:** (Permanent Effect) From now on, the identity (Character Card) of Players eliminated by Werewolf Faction Agents at night is never revealed.

## Turn Order Summary (from Page 24 - Excluding Building Dependencies)

### Preparation Before Game

*   Deal Character Cards
*   (If using) Divide Village for Prejudiced Manipulator
*   (If using) Prepare Gypsy's Spiritualism cards, Town Crier's New Moon Event cards, Thief's extra Character Cards, and Actor Setup Cards with actionable individual hard-aligned Villager powers
*   Sheriff Election (can be later in Day 1)
*   (If using New Moon Events) Shuffle New Moon Event deck

### Call Order: 1st Night ONLY

1.  Thief
2.  Actor
3.  Little Girl (identification time)
4.  Cupid
5.  Lovers (recognize each other)
6.  Fox
7.  Stuttering Judge (shows sign to Moderator)
8.  Two Sisters / Three Brothers (recognize each other)
9.  Wild Child (chooses model)
10. Wolf Hound
11. Bear Tamer
12. Defender
13. All Werewolf Faction Agents (including Wolf Hound if chosen Werewolf, White Werewolf, Accursed Wolf-Father, Big Bad Wolf) - choose victim
14. Little Girl (spying time)
15. Accursed Wolf-Father (infection option)
16. Big Bad Wolf (second victim option)
17. Seer
18. Witch (shown victim, uses potions)
19. Gypsy (can choose medium)
20. Piper (charms Players)
21. Charmed Players (tapped by Moderator)

### Call Order: Each Subsequent Night (Subject to New Moon Event modifications, e.g., Full Moon Rising)

1.  Actor
2.  Fox
3.  Defender
4.  All Werewolf Faction Agents (including Wolf Hound if Werewolf, Wild Child if turned, infected Player, White Werewolf, Accursed Wolf-Father, Big Bad Wolf, *or* temporary Werewolf Faction Agents from Full Moon Rising) - choose victim (potential modification by Backfire, Specter)
5.  Little Girl (spying time)
6.  White Werewolf (every *other* night - attacks another Werewolf Faction Agent)
7.  Accursed Wolf-Father (infection option, if unused)
8.  Big Bad Wolf (second victim option, if condition met)
9.  Seer
10. Witch (shown victim, uses potions if available)
11. Gypsy (can choose Medium)
12. Piper (charms Players)
13. Charmed Players (tapped by Moderator)

### Each Day

1.  Village Wakes
2.  **(If using New Moon Events, after Day 1)** Draw and resolve New Moon Event card.
3.  Victims are revealed (unless Burial active, potential modification by Miracle).
4.  Resolve the Dawn Victory Check Window.
5.  Bear Tamer's grunt (if triggered).
6.  Medium chosen by Gypsy performs action (if Spiritualism card drawn).
7.  Town Crier makes announcement (if applicable).
8.  Debate (subject to Eclipse, Good Manners, Not Me - Nor Wolf).
9.  Vote (Standard or modified/replaced by Nightmare, Influences, Great Distrust, Enthusiasm, Dissatisfaction, Punishment) and potential call to Devoted Servant.
10. Angel eligibility is included in each relevant Victory Check Window until it expires immediately after the Dawn Victory Check Window resolving Night 2.
11. Possible second vote (if Stuttering Judge used power, or Enthusiasm/Dissatisfaction triggered) and potential call to Devoted Servant.
12. Resolve the pre-Night Victory Check Window.

The app will guide the Moderator through this call order, prompting for Player identification for Roles revealed during Night 1.
