# Werewolves Moderator Helper

A mobile app that assists a human **Moderator** running a physical game of "The Werewolves of Miller's Hollow." The app tracks game state, guides the Moderator through phases, and prompts for input — it never replaces the Moderator or makes decisions for them.

## Language

### Game Participants

**Moderator**:
The human operator using the app; guides the physical game and feeds outcomes into the app.
_Avoid_: User, admin, game master, GM

**Player**:
A person seated at the table participating in the game. Identified by name and seating position.
_Avoid_: Participant, character

**Supported Player Count**:
The number of Players the app is expected to validate and support for a Game Session; currently 5-30 Players.
_Avoid_: Sweet spot, recommended size

**Seating Order**:
The clockwise arrangement of Players around the table. Determines adjacency for abilities like Bear Tamer and Rusty Sword.
_Avoid_: Player order, turn order

### Identity & Allegiance

**Role**:
A Player's secret identity, determining their abilities, wake-up schedule, and default allegiance. Drawn from a physical Character Card.
_Avoid_: Character, class, card (when referring to the assigned identity)

**Role Composition**:
The multiset of Roles selected for the physical game deck before a Game Session starts, independent of which Player receives each Role. Includes extra undealt Character Cards required by Thief; excludes Actor Setup Cards.
_Avoid_: Combination, setup (too broad), assignment (implies Player-specific Role knowledge)

**Actor Setup Cards**:
The three face-up Character Cards selected by the Moderator during setup for the Actor to borrow powers from. Actor Setup Cards must be hard-aligned Villager Roles with actionable individual powers. Actor Setup Cards are not part of the Role Composition and do not contribute Starting Factions or Possible Factions. The Actor Role itself is a hard-aligned Villager Role.
_Avoid_: Actor Role Composition, Actor deck

**Minimum Viable Role Composition**:
The smallest Role Composition the app treats as a meaningful Game Session: 5 Players, exactly one hard-aligned Werewolf Role, and four hard-aligned Villager Roles. Ambiguous Roles and Loner Roles are not meaningful at this size.
_Avoid_: Starter deck, tutorial setup

**Already-Decided Role Composition**:
A Role Composition where at least one Faction's win condition is already met before Turn 1 begins.
_Avoid_: Simulated loss, failed run

**Degenerate Role Composition**:
A legal Role Composition whose 1,000-run baseline screening simulation only observes Game Sessions ending by the end of Turn 1, before Players get meaningful agency.
_Avoid_: Invalid (ambiguous with rules-invalid), failed simulation, mathematically proven early ending

**Balanced Role Composition**:
A Role Composition whose starting Factions have similar pre-game win probabilities under the simulator's baseline decision model.
_Avoid_: Fair game (too vague), duration-balanced

**Faction**:
A distinct win condition together with the set of Players who benefit from it being met. A Faction comes into being whenever at least one Player holds a win condition not shared by any existing Faction. Membership can change during the game. Examples: the Villager Faction (all Villagers win when every Werewolf is Eliminated), the Piper Faction (the Piper alone wins when all surviving Players are Charmed).
_Avoid_: Team (implies cooperation — the Piper's Charmed targets aren't allies), side, alignment

**Werewolf Control Shortcut**:
The special case where the Werewolf Faction is treated as having already won because living Werewolf Faction Beneficiaries have daytime voting control over the remaining opposition. This shortcut applies only when every living non-Werewolf Faction Beneficiary is a Villager Faction Beneficiary.
_Avoid_: Werewolf win condition, parity win

**Durable Voting Power**:
A Player's stable voting weight for Werewolf Control Shortcut evaluation. Durable Voting Power includes permanent voting changes already in force, even when they originated from Events, and excludes temporary one-window voting effects from Events or role-triggered vote restrictions. Examples that count once in force: Sheriff double vote, Village Idiot vote loss, Little Rascal triple vote. Examples that do not count: Scapegoat next-day restrictions and temporary Event vote rules.
_Avoid_: Vote count, current poll result

**Game Session Outcome**:
The final result of a Game Session after win-condition resolution. A Game Session Outcome can be a single winning Faction, a Shared Victory Outcome, or a No-Winner Outcome.
_Avoid_: Winning Team, result (too generic)

**Faction Beneficiary**:
A living Player who would win if a specific Faction's win condition is satisfied at the current point in the Game Session. Each Player has exactly one beneficiary Faction at a time in the current ruleset. This is based on the Player's current allegiance and Status Effects, not only their original Role or who they wake with.
_Avoid_: Team member, ally, Role owner

**Faction Agent**:
A living Player who currently acts for, wakes with, is perceived as, or is counted by a Faction for operational mechanics without necessarily benefiting from that Faction's win condition. Examples: White Werewolf is a Werewolf Faction Agent while remaining a White Werewolf Faction Beneficiary; Double Agent is a Werewolf Faction Beneficiary while remaining operationally outside the Werewolf night group.
_Avoid_: Team member, ally, operative

**Permanent Role Swap**:
A Role replacement that permanently changes a Player's Role for the rest of the Game Session. By default, a Permanent Role Swap changes the Player's Faction Beneficiary to the new Role's default Faction unless another rule explicitly takes precedence, such as Cross-Faction Lovers.
_Avoid_: Transformation (too broad), conversion (ambiguous with infection)

**Shared Victory Outcome**:
A Game Session Outcome where multiple Factions win because their win conditions become true in the same Victory Check Window.
_Avoid_: Tie (ambiguous with Vote ties), co-winners (too informal)

**No-Winner Outcome**:
A Game Session Outcome where no Faction wins because no Faction win condition is true and every Player is Eliminated.
_Avoid_: Draw, stalemate

**Faction Win Probability**:
The probability that a Faction is among the winners of a simulated Game Session. Shared Victory Outcomes credit every winning Faction, so Faction Win Probabilities can sum above 100%.
_Avoid_: Outcome share, exclusive probability

**Exclusive Outcome Share**:
The probability of each mutually exclusive Game Session Outcome, including single-Faction wins, Shared Victory Outcome tuples, and No-Winner Outcome. Exclusive Outcome Shares sum to 100% across completed runs.
_Avoid_: Faction probability, win rate

**Starting Faction**:
A Faction represented in the Role Composition before Turn 1 as a stable win-condition beneficiary. Starting Factions are counted by Initial Faction Count even when a setup branch means that Faction never appears in a particular Game Session.
_Avoid_: Initial Faction (ambiguous with Initial Faction Count), default side

**Possible Faction**:
A Faction implied by the Roles present in the Moderator-selected Role Composition, regardless of whether that Faction appears as a beneficiary in a particular Game Session or simulation batch. Possible Factions are listed in probability output even when their observed win rate is 0%, because never coming into being is useful balance feedback.
_Avoid_: Observed Faction, winning Faction

**Hard-Aligned Role**:
A Role whose starting win condition belongs to either the Villager Faction or the Werewolf Faction without depending on setup choices, Status Effects, or Events.
_Avoid_: Basic Role, normal Role, Team Role

**Transient Faction**:
A Faction that can win only during a limited window, then disappears or merges into another Faction. Transient Factions are not counted by Initial Faction Count.
_Avoid_: Temporary Team, short-lived side

**Latent Faction**:
A Faction that is not present at the beginning of a Game Session but can come into being through setup choices, Night Actions, Status Effects, or Events. Latent Factions are not counted by Initial Faction Count.
_Avoid_: Possible Faction, hidden Team

**Initial Faction Count**:
The count of Starting Factions in a Role Composition.
_Avoid_: Possible outcome count, Role Group count

**Reference Turn Horizon**:
A rough descriptive duration baseline equal to Player count divided by Initial Faction Count.
_Avoid_: Expected Turn count, predicted duration

**Run Seed Material**:
The canonical string used to identify a simulated run's random choices, including simulator version, strategy, run number, and Role Composition.
_Avoid_: Seed (ambiguous with PRNG integer), random key

**Team** _(deprecated — use Faction for win-condition grouping)_:
Legacy term still present in the codebase (`Team` enum). Refers to runtime allegiance — who wakes together, who can target whom. Being migrated toward Faction as the canonical concept.
_Avoid_: Using in new domain discussions; prefer Faction

**Role Group**:
A classification bucket for Roles: Werewolves, Villagers, Ambiguous, Loners, New Moon. Used for UI grouping and validation, not for determining Team allegiance at runtime.
_Avoid_: Faction (overloaded with Team)

**Status Effect**:
A persistent condition applied to a Player that modifies their state or abilities. Multiple can stack (e.g., Sheriff + Infected + Charmed). Tracked as flags.
_Avoid_: Buff, debuff, modifier, secondary role (historically used, now unified under Status Effect)

### Game Flow

**Game Session**:
A single game instance from configuration through to victory. Owns all state for one game.
_Avoid_: Game, match, room

**Turn**:
A complete cycle through Night, Dawn, and Day. Numbered starting at 1.
_Avoid_: Round

**Victory Check Window**:
A resolved boundary where the app evaluates all win conditions against the current Game Session state. Victory Check Windows happen at Dawn after Night eliminations and related cascades are resolved, and before the next Night after Day vote resolution and related cascades are resolved. The latter is sometimes conversationally called "dusk," but it is not a separate app phase.
_Avoid_: Instant win check, Dusk Phase

**Night Phase**:
When the Moderator wakes Roles one by one to perform secret actions (attacks, checks, protections). All Players have their eyes closed except when called.
_Avoid_: Night round

**Dawn Phase**:
A resolution phase between Night and Day. The app resolves conflicting Night Actions (e.g., Witch save vs. Defender protection vs. infection) and determines who was eliminated.
_Avoid_: Morning (ambiguous — Dawn is resolution, Day is when players are awake)

**Day Phase**:
When Players open their eyes, victims are announced, debate happens, and the village votes to eliminate a suspect.
_Avoid_: Day round

### Communication Contract

**Moderator Instruction**:
A directive from the app to the Moderator — what to announce publicly, what to do privately, and what input to collect next.
_Avoid_: Prompt, message, command

**Moderator Response**:
Input from the Moderator recording what happened in the physical game — selected players, assigned roles, chosen options, or a confirmation.
_Avoid_: Input, answer, user input

### Game Actions

**Night Action**:
A specific action performed by a Role during the Night Phase (e.g., Werewolf attack, Seer check, Witch save/kill, Defender protect).
_Avoid_: Night event, ability use

**Vote**:
The village's collective decision during the Day Phase to eliminate a suspected Werewolf. Can be modified by events or roles (e.g., Sheriff's double vote, Stuttering Judge's re-vote).
_Avoid_: Poll, election

**Elimination**:
Removing a Player from the game. Has a specific reason (Werewolf attack, day vote, Hunter shot, Lovers heartbreak, etc.).
_Avoid_: Death, kill (too informal — Elimination is the domain term for the state change)

### Special Relationships

**Sheriff**:
An elected Status Effect granting double voting power. Passed to a successor on Elimination.
_Avoid_: Leader, mayor

**Lovers**:
A pair of Players linked by Cupid on Night 1. If one is Eliminated, the other dies of heartbreak. If they become Cross-Faction Lovers, their goal shifts to being the last two alive.
_Avoid_: Couple, pair

**Cross-Faction Lovers**:
Lovers whose current win-condition beneficiaries differ when Cupid links them. Cross-Faction Lovers create a Latent Faction with the goal of being the last two Players alive.
_Avoid_: Cross-team Lovers, mixed Lovers

**New Moon Event**:
An event card drawn daily that temporarily or permanently modifies game rules. Physical cards exist at the table; the app tracks their effects.
_Avoid_: Event card (when referring to the game mechanic), Spirit card

### Persistence

**Serialization**:
Saving the latest stable Game Session recovery snapshot to JSON so it can survive app restarts or device changes.
_Avoid_: Save game, checkpoint

**Rehydration**:
Restoring a Game Session from its serialized stable recovery snapshot. Rehydration restores cached boundary state directly; it does not replay the event log.
_Avoid_: Load game, restore

## Relationships

- A **Game Session** has an ordered list of **Players** in **Seating Order** within the **Supported Player Count**
- A **Game Session** starts from one **Role Composition**
- Each **Player** has exactly one **Role** and zero or more **Status Effects**
- A **Role Composition** contains one **Role** per **Player**, plus extra undealt Character Cards required by Thief
- **Actor Setup Cards** are selected through a separate setup flow and are not part of the **Role Composition**
- Actor is a hard-aligned Villager **Role**; **Actor Setup Cards** provide borrowed powers only and do not change the Actor's **Faction Beneficiary**
- Actor counts toward hard-aligned Villager **Role** requirements for supported **Role Compositions**
- **Actor Setup Cards** must be hard-aligned Villager **Roles** with actionable individual powers; Simple Villager, Villager-Villager, Two Sisters, and Three Brothers are not eligible
- Any **Role** present in the Moderator-selected **Role Composition** can contribute **Starting Factions** and **Possible Factions** even if a particular simulation run never assigns that Role
- The **Minimum Viable Role Composition** is one hard-aligned Werewolf **Role** and four hard-aligned Villager **Roles**
- A supported **Role Composition** must include at least one Villager hard-aligned **Role** and at least one Werewolf hard-aligned **Role**
- Ambiguous **Roles** do not create **Starting Factions**; their choices or later state changes resolve into existing Factions or later outcomes
- Ambiguous **Roles** default to Villager Faction beneficiaries unless their Role definition explicitly says otherwise
- Loner **Roles** do not share a Loner **Faction**; each Loner **Role** defines its own Faction lifecycle
- **Cross-Faction Lovers** are a **Latent Faction**; same-Faction **Lovers** remain only a **Status Effect**
- A Player changing which Faction they benefit from does not create a new **Faction**
- **New Moon Events** do not create **Factions** unless they define a distinct win condition
- Elimination-style Faction win conditions are evaluated against **Faction Beneficiaries**
- In the current ruleset, a Player has exactly one beneficiary Faction at a time; changes such as Cross-Faction Lovers, Wild Child transformation, Wolf Hound choice, Thief swap, Devoted Servant swap, or Double Agent replace the Player's previous beneficiary link
- Infection changes a Player's **Faction Agent** status, not their **Faction Beneficiary**
- A **Permanent Role Swap** changes the Player's **Faction Beneficiary** to the new Role's default Faction unless an explicit precedence rule says otherwise
- Cross-Faction Lovers immediately replace both Lovers' **Faction Beneficiary** links; same-Faction **Lovers** remain only a **Status Effect**
- Cross-Faction Lovers take precedence over later beneficiary-changing effects, including Double Agent, Wild Child transformation, Wolf Hound choice, Thief swap, Specter, and Backfire; the Lover can gain operational state or information without changing **Faction Beneficiary**
- A Lover cannot use Devoted Servant's swap ability
- If Miracle revives only one eliminated Cross-Faction Lover after the Lovers were Eliminated, the revived Player is no longer a Cross-Faction Lover
- Devoted Servant's successful swap changes the Servant's **Faction Beneficiary** to the new Role's default Faction
- The Villager Faction wins when every living non-Villager **Faction Beneficiary** has been Eliminated
- The Werewolf Faction's full win condition is eliminating all other **Faction Beneficiaries**; **Werewolf Control Shortcut** is only a shortcut for Villager-vs-Werewolf endgames
- **Werewolf Control Shortcut** uses **Durable Voting Power**, not temporary vote effects
- The White Werewolf Faction wins when the White Werewolf is the sole surviving Player
- White Werewolf is a Werewolf **Faction Agent** for night targeting and Seer detection, and a White Werewolf **Faction Beneficiary** for win conditions
- Generic rules that check, target, count, or react to "Werewolves" use Werewolf **Faction Agents** unless the rule explicitly says Role or Character Card
- Werewolf group attacks cannot target Werewolf **Faction Agents**
- White Werewolf's solo attack targets another Werewolf **Faction Agent**
- Big Bad Wolf's extra attack is disabled once any non-temporary Werewolf **Faction Agent** has been Eliminated; temporary Werewolf **Faction Agents** from Full Moon Rising do not count
- Temporary Werewolf **Faction Agents** from Full Moon Rising affect operational Werewolf **Faction Agent** checks while active, such as Seer, Fox, Bear Tamer, Knight with the Rusty Sword, and night targeting checks
- Elder's village-vote penalty removes powers from all Villager **Roles**, including Actor, regardless of those Players' **Faction Beneficiaries**
- Elder's village-vote penalty is a continuing suppression of Villager **Role** powers for the rest of the Game Session, so later Permanent Role Swaps into Villager Roles enter with their powers suppressed
- Double Agent can be assigned only to a living non-Werewolf **Faction Agent**
- The Piper Faction wins when every surviving non-Piper Player is Charmed
- The Prejudiced Manipulator Faction wins when every living Player in the opposing public group has been Eliminated, regardless of those Players' **Faction Beneficiaries**
- A Player can be a **Faction Agent** for one Faction while benefiting from another Faction's win condition, such as White Werewolf waking with Werewolves or Double Agent benefiting from Werewolf victory without waking with Werewolves
- The Seer detects Werewolf **Faction Agents**, not Werewolf **Faction Beneficiaries**
- Win conditions are evaluated only during **Victory Check Windows**
- Angel's transient Faction expires immediately after the Dawn **Victory Check Window** that resolves Night 2 if the Angel did not win, and the Angel then becomes a Simple Villager
- During a **Victory Check Window**, all win-condition predicates are evaluated against the same resolved Game Session state; if multiple Factions' predicates are true, the Game Session ends with a **Shared Victory Outcome**
- A **No-Winner Outcome** is evaluated only when no Faction win condition is true in the Victory Check Window and every Player is Eliminated
- A **Game Session** ends with exactly one **Game Session Outcome**
- A **No-Winner Outcome** can occur when mutually assured Elimination leaves no Faction able to win
- The current runtime victory check is a two-Faction `Team` shortcut and is not the complete future **Faction** win-condition model
- Faction lifecycle describes how **Initial Faction Count** is computed; probability output is still reported as Faction win rates
- Probability output lists every **Possible Faction** for the **Role Composition**, including Starting Factions, Transient Factions, and Latent Factions, even when a possible Faction has a 0% observed win rate or never came into being in the simulation batch
- Probability output includes a **No-Winner Outcome** row in addition to **Possible Faction** rows
- **Faction Win Probability** credits every winning Faction in a **Shared Victory Outcome**
- **Exclusive Outcome Share** preserves mutually exclusive outcomes separately, including Shared Victory Outcome tuples
- An **Already-Decided Role Composition** is rejected without simulation
- A **Degenerate Role Composition** is classified by a 1,000-run baseline screening simulation before running a 10,000-run probability simulation
- A **Balanced Role Composition** is evaluated by comparing starting Faction win probabilities, not by comparing winning Turn to the **Reference Turn Horizon**
- The Moderator judges whether a Role Composition is balanced; the app only blocks Already-Decided and Degenerate Role Compositions
- **Initial Faction Count** counts **Starting Factions** and excludes **Transient Factions** and **Latent Factions**
- **Reference Turn Horizon** is derived from **Player** count and **Initial Faction Count**
- **Run Seed Material** is stored as a string and hashed only at the boundary where a random generator needs a numeric seed
- A **Role** belongs to one **Role Group** and determines a default **Team**, but a Player's actual **Team** can change via **Status Effects** (e.g., infection)
- A **Turn** consists of one **Night Phase**, one **Dawn Phase**, and one **Day Phase**, in that order
- During each **Night Phase**, Roles perform **Night Actions** which are resolved during **Dawn Phase**
- During **Day Phase**, the village holds a **Vote** which may result in an **Elimination**
- The app sends **Moderator Instructions** and receives **Moderator Responses** — this is the only communication channel
- **New Moon Events** can modify the behavior of any phase, vote, or role ability

## Example dialogue

> **Dev:** "When we say a Player is 'Eliminated,' does that always mean they're removed from the game?"
> **Domain expert:** "Yes. Elimination is final — the Player is dead and out. The cause varies (Vote, Werewolf attack, Hunter shot) but the outcome is always removal. Don't confuse it with the Village Idiot surviving a Vote — that's not an Elimination, it's immunity preventing the Elimination from happening."

> **Dev:** "Is the Sheriff a Role?"
> **Domain expert:** "No. The Sheriff is a Status Effect — it's layered on top of whatever Role the Player already has. A Player is both a Seer and the Sheriff simultaneously. Same goes for Lovers and Charmed."

> **Dev:** "What's the difference between Dawn and Day?"
> **Domain expert:** "Dawn is a machine phase — the app resolves Night Action conflicts and figures out who died. The Moderator doesn't interact with players during Dawn. Day is when players open their eyes, hear announcements, debate, and vote."

## Flagged ambiguities

- "Character" vs "Role" — resolved: **Role** is the canonical term. "Character Card" refers only to the physical card at the table, never to the in-app concept.
- "Death" vs "Elimination" — resolved: **Elimination** is the domain term for the state change. "Death" is informal and ambiguous (does it include heartbreak? Hunter shot? Rusty Sword?). Elimination covers all cases with an explicit reason.
- "Secondary Role" vs "Status Effect" — resolved: historically roles like Sheriff and Lovers were called "secondary roles." Now unified under **Status Effect** since they're tracked as stackable flags, not as separate role assignments.
- "Input" vs "Response" — resolved: **Moderator Response** is the term for data flowing from the Moderator to the app. "Input" is too generic and collides with UI terminology.
- "Morning" — avoided: too ambiguous. **Dawn** is the resolution phase (app-internal). **Day** is when players are awake and active.
- "Combination" — resolved: use **Role Composition** for pre-game balance and simulation discussions. It does not mean a Player-specific Role assignment or a Seating Order permutation.
- "Supported" vs "recommended" player counts — resolved: the app supports 5-30 Players; product guidance may still describe a narrower ergonomic sweet spot for physical play.
- "Invalid" Role Compositions — split into **Already-Decided Role Composition** for pre-game win-condition rejection and **Degenerate Role Composition** for legal but probably unfun Turn 1 endings.
- "Simulation failure" vs early game end — resolved: a Game Session ending on Turn 1 is a completed outcome, not a failed simulation.
- "Faction count" for balance — resolved: use **Initial Faction Count**, excluding latent or transient Factions from the pre-game balance denominator even if they may appear in simulator outcomes.
- "Ambiguous" Role Group as Faction — resolved: Ambiguous **Roles** do not create a Starting Faction of their own.
- Ambiguous **Role** beneficiary defaults — resolved: Ambiguous Roles default to Villager Faction beneficiaries unless their Role definition explicitly says otherwise.
- "Loners" Role Group as Faction — resolved: Loner **Roles** do not share a Faction; White Werewolf, Piper, Prejudiced Manipulator, and Angel each need their own Faction lifecycle.
- "Cross-team Lovers" — resolved: use **Cross-Faction Lovers**. Only Cross-Faction Lovers create a Latent Faction; same-Faction Lovers remain a Status Effect.
- "New Moon" as Faction — resolved: New Moon Events and Role Groups are not Factions unless a specific effect defines a distinct win condition.
- "Extra Character Cards" as Starting Factions — resolved: any Role present in the Moderator-selected Role Composition can contribute Starting Factions and Possible Factions even if a setup branch means that Role is never assigned in a particular Game Session.
- "Actor cards" as Role Composition — resolved: **Actor Setup Cards** are a separate setup artifact, not part of the Role Composition and not a source of Possible Factions.
- "Actor as Ambiguous Role" — resolved: Actor is a hard-aligned Villager **Role**, counts toward hard-aligned Villager Role Composition requirements, and **Actor Setup Cards** provide powers only without affecting Faction lifecycle.
- "Zero-win Factions" — resolved: probability output includes every **Possible Faction** for the Role Composition, not only Factions observed to win or come into being in simulation.
- "Shared victory probability" — resolved: each winning Faction receives **Faction Win Probability** credit, while **Exclusive Outcome Share** preserves the shared outcome tuple separately.
- "Werewolf parity" — resolved: parity is a **Werewolf Control Shortcut** for Villager-vs-Werewolf endgames, not the Werewolf Faction's full win condition once active solo or latent Factions are present.
- "Operational Faction vs beneficiary Faction" — resolved: use **Faction Beneficiary** for win-condition membership and **Faction Agent** for operational behavior such as waking, acting, or being perceived with a Faction.
- "Seer detection" — resolved: Seer checks whether the target is a Werewolf **Faction Agent**, not whether the target is a Werewolf **Faction Beneficiary**.
- "Dual Faction beneficiaries" — resolved: in the current ruleset, each Player has exactly one beneficiary Faction; beneficiary changes are exclusive replacements even when operational behavior, wake groups, or public identity stay unchanged.
- "Infection beneficiary" — resolved: infection changes **Faction Agent** status but does not change **Faction Beneficiary**.
- "Permanent Role Swap beneficiary" — resolved: permanent Role swaps change **Faction Beneficiary** to the new Role's default Faction unless an explicit precedence rule says otherwise.
- "Cross-Faction Lovers precedence" — resolved: Cross-Faction Lovers keep their beneficiary precedence over later effects; Lover status blocks Devoted Servant's swap ability, and Miracle reviving only one eliminated Lover breaks the Cross-Faction Lovers outcome.
- "Werewolf voting control" — resolved: **Werewolf Control Shortcut** uses **Durable Voting Power** and ignores temporary vote effects.
- "Generic Werewolf checks" — resolved: use Werewolf **Faction Agent** unless a rule explicitly refers to Role or Character Card.
- "Elder power loss" — resolved: the Elder's village-vote penalty affects all Villager **Roles**, including Actor, regardless of **Faction Beneficiary**, and continues to suppress later Villager Role powers.
- "Big Bad Wolf disablement" — resolved: the extra attack is disabled once any non-temporary Werewolf **Faction Agent** has been Eliminated; Full Moon Rising's temporary Werewolf Faction Agents do not count.
- "Temporary Werewolf Faction Agents" — resolved: Full Moon Rising affects operational Werewolf Faction Agent checks while active, but does not change Faction Beneficiaries or Big Bad Wolf disablement.
- "Double Agent eligibility" — resolved: the target must be a living non-Werewolf **Faction Agent**.
- "Angel expiry" — resolved: Angel's transient Faction expires immediately after the Dawn **Victory Check Window** that resolves Night 2 if the Angel did not win.
- "Victory timing" — resolved: win conditions are checked at **Victory Check Windows**: Dawn after Night resolution, and before the next Night after Day resolution. There is no separate Dusk Phase.
- "Win-condition priority" — resolved: evaluate all win-condition predicates in the same **Victory Check Window** before deciding the **Game Session Outcome**.
- "Tie" as final result — resolved: use **Shared Victory Outcome** for multiple Factions winning in the same **Victory Check Window**; keep "tie" for Vote ties.
- "Everybody dies" — resolved: use **No-Winner Outcome** for completed Game Sessions where no Faction wins.
- "Balanced" vs "long enough" — resolved: **Balanced Role Composition** means similar starting Faction win probabilities; **Reference Turn Horizon** is not used to block Role Compositions.
- "Balance judgment" — resolved: the app surfaces pre-game Faction probabilities for the Moderator to interpret, and only blocks Role Compositions that are Already-Decided or Degenerate.
- "Degenerate threshold" — resolved: do not use a percentage threshold; block legal Role Compositions when a 1,000-run baseline screening simulation only observes Turn 1 endings.
- "Seed" — resolved: store **Run Seed Material** as a canonical string for replay evidence; hash it into a numeric seed only when constructing a random generator.
