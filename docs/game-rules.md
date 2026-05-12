# The Werewolves of Miller's Hollow: The Pact - Core Rules & Roles (Excluding Building Dependencies)

## Game Objective

*   **Villagers:** Eliminate all Werewolves.
*   **Werewolves:** Eliminate all Villagers (and other non-Werewolves).
*   **Loners:** Achieve their specific unique objective.
*   **Ambiguous:** Ensure the side they belong to at the end of the game wins. Their side can change.

## Setup

1.  Select a non-playing Game Moderator.
2.  Players draw their Character Card face-down (e.g., from a shuffled deck or bag). The Moderator does *not* initially know player roles unless revealed by game actions.
3.  Players secretly look at their card.
4.  (If applicable) Perform specific physical role setup steps (e.g., dealing extra cards for Thief setup, dividing groups for Prejudiced Manipulator).
5.  The Moderator informs the helper app which roles are included in the game deck.
6.  The Moderator starts the game session in the helper app, providing player names.
7.  (If applicable) The app may prompt the Moderator for initial known information (e.g., Sheriff election, initial role reveals like Thief/Cupid during Night 1).
8.  (If using New Moon Events) Shuffle the physical Event Card deck and place it face down.

## Game Flow

The game alternates between Night and Day phases. The helper app guides the Moderator through the phases and prompts for input when information needs to be recorded.

### Night Phase

1.  **Village Sleeps:** Moderator instructs all players to close their eyes.
2.  **Character Actions:** Moderator calls characters/groups one by one in the specified order (see Turn Order Summary), guided by the helper app.
3.  **Role Identification:** For roles called on Night 1 (Thief, Cupid, Seer, etc.), the app will prompt the Moderator to identify which player performed the action, thereby recording that player's role in the app.
4.  Called players open their eyes, silently perform their action (often pointing), and close their eyes again. The Moderator inputs the results of actions (targets, choices) into the helper app when prompted.
5.  Special effects from active Event Cards might modify this phase, and the app will reflect these modifications in its prompts.

### Day Phase

1.  **Village Wakes:** Moderator instructs all players to open their eyes (guided by the app).
2.  **(If using New Moon Events, after Day 1)** Draw the top physical Event Card. The player most recently eliminated (or another designated player) reads it aloud. The Moderator inputs the drawn event into the app, which then applies its effects and adjusts subsequent prompts.
3.  **Victims Revealed:** Based on recorded night actions, the app informs the Moderator which player(s) were eliminated. The Moderator announces this to the players.
4.  **Role Reveal on Death:** When a player is eliminated (night or day), they reveal their physical Character Card. The Moderator inputs the revealed role into the helper app, updating the app's knowledge of the game state.
5.  Special character effects triggered by victim reveal occur now (e.g., Bear Tamer). The app may prompt for related actions or information. Event card effects might alter this step (e.g., Burial prevents role reveal, Miracle saves victim).
6.  **Debate:** Players discuss suspicions. The app tracks the phase but doesn't directly participate. Event card effects might alter this step, and the app may remind the Moderator of active rules (e.g., Eclipse, Good Manners, Not Me - Nor Wolf).
7.  **Vote:**
    *   Moderator calls for a vote (guided by the app). Event card effects might replace or modify the standard vote; the app will prompt for the appropriate input format (standard votes, accusations, friend votes, etc.).
    *   Standard Vote: All living players simultaneously point at one player they wish to eliminate. Moderator inputs the vote counts into the app.
    *   The app calculates the result (considering Sheriff's double vote, ties).
    *   Ties may trigger specific roles (Scapegoat - app prompts for Scapegoat's decision) or result in no elimination.
    *   The app indicates the eliminated player. The player reveals their card (unless Executioner is active). Moderator inputs the revealed role into the app.
    *   Special character effects triggered by elimination occur now (e.g., Hunter's shot - app prompts for target; passing Sheriff role - app prompts for successor).
8.  **Check Victory Conditions:** Based on the known roles and player statuses, the app checks if a winning condition has been met and informs the Moderator. If not, proceed to the next Night Phase (app prompts accordingly).

## Character Roles

*(Note: Some roles depend on specific expansions/components like Event Cards, as indicated)*

### The Werewolves

*Goal: Eliminate all non-Werewolves.*

*   **Simple Werewolf:** Each night, wakes with other Werewolves to collectively choose one victim to eliminate. Cannot choose a Werewolf.
*   **Big Bad Wolf:** Each night, wakes and eliminates with other Werewolves. Additionally, wakes again alone and eliminates a second (non-Werewolf) victim, *unless* any Werewolf, Wild Child, or Wolf Hound has already been eliminated.
*   **Accursed Wolf-Father:** Each night, wakes and eliminates with other Werewolves. Once per game, instead of eliminating the chosen victim, may choose to infect them. The infected player secretly becomes a Werewolf (keeping any previous night abilities) and wakes with the Werewolves on subsequent nights.

### The Villagers

*Goal: Eliminate all Werewolves.*

*   **Simple Villager:** No special abilities. Relies on deduction and persuasion.
*   **Villager-Villager:** A Simple Villager card with identical art on both sides, proving the player's role if revealed (e.g., by the Seer).
*   **Seer:** Each night, chooses one player and is given thumbs up or down if they are currently part of the group of players that wake with the werewolves, for whatever reason (this includes the white werewolf, any ambiguous roles that are currently werewolf-aligned, or an infected villager, etc. ). Must be discreet. (Effect modified by Somnambulism and Full Moon Rising events).
*   **Cupid:** On the first night only, chooses two players (can be self) to be the Lovers. If one Lover dies, the other dies immediately of heartbreak. Lovers cannot vote against each other. **Special Goal:** If the Lovers are from opposing sides (Villager/Werewolf), their goal changes to eliminate everyone else.
*   **Witch:** Has two single-use potions:
    *   **Healing Potion:** Can save the player targeted by the Werewolves that night. Can be used on self.
    *   **Poison Potion:** Can eliminate one player during the night.
    *   Can use one or both potions in the same night after being informed of the Werewolves' victim.
*   **Hunter:** When eliminated (by vote or night attack), must immediately choose another player to eliminate with a final shot.
*   **Little Girl:** Can discreetly try to spy (peek) during the Werewolves' turn at night. Cannot be targetted by the Defender.
*   **Defender:** Each night, chooses one player to protect from the Werewolves' attack for that night only. Cannot protect the same player two nights in a row. Can protect self. Protection does not work against Witch's poison, Hunter's shot, Piper's charm, or Wolf-Father's infection.
*   **Elder:** Survives the first Werewolf attack against them (Moderator doesn't reveal card). Is eliminated by the second Werewolf attack, or the first time by village vote, Hunter's shot, or Witch's poison. If the Elder is eliminated by the village vote, all Villagers lose their special powers for the rest of the game. Not affected by Wolf-Father infection on the first attempt.
*   **Scapegoat:** If the day's vote results in a tie, the Scapegoat is eliminated instead of the tied players. If eliminated, the Scapegoat chooses which player(s) may or may not vote the following day.
*   **Village Idiot:** If the village votes to eliminate the Idiot, they reveal their card and are proven innocent. They remain in the game but can no longer vote. The vote that targeted them is cancelled (no elimination that turn). Still vulnerable to night eliminations.
*   **Two Sisters:** On the first night, wake to recognize each other. May be allowed brief silent communication periods on subsequent nights at Moderator's discretion.
*   **Three Brothers:** On the first night, wake to recognize each other. May be allowed brief silent communication periods on subsequent nights at Moderator's discretion.
*   **Fox:** Each night, may choose a player. Moderator points to that player and their immediate neighbors. If at least one Werewolf is among the three, the Moderator gives the Fox an affirmative sign. If no Werewolves are present, the Fox loses their power permanently.
*   **Bear Tamer:** Each morning, after victims are revealed, if a Werewolf is currently sitting immediately next to the Bear Tamer, the Moderator makes a growling sound. (Eliminated players should move away).
*   **Stuttering Judge:** Once per game, can signal the Moderator (using a pre-agreed sign shown on the first night) during a day vote. If signaled, there will be two consecutive elimination votes that day.
*   **Knight with the Rusty Sword:** If eliminated by Werewolves, the first Werewolf to their left is also eliminated the *following* night due to disease (revealed in the morning).

### The Ambiguous

*Goal: Make their side win; side can change.*

*   **Thief:** On the first night, shown two unused Character Cards. Must swap their Thief card for one of them. If both available cards are Werewolves, *must* take one. Plays as the chosen role for the rest of the game. (Requires 2 extra Villager cards added during setup).
*   **Devoted Servant:** Before an eliminated player's card is revealed (after the vote), the Servant can reveal their own card. They take the eliminated player's card (without revealing it) and role, discarding the Servant card. Any states affecting the eliminated player (infected, charmed, Sheriff, Lover, etc.) are removed; the Servant starts fresh with the new role's ability reset. Cannot use if they are a Lover. If infected, remains infected.
*   **Actor:** Each night, chooses one of three face-up Character Cards (chosen by Moderator during setup, cannot be Werewolf roles). Uses that card's power until the next night. Once a card is used, it's removed from play.
*   **Wild Child:** On the first night, chooses another player as their role model. Remains a Villager as long as the model is alive. If the model is eliminated, the Wild Child immediately becomes a Werewolf and wakes with them on subsequent nights. Wins with Villagers if model survives and WWs lose; wins with WWs if model dies and WWs win.
*   **Wolf Hound:** On the first night, decides privately whether to be a Simple Villager or a Werewolf for the entire game. If Werewolf, wakes with them each night.

### The Loners

*Goal: Achieve their unique objective.*

*   **White Werewolf:** Wakes and eliminates with other Werewolves. Every second night, wakes again alone and may eliminate one Werewolf. Goal: Be the last player alive.
*   **Angel:** If eliminated up until the 2nd turn (by vote on Day 1 or killed Night 1 or 2 *before* Day 2 vote), they immediately win the game alone. If they survive the second turn, they become a Simple Villager.
*   **Piper:** Each night, charms two players (cannot charm self). Moderator taps charmed players. Charmed players continue playing normally with powers/goals but are secretly charmed. The Piper wins alone if all *surviving* players are charmed. Charm is not blocked by Defender/Witch. Charm is not passed between Lovers.
*   **Prejudiced Manipulator:** Before the game, Moderator divides players into two publicly known groups (based on an arbitrary criterion). Goal: Eliminate everyone in the group they *do not* belong to. Has no special night power.

### Characters Specific to New Moon (Require Event Cards)

*   **Gypsy:** Each night, uses a "Spiritualism" card provided by the Moderator to ask a yes/no question. The next day, a designated player (chosen by Gypsy the previous night) asks the question aloud, and the Moderator answers truthfully (yes/no) based on the game state, as if answered by the first player eliminated. Requires New Moon event cards.
*   **Town Crier:** Designated by the Sheriff. Receives a hand of Event Cards (non-Spiritualism) from the Moderator. Each morning, may choose to play one card, reading it aloud as a public announcement. The Sheriff can replace the Town Crier. Requires New Moon event cards.

### Special Roles (Can be held in addition to Character Card)

*   **Sheriff:** Elected by player vote (usually Day 1, relative majority). Sheriff's vote counts as two. If the Sheriff is eliminated, they choose their successor before dying. Cannot refuse the role.
*   **Lovers:** Chosen by Cupid on Night 1. See Cupid description.
*   **Charmed:** Chosen by Piper. See Piper description.
*   **(New Moon Event)** **Executioner:** Elected when event card is drawn. Knows the identity of players eliminated by vote but does not reveal their cards. Can choose to reveal this info verbally. If eliminated, appoints a successor. (Permanent effect).
*   **(New Moon Event)** **Double Agent:** Chosen when event card is drawn. A non-Werewolf player secretly shown the Werewolves by the Moderator. Remains a Villager but wins if the Werewolves win. Does not wake with Werewolves. (Permanent effect).
*   **(New Moon Event)** **Little Rascal:** The youngest player physically present leaves the room when the event card is drawn. Misses debates/votes for one full day/night cycle. Returns the next morning; their vote counts triple from then on. (Temporary effect on player presence, permanent effect on vote weight).

## New Moon Event Card Effects

*(Note: These cards are drawn once per day, usually after the first day, and their effects apply as described.)*

*   **Full Moon Rising:** (Temporary Night Effect) For the *next* night only: Werewolves act as Seers (each spies on one player, and is told that player's role if it is known to the moderator at the time). The Hunter, Witch, and Seer become temporary Werewolves (wake together, eliminate one player). Roles revert the following morning.
*   **Somnambulism:** (Permanent Effect) From now on, when the Seer uses their power, the Moderator publicly announces the *role* seen, but not *who* was seen. This can stack with **Full Moon Rising**
*   **Enthusiasm:** (Conditional Day Effect) If the *next* player eliminated by the village vote *is* a Werewolf, a second immediate vote occurs without debate.
*   **Backfire:** (Temporary Night Effect) For the *next* night only: If Werewolves target a Simple Villager, she transforms into a Werewolf instead of being eliminated (Moderator secretly swaps card). If they target anyone else, the victim survives, and the first Werewolf to the victim's left is eliminated. No effect if Werewolves don't agree on a victim.
*   **Nightmare:** (Replaces Day Vote) Immediately, players awake. Starting player (left of last eliminated) accuses one player. Continues clockwise. Player with most accusations is eliminated. No debate.
*   **Influences:** (Modifies Day Vote) The next vote is sequential. Last eliminated player chooses first voter. First voter points at target. Player to their left points, and so on. Standard vote resolution applies.
*   **Executioner:** (Permanent Role Assignment) Village elects an Executioner. Henceforth, players eliminated by the vote do not reveal their cards; only the Executioner knows their role (and can choose to lie/tell truth). If eliminated, Executioner names successor.
*   **Double Agent:** (Permanent Role Assignment) Village sleeps. First eliminated player chooses a living non-Werewolf. Moderator wakes this player, points out the Werewolves. Player becomes a secret ally (wins with WWs but remains Villager).
*   **Great Distrust:** (Replaces Day Vote) Each player simultaneously points/indicates their 3 "best friends" (using fingers/tokens). Any player receiving *zero* "friend" votes is eliminated.
*   **Spiritualism (1-5):** (Day Action) A designated player (Medium) asks the spirit of the first Werewolf victim *one* question from the specific Spiritualism card drawn. Moderator answers "Yes" or "No" truthfully. (See specific cards for questions).
*   **Not Me - Nor Wolf:** (Temporary Rule) Until the next vote, players cannot say the words "wolf" or "me". Violators lose their vote for that turn.
*   **Miracle:** (Victim Effect) The last player targeted by the Werewolves is not eliminated. They revive immediately but become a Simple Villager (losing previous role/powers).
*   **Dissatisfaction:** (Conditional Day Effect) If the *next* player eliminated by the village vote is *not* a Werewolf, a second immediate vote occurs without debate.
*   **The Little Rascal:** (Temporary Player Removal/Permanent Vote Modifier) The youngest player leaves the game area for one day/night cycle. Returns next morning, their vote counts as triple thereafter.
*   **Punishment:** (Conditional Day Elimination) Last eliminated player designates a target. Target is eliminated unless at least 2 other players vouch by kissing them.
*   **Eclipse:** (Temporary Debate Rule) Players turn their backs to the circle center for the debate phase. Cannot look at each other. Violators lose their vote. Return to normal for the vote itself.
*   **The Specter:** (Night Effect) Moderator touches the next Werewolf victim, who opens eyes while Werewolves remain awake. Victim becomes a Werewolf, chooses one of the *original* Werewolves to be immediately eliminated. Moderator swaps cards before morning.
*   **Good Manners:** (Temporary Debate Rule) Players must speak in turn during debate, no interruptions. Moderator enforces. Violators lose their vote for this turn.
*   **Burial:** (Permanent Effect) From now on, the identity (card) of players eliminated by the Werewolves at night is never revealed.

## Turn Order Summary (from Page 24 - Excluding Building Dependencies)

### Preparation Before Game

*   Deal Character Cards
*   (If using) Divide Village for Prejudiced Manipulator
*   (If using) Prepare Gypsy's/Town Crier's/Thief's/Actor's cards
*   Sheriff Election (can be later in Day 1)
*   (If using New Moon Events) Shuffle Event Card deck

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
10. Bear Tamer
11. Defender
12. All Werewolves (including Wolf Hound if chosen WW, White Werewolf, Accursed Wolf-Father, Big Bad Wolf) - Choose victim
13. Little Girl (spying time)
14. Accursed Wolf-Father (infection option)
15. Big Bad Wolf (second victim option)
16. Seer
17. Witch (shown victim, uses potions)
18. Gypsy (can choose medium)
19. Piper (charms players)
20. Charmed players (tapped by Moderator)

### Call Order: Each Subsequent Night (Subject to Event Card modifications, e.g., Full Moon Rising)

1.  Actor
2.  Fox
3.  Defender
4.  All Werewolves (including Wolf Hound if WW, Wild Child if turned, infected player, White WW, Accursed WF, Big Bad Wolf, *or* Temp WWs from Full Moon Rising) - Choose victim (potential modification by Backfire, Specter)
5.  Little Girl (spying time)
6.  White Werewolf (every *other* night - attacks a Werewolf)
7.  Accursed Wolf-Father (infection option, if unused)
8.  Big Bad Wolf (second victim option, if condition met)
9.  Seer
10. Witch (shown victim, uses potions if available)
11. Gypsy (can choose Medium)
12. Piper (charms players)
13. Charmed players (tapped by Moderator)

### Each Day

1.  Village Wakes
2.  **(If using New Moon Events, after Day 1)** Draw and resolve Event Card.
3.  Victims are revealed (unless Burial active, potential modification by Miracle).
4.  Check victory conditions.
5.  Bear Tamer's grunt (if triggered).
6.  Medium chosen by Gypsy performs action (if Spiritualism card drawn).
7.  Town Crier makes announcement (if applicable).
8.  Debate (subject to Eclipse, Good Manners, Not Me - Nor Wolf).
9.  Vote (Standard or modified/replaced by Nightmare, Influences, Great Distrust, Enthusiasm, Dissatisfaction, Punishment) and potential call to Devoted Servant.
10. Angel wins (only if eliminated on 2nd turn).
11. Possible second vote (if Stuttering Judge used power, or Enthusiasm/Dissatisfaction triggered) and potential call to Devoted Servant.
12. Check victory conditions.

The app will guide the moderator through this call order, prompting for player identification for roles revealed during Night 1.