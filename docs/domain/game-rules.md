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
2.  Build the complete physical deck from the selected Role Composition, including both extra Character Cards required when Thief is selected. Shuffle it, then let Players perform the Physical Deal by drawing one Character Card face-down each. Any Thief extras are therefore part of the random deal and exactly two physical cards remain undealt. The Moderator and app do *not* initially know Player Roles unless a later physical action identifies or reveals them; the app never performs or deduces the live deal.
3.  Players secretly look at their Character Card.
4.  (If applicable) Perform specific physical Role setup steps that occur outside the deal, such as selecting Actor Setup Cards and creating the Prejudiced Manipulator Public Group Partition. The two Thief cards are already the chance-determined physical remainder of the Players' deal; they are observed only if the Thief flow occurs.
5.  The Moderator informs the helper app which Roles are included in the Role Composition and records any public setup artifacts. The app validates these facts but does not generate the deal, Actor Setup Cards, or Public Group Partition.
6.  The Moderator starts the Game Session in the helper app, providing Player names.
7.  (If applicable) The app may prompt the Moderator for initial known information, such as Sheriff election or private Role Identification for Thief and Cupid during Night 1.
8.  (If using New Moon Events) Shuffle the physical New Moon Event deck and place it face down.

## Game Flow

The Game Session alternates between Night Phase and Day Phase. The helper app guides the Moderator through the phases and prompts for input when information needs to be recorded.

Win conditions are evaluated during Victory Check Windows: after Night eliminations and related cascades are resolved at Dawn, and after Day vote resolution and related cascades are resolved before the village is sent to sleep. The pre-Night Victory Check Window is not a separate phase.

### Night Phase

1.  **Village Sleeps:** Moderator instructs all Players to close their eyes.
2.  **Role Actions:** Moderator calls Roles/groups one by one in the specified order (see Turn Order Summary), guided by the helper app.
3.  **Role Identification:** For Roles called on Night 1 (Thief, Cupid, Seer, etc.), the app prompts the Moderator to record which Player physically answered the call. This private observation records the Role in the app; it does not assign a card or publicly reveal the Role.
4.  Called Players open their eyes, silently perform their action (often pointing), and close their eyes again. The Moderator inputs the results of actions (targets, choices) into the helper app when prompted.
5.  Special effects from active New Moon Events might modify this phase, and the app will reflect these modifications in its prompts.

### Day Phase

1.  **Village Wakes:** Moderator instructs all Players to open their eyes (guided by the app).
2.  **(If using New Moon Events, after Day 1)** Draw the top physical New Moon Event card. The Player most recently eliminated (or another designated Player) reads it aloud. The Moderator inputs the drawn New Moon Event into the app, which then applies its effects and adjusts subsequent prompts.
3.  **Pending Victims Announced:** Based on recorded Night Actions, the app tells the Moderator which Player or Players are pending physical Elimination. The Moderator announces them, but Core has not yet committed those Eliminations.
4.  **Role Reveal before Elimination:** Each pending victim publicly shows the applicable physical Character Card unless an explicit pre-reveal rule intercepts first. If the current Role is already Moderator-known, the Moderator acknowledges the physical reveal; otherwise the Moderator records a complete valid mapping for every requested Player. Either path commits public knowledge without changing current Role.
5.  Core now commits each actual Elimination or rule-defined replacement and drains all required reveal, heartbreak, Hunter, and other reactions in the same Elimination Cascade before phase navigation. New Moon Event effects may explicitly replace the normal reveal or Elimination boundary.
6.  **Debate:** Players discuss suspicions. The app tracks the phase but doesn't directly participate. New Moon Event effects might alter this step, and the app may remind the Moderator of active rules (e.g., Eclipse, Good Manners, Not Me - Nor Wolf).
7.  **Vote:**
    *   Moderator calls for a vote (guided by the app). New Moon Event effects might replace or modify the standard vote; the app will prompt for the appropriate input format (standard votes, accusations, friend votes, etc.).
    *   Standard Vote: All eligible living Players vote using the physical procedure in force. The Moderator resolves that physical Vote, including any voting-power modifiers, then records only the final living target or an empty selection for a tie.
    *   The app validates and records the Moderator-authoritative final result. It does not collect individual ballots or calculate vote counts in this program.
    *   Ties may trigger specific Roles (Scapegoat - app prompts for Scapegoat's decision) or result in no elimination.
    *   The app fixes the Vote target. Any pre-reveal interception runs first; otherwise the target completes generic public reveal. Core then commits or cancels the Elimination under the applicable Role rule and drains the complete Elimination Cascade (for example heartbreak, Hunter's shot, or passing Sheriff) before another Vote or Victory Check Window.
    *   Resolve the Vote's result and its complete Elimination Cascade before continuing.
    *   If a valid Stuttering Judge signal requires a Consecutive Vote, hold it immediately after the first Vote's Elimination Cascade, regardless of the first Vote's outcome. Do not hold another Debate or resolve a Victory Check Window between the Votes. The Consecutive Vote uses the living Players and voting rights then in force.
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
*   **Hunter:** Whenever actually Eliminated, regardless of the Elimination reason, the Hunter's available Role Power triggers exactly once and must eliminate another living Player with one final shot. A prevented or cancelled Elimination does not trigger the shot; active Role Power Suppression prevents a later Hunter trigger.
*   **Little Girl:** Can discreetly try to spy (peek) during the Werewolf Faction Agents' turn at night. Cannot be targeted by the Defender.
*   **Defender:** Each night when the Role Power is available, chooses one Player to protect from the Werewolf Faction Agents' physical attacks for that night only. Cannot protect the same Player on two consecutive Nights that both contain Defender protection, and can protect self. Any Night without Defender protection breaks that sequence, so the previously protected Player is eligible at the next active call. Protection does not work against Witch's poison, Hunter's shot, Piper's charm, or Wolf-Father's infection.
*   **Elder:** Survives the first Werewolf Faction Agent attack against them (Moderator doesn't reveal Character Card). Is eliminated by the second Werewolf Faction Agent attack, or the first time by village vote, Hunter's shot, or Witch's poison. If the Elder is eliminated by the village vote, Role Power Suppression begins after that Vote's complete Elimination Cascade and before any Consecutive Vote. It prevents every chosen, automatic, reactive, passive, or communication-based Role Power of all current and future Villager Roles, including Actor, from beginning or triggering, regardless of those Players' Faction Beneficiaries. It does not remove Role identity or undo effects already learned, committed, or resolved. Not affected by Wolf-Father infection on the first attempt.
*   **Scapegoat:** If a Vote results in a tie, the Scapegoat is eliminated instead of the tied Players. If eliminated, the Scapegoat chooses which Player(s) may or may not vote the following Day; that restriction does not affect a Consecutive Vote during the current Day.
*   **Village Idiot:** The first time the village votes to eliminate the Idiot, they reveal their Character Card and are proven innocent. That Vote's Elimination is cancelled, they remain in the Game Session, and they immediately lose their voting right. A required Consecutive Vote still occurs: the Village Idiot cannot vote but remains a legal target, and a later Vote eliminates them normally because the pardon has been spent. Still vulnerable to Night Eliminations.
*   **Two Sisters:** On the first night, wake to recognize each other. May be allowed brief silent communication periods on subsequent nights at Moderator's discretion.
*   **Three Brothers:** On the first night, wake to recognize each other. May be allowed brief silent communication periods on subsequent nights at Moderator's discretion.
*   **Fox:** Each night, may decline or choose any living Player, including self. The Moderator checks the duplicate-free set containing that Player and their nearest living Player in each direction around the Seating Order, skipping Eliminated Players. The set normally contains three Players; with exactly two living Players, it contains both. If at least one current Werewolf Faction Agent is present, the Moderator gives an affirmative sign and the Fox keeps the power. If none are present, the Moderator gives a negative sign and the Fox loses the power permanently. Declining gives no sign and preserves the power.
*   **Bear Tamer:** Each morning, after victims are revealed, if a Werewolf Faction Agent is currently sitting immediately next to the Bear Tamer, the Moderator makes a growling sound. (Eliminated Players should move away).
*   **Stuttering Judge:** Once per Game Session, can signal the Moderator (using a pre-agreed sign shown on Night 1) during the first Vote of a Day. A valid signal commits a Consecutive Vote after the first Vote and its complete Elimination Cascade, regardless of that Vote's outcome. The Consecutive Vote occurs without another Debate or an intervening Victory Check Window.
*   **Knight with the Rusty Sword:** If actually eliminated at Dawn by a physical Werewolf attack, wait for that Dawn's complete Elimination Cascade, then scan clockwise from the Knight's fixed seat and snapshot the first surviving eligible Werewolf Faction Agent. Eligibility uses Agent state at that snapshot, including a Player successfully infected during the triggering Night; a temporary Agent from that Night remains eligible for this check. If no eligible Player survives, no disease is scheduled. Otherwise, the snapshotted Player is eliminated by disease at the following Dawn if still living, regardless of later Role or Agent changes; the disease never retargets.
*   **Actor:** A hard-aligned Villager Role. At the Actor call each Night, may decline or select one of three face-up Actor Setup Cards chosen by the Moderator during setup from eligible Roles not already included in the Role Composition. Eligible cards must be hard-aligned Villager Roles with borrowable individual Role Powers, whether chosen, reactive, or passive; Simple Villager, Villager-Villager, Two Sisters, and Three Brothers are excluded. Confirming a selection immediately removes that card from play and activates a fresh Borrowed Role Power until the Actor call at the start of the next Night, even if its action is declined or its trigger never occurs. The complete source power—including benefits, costs, conditions, and consequences—uses its ordinary relative call or trigger; a source Role's native Night 1 setup restriction does not prevent Actor from starting that one-use power on a later Night. Actor remains the Actor Role and keeps the same Faction Beneficiary. While Elder's Role Power Suppression is active, Actor cannot select a new Actor Setup Card or newly use or trigger an already-borrowed power; effects already committed or resolved remain in force.

### The Ambiguous

*Goal: Win with their current Faction Beneficiary; their Faction Beneficiary can change.*

*   **Thief:** On the first night, shown the two Character Cards left undealt after random Role distribution. These Character Cards are not chosen or set aside specifically for the Thief. Must swap their Thief Character Card for one of them. If both available Character Cards are Werewolf Roles, *must* take one. This is a Permanent Role Swap: it takes effect immediately, the Thief plays as the chosen Role for the rest of the Game Session, and their Faction Beneficiary becomes that Role's default Faction unless an explicit precedence rule says otherwise. Continue the canonical Night 1 call order; the acquired Role receives its normal remaining call, including a first-Night-only call, without replaying earlier calls or pre-game setup. (Requires 2 extra Cards added during setup).
*   **Devoted Servant:** Before an eliminated Player's Character Card is revealed (after the Vote), the Servant can reveal and discard their own Character Card, then take the eliminated Player's card without revealing it. This is a Permanent Role Swap: the acquired Role remains hidden, becomes the Servant's current Role and default Faction Beneficiary, and starts fresh when first called on the next Night. The eliminated Player's Status Effects and relationships are not inherited. A Lover cannot use this power. The swap clears Charmed, Sheriff, and Town Crier as explicit parts of the Servant's old identity. Infection remains attached to the Player and continues to make them a Werewolf Faction Agent; an in-force Scapegoat voting restriction likewise continues to apply to that Player.
*   **Wild Child:** On the first night, chooses another Player as their role model. Remains a Villager Faction Beneficiary as long as the model is alive. If the model is eliminated, the Wild Child immediately becomes a Werewolf Faction Beneficiary and Werewolf Faction Agent, and wakes with the Werewolf Faction Agents on subsequent nights.
*   **Wolf Hound:** On Night 1, makes a final private choice to play with the Villagers or Werewolves while retaining the Wolf Hound Role and physical Character Card. The Villager choice makes the Player a Villager Faction Beneficiary and not a Werewolf Faction Agent. The Werewolf choice makes the Player a Werewolf Faction Beneficiary and Werewolf Faction Agent; they immediately join the collective Werewolf action later that Night and wake with the Werewolves each Night thereafter. Explicit beneficiary-precedence rules such as Cross-Faction Lovers still apply. Revealing the Character Card identifies the Role as Wolf Hound but does not reveal the private choice; the app does not announce that choice automatically, while the Moderator may disclose it verbally at their discretion.

### The Loners

*Goal: Achieve their unique objective.*

*   **White Werewolf:** Wakes and eliminates with other Werewolf Faction Agents while remaining a White Werewolf Faction Beneficiary. Has no solo action on Night 1. On Nights 2, 4, 6, and every later even-numbered Game Session Night, wakes again alone and may eliminate one other living Werewolf Faction Agent. The White Werewolf Faction wins when the White Werewolf is the sole surviving Player.
*   **Angel:** If Eliminated by the first Vote of Day 1 or during Night 1 or Night 2, the Angel Faction's win predicate becomes true at the next Victory Check Window. Elimination by a Consecutive Vote on Day 1 does not qualify. If they do not win by the Dawn Victory Check Window that resolves Night 2, they immediately become a Simple Villager.
*   **Piper:** Each Night, must charm distinct eligible Players: exactly two when at least two are available, the sole Player when only one is available, and no Player when none are available. An eligible target is living, is not the Piper, and is not already Charmed; the Piper cannot decline or select the same Player twice. Afterward, all living Charmed Players wake and recognize one another. Charmed Players keep their Roles, Faction Beneficiaries, and powers. At the next Victory Check Window, the Piper Faction predicate is true only if a living Piper Faction Beneficiary exists and every other living Player is Charmed. Charm is not blocked by Defender or Witch and is not passed between Lovers.
*   **Prejudiced Manipulator:** Before the Game Session, the Moderator creates a Public Group Partition based on an arbitrary announced criterion. Both groups must be non-empty, may differ in size, and keep the same Player membership for the full Game Session. At a Victory Check Window, the Prejudiced Manipulator Faction predicate is true only if a living Prejudiced Manipulator Faction Beneficiary exists and no living Player remains in that beneficiary's Opposing Public Group, regardless of those Players' Faction Beneficiaries. A later holder of the Role uses the existing partition and the group opposite that holder. Has no special night power.

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

## Turn Order Summary (Adapted from Page 24, Excluding Building Dependencies and Incorporating Settled Rulings)

### Preparation Before Game

*   Deal Character Cards
*   (If using) Divide Village for Prejudiced Manipulator
*   (If using) Prepare Gypsy's Spiritualism cards, Town Crier's New Moon Event cards, Thief's extra Character Cards, and Actor Setup Cards with borrowable individual hard-aligned Villager Role Powers
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
13. All Werewolf Faction Agents (including Wolf Hound if chosen Werewolf, White Werewolf, Accursed Wolf-Father, Big Bad Wolf) - wake and choose victim; the Little Girl may spy only during this collective wake interval; the White Werewolf has no solo action on Night 1
14. Accursed Wolf-Father (infection option)
15. Big Bad Wolf (mandatory second victim when the power is available and a legal target exists)
16. Seer
17. Witch (shown victim, uses potions)
18. Gypsy (can choose medium)
19. Piper (charms Players)
20. Charmed Players (tapped by Moderator)

### Call Order: Each Subsequent Night (Subject to New Moon Event modifications, e.g., Full Moon Rising)

1.  Actor
2.  Fox
3.  Defender
4.  All Werewolf Faction Agents (including Wolf Hound if Werewolf, Wild Child if turned, infected Player, White Werewolf, Accursed Wolf-Father, Big Bad Wolf, *or* temporary Werewolf Faction Agents from Full Moon Rising) - wake and choose victim; the Little Girl may spy only during this collective wake interval (potential modification by Backfire, Specter)
5.  Accursed Wolf-Father (infection option, if unused)
6.  White Werewolf (Nights 2, 4, 6, and every later even-numbered Night - attacks another living Werewolf Faction Agent)
7.  Big Bad Wolf (mandatory second victim when the power condition and a legal target are present)
8.  Seer
9.  Witch (shown victim, uses potions if available)
10. Gypsy (can choose Medium)
11. Piper (charms Players)
12. Charmed Players (tapped by Moderator)

### Each Day

1.  Village Wakes
2.  **(If using New Moon Events, after Day 1)** Draw and resolve New Moon Event card.
3.  Victims are revealed (unless Burial active, potential modification by Miracle).
4.  Resolve the Dawn Victory Check Window.
5.  Bear Tamer's grunt (if triggered).
6.  Medium chosen by Gypsy performs action (if Spiritualism card drawn).
7.  Town Crier makes announcement (if applicable).
8.  Debate (subject to Eclipse, Good Manners, Not Me - Nor Wolf).
9.  Resolve the first Vote (Standard or modified/replaced by Nightmare, Influences, Great Distrust, Enthusiasm, Dissatisfaction, Punishment), any potential call to Devoted Servant, and its complete Elimination Cascade.
10. If a rule requires a Consecutive Vote (a valid Stuttering Judge signal or an applicable New Moon Event), resolve it and its complete Elimination Cascade without another Debate or an intervening Victory Check Window.
11. Resolve the pre-Night Victory Check Window. On Day 1, only Elimination by the first Vote qualifies the Angel; Elimination by a Consecutive Vote does not.

The app will guide the Moderator through this call order, prompting for private Role Identification for Roles called during Night 1.
