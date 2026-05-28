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

**Rules Role Set**:
The Roles described by the physical rules in `docs/game-rules.md`, regardless of whether the app has implemented them.
_Avoid_: Supported roles, implemented roles

**Implemented Role Set**:
The Roles with working engine behavior in the app.
_Avoid_: Rules Role Set, selectable roles

**Simulator Profile Role Set**:
The Roles the active simulator profile can execute under its configured setup artifacts and baseline decision behavior.
_Avoid_: Implemented Role Set, selectable roles

**Selectable Role Set**:
The Roles exposed to the Moderator in the current role-selection UI.
_Avoid_: Rules Role Set, implemented roles

**Role Composition**:
The multiset of Roles selected for the physical game deck before a Game Session starts, independent of which Player receives each Role. Includes the two extra Character Cards required by Thief; excludes Actor Setup Cards, New Moon Events, Player names, Seating Order, Status Effects, and setup choices.
_Avoid_: Combination, setup (too broad), assignment (implies Player-specific Role knowledge)

**Rules-Valid Role Composition**:
A Role Composition that satisfies the physical game rules for card count, role counts, and required hard-aligned Faction coverage, without considering whether the app or active simulator profile implements every included Role.
_Avoid_: Valid (too broad), app-supported

**App-Supported Role Composition**:
A Rules-Valid Role Composition that falls within the app's product support boundaries, including Supported Player Count and supported feature scope.
_Avoid_: Rules-valid, simulator-supported

**Actor Setup Cards**:
The three face-up Character Cards selected by the Moderator during setup for the Actor to borrow powers from. Actor Setup Cards must be hard-aligned Villager Roles with actionable individual powers that are not already part of the Role Composition. Actor Setup Cards are not part of the Role Composition and do not contribute Starting Factions or Possible Factions. The Actor Role itself is a hard-aligned Villager Role.
_Avoid_: Actor Role Composition, Actor deck

**Simulation Scenario**:
The complete pre-game simulator input used for lobby-level cache lookup or pre-game simulation. A Simulation Scenario always includes a canonical Role Composition and may also include setup artifacts or non-default assumptions, such as Actor Setup Cards, New Moon Event support, or a non-default Prejudiced Manipulator group model. Profile defaults, such as the baseline even split for Prejudiced Manipulator, do not need to be repeated in every Simulation Scenario.
_Avoid_: Role Composition (when setup artifacts are also included), setup (too broad)

**Canonical Role Composition**:
The stable string representation of a Role Composition for cache keys, simulation scenarios, and replay evidence. It contains non-zero Role counts only, sorted alphabetically by exact enum identifier, using enum identifiers rather than localized names. It counts every physical Role card in the Role Composition, including Thief extras, and never includes Actor Setup Cards.
_Avoid_: Display role list, localized composition

**Canonical Simulation Scenario**:
The stable string representation of a Simulation Scenario. It includes Player count separately from the Canonical Role Composition because Thief can make card count differ from Player count. It also includes setup artifacts or non-default assumptions that affect simulation, such as Actor Setup Cards, while leaving profile defaults implicit in the profile/version.
_Avoid_: Canonical Role Composition (when Player count or setup artifacts are included)

**Simulator-Supported Simulation Scenario**:
A Simulation Scenario that the active simulator profile can execute with implemented Roles, setup artifacts, and baseline decision behavior.
_Avoid_: App-supported Role Composition, cacheable

**Cacheable Simulation Scenario**:
A Simulator-Supported Simulation Scenario that is eligible for offline cache generation and lookup.
_Avoid_: Role Composition, simulator-supported

**Minimum Viable Role Composition**:
The smallest Role Composition the app treats as a meaningful Game Session: 5 Players, exactly one hard-aligned Werewolf Role, and four hard-aligned Villager Roles. Ambiguous Roles and Loner Roles are not meaningful at this size.
_Avoid_: Starter deck, tutorial setup

**Already-Decided Role Composition**:
A Role Composition where at least one Faction would already win at Lobby Exit based only on the Role Composition, before random assignment, setup artifacts, simulation, or Turn 1 choices.
_Avoid_: Simulated loss, failed run

**Degenerate Simulation Scenario**:
A legal, supported Simulation Scenario whose 1,000-run baseline screening simulation completes every run and only observes Game Sessions ending by the end of Turn 1, before Players get meaningful agency.
_Avoid_: Degenerate Role Composition, invalid (ambiguous with rules-invalid), failed simulation, mathematically proven early ending

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

**Exclusive Outcome Bucket**:
The canonical bucket for one completed Game Session Outcome in mutually exclusive aggregation. A single-Faction win is bucketed by that one Faction, a Shared Victory Outcome is bucketed by the sorted set of winning Factions, and a No-Winner Outcome is bucketed separately. Exclusive Outcome Buckets are used to count outcomes that cannot overlap with each other.
_Avoid_: Faction row, winner key

**Exclusive Outcome Share**:
The probability of each mutually exclusive Game Session Outcome, including single-Faction wins, Shared Victory Outcome buckets, and No-Winner Outcome. Exclusive Outcome Shares aggregate completed runs by Exclusive Outcome Bucket and sum to 100% across completed runs.
_Avoid_: Faction probability, win rate

**Starting Faction**:
A Faction represented in the Role Composition before Turn 1 as a stable win-condition beneficiary. Starting Factions are counted by Initial Faction Count even when a setup branch means that Faction never appears in a particular Game Session.
_Avoid_: Initial Faction (ambiguous with Initial Faction Count), default side

**Possible Faction**:
A Faction implied by the Roles present in the Moderator-selected Role Composition, regardless of whether that Faction appears as a beneficiary in a particular Game Session or simulation batch. Possible Factions are listed in probability output even when their observed win rate is 0%, because never coming into being is useful balance feedback.
_Avoid_: Observed Faction, winning Faction

**Hard-Aligned Role**:
A Role whose starting win condition belongs to either the Villager Faction or the Werewolf Faction without depending on setup choices, Status Effects, or Events. For Role Composition validation, hard-aligned Werewolf Roles are Simple Werewolf, Big Bad Wolf, and Accursed Wolf-Father. Hard-aligned Villager Roles are Simple Villager, Villager-Villager, Seer, Cupid, Witch, Hunter, Little Girl, Defender, Elder, Scapegoat, Village Idiot, Two Sisters, Three Brothers, Fox, Bear Tamer, Stuttering Judge, Knight with the Rusty Sword, and Actor. Gypsy is hard-aligned Villager only in New-Moon-enabled Simulation Scenarios. White Werewolf is not hard-aligned Werewolf because it is a White Werewolf Faction Beneficiary.
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
The canonical string used to identify a simulated run's random choices, including simulator version, strategy/profile, run number, Player count, Role Composition, and Simulation Scenario assumptions, with profile defaults implicit in the strategy/profile version. Run Seed Material is stable replay evidence for both Completed and Incomplete Simulation Runs.
_Avoid_: Seed (ambiguous with PRNG integer), random key

**Completed Simulation Run**:
A simulation run that reaches exactly one Game Session Outcome. A run ending during Turn 1 is still a Completed Simulation Run when win-condition resolution produced a Game Session Outcome.
_Avoid_: Successful run (ambiguous with desirable outcome), valid game

**Incomplete Simulation Run**:
A simulation run that does not reach a Game Session Outcome because the simulator could not generate, drive, or finish the run. Incomplete Simulation Runs are not Game Session Outcomes, are not No-Winner Outcomes, and do not contribute to Faction Win Probability or Exclusive Outcome Share.
_Avoid_: Draw, degenerate run, early ending

**Simulation Result Evidence**:
The stable domain evidence reported by a simulation run or batch: the Simulation Scenario/profile identity, run count evidence, one minimal source record per attempted run, Completed versus Incomplete Simulation Run counts, completed Game Session Outcomes, ending Turns and Victory Check Windows, Run Seed Material needed for replay evidence, the Simulation Scenario's Possible Faction inventory, and source data sufficient to derive aggregate Faction and outcome views. Final Player/Faction state snapshots, full transcripts, instruction counts, exception details, timing, memory, raw engine traces, and driver limits are diagnostics rather than Simulation Result Evidence.
_Avoid_: Debug log, transcript

**Team** _(deprecated — use Faction for win-condition grouping)_:
Legacy term still present in the codebase (`Team` enum). Refers to runtime allegiance — who wakes together, who can target whom. Being migrated toward Faction as the canonical concept.
_Avoid_: Using in new domain discussions; prefer Faction

**Role Group**:
A classification bucket for Roles: Werewolves, Villagers, Ambiguous, Loners, New Moon. Used for UI grouping and validation, not for determining Team allegiance at runtime.
_Avoid_: Faction (overloaded with Team)

**Status Effect**:
A persistent condition applied to a Player that modifies their state or abilities. Multiple can stack (e.g., Sheriff + Infected + Charmed). Tracked as flags.
_Avoid_: Buff, debuff, modifier, secondary role (historically used, now unified under Status Effect)

**New Moon Assignment**:
A non-Role responsibility or state introduced by New Moon rules or Events, such as Town Crier, Executioner, Double Agent, or Little Rascal. New Moon Assignments are not part of the Role Composition.
_Avoid_: Role, Character Card

### Game Flow

**Game Session**:
A single game instance from configuration through to victory. Owns all state for one game.
_Avoid_: Game, match, room

**Lobby Exit**:
The boundary where the Moderator attempts to leave pre-game configuration and start the physical Game Session with the selected Role Composition.
_Avoid_: Game start (too broad), setup complete

**Simulation Start State**:
The fully defined Game Session state from which a simulation batch begins. A pre-game Simulation Start State can be derived from a Simulation Scenario; a mid-game Simulation Start State can be captured from an in-progress Game Session once that state is representable.
_Avoid_: Role Composition (when current Game Session state matters), snapshot (too implementation-specific)

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
- A **Role Composition** contains one **Role** per **Player**, plus two extra undealt Character Cards when Thief is present; Actor does not change **Role Composition** size
- **Actor Setup Cards** are selected through a separate setup flow and are not part of the **Role Composition**
- Actor is a hard-aligned Villager **Role**; **Actor Setup Cards** provide borrowed powers only and do not change the Actor's **Faction Beneficiary**
- Actor counts toward hard-aligned Villager **Role** requirements for supported **Role Compositions**
- **Actor Setup Cards** must be hard-aligned Villager **Roles** with actionable individual powers that are not already selected in the **Role Composition**; Simple Villager, Villager-Villager, Two Sisters, and Three Brothers are not eligible
- If Actor is in the **Role Composition**, the app must require at least three eligible **Actor Setup Cards** to remain outside the **Role Composition** before setup can advance
- **New Moon Events**, Player names, **Seating Order**, **Status Effects**, Sheriff, Lovers, Charmed, Prejudiced Manipulator groups, and physical traits such as youngest Player are not part of the **Role Composition**
- New-Moon-dependent Roles and Event effects are outside the v1 simulator scope unless a **Simulation Scenario** explicitly includes New Moon support
- Cache and simulation inputs use a **Simulation Scenario** when setup artifacts or profile assumptions matter beyond the **Role Composition**
- The simulator runs from a **Simulation Start State**; pre-game cache generation derives that state from a **Simulation Scenario**, while mid-game projection can use the same simulation mechanism from a later fully defined Game Session state
- **Degenerate Simulation Scenario** classification applies to the **Simulation Scenario**; each screening run derives its own seeded pre-game **Simulation Start State** from that scenario, including random assignment and profile/default setup choices
- **Canonical Role Composition** omits zero-count Roles, uses exact enum identifiers rather than localized names, and sorts Role entries alphabetically by enum identifier
- **Canonical Simulation Scenario** includes `players=N` separately from **Canonical Role Composition** because Thief can make Role card count differ from Player count
- Prejudiced Manipulator group splitting is not part of **Role Composition**; an even split is the baseline simulator profile default, and only non-default group models need explicit **Simulation Scenario** material
- Any **Role** present in the Moderator-selected **Role Composition** can contribute **Starting Factions** and **Possible Factions** even if a particular simulation run never assigns that Role
- A **Cacheable Simulation Scenario** is limited by the active **Simulator Profile Role Set**, not the full **Rules Role Set**
- A Role becomes **App-Supported** only when the app can actually guide the Moderator through it; Roles that exist only in the **Rules Role Set** are Rules-Valid but not App-Supported
- **Supported Player Count** caps Players only, not total physical cards; Thief can make a **Role Composition** larger than the Player count, and **Actor Setup Cards** are additional physical cards outside the **Role Composition**
- A **Rules-Valid Role Composition** must include at least one hard-aligned Villager **Role** and at least one hard-aligned Werewolf **Role**, but Simple Villager and Simple Werewolf are not mandatory by role name
- Role-count constraints such as single-copy special Roles, exactly two Sisters, and exactly three Brothers are domain rules; Supported Player Count, currently implemented Roles, New Moon exclusion, and simulator profile support are app/product constraints
- The **Minimum Viable Role Composition** is one hard-aligned Werewolf **Role** and four hard-aligned Villager **Roles**
- A supported **Role Composition** must include at least one Villager hard-aligned **Role** and at least one Werewolf hard-aligned **Role**
- Classification order is: **Rules-Valid Role Composition**, **App-Supported Role Composition**, **Simulator-Supported Simulation Scenario**, **Already-Decided Role Composition**, **Degenerate Simulation Scenario**, then probability simulation
- **Already-Decided Role Composition** classification runs every Faction victory trigger that can be evaluated from the Role Composition alone at **Lobby Exit**; possible Player assignments, setup branches, and Night 1 choices are not evidence for this classification
- **Already-Decided Role Composition** classification does not derive or simulate a **Simulation Start State**
- If multiple Faction victory triggers are true at **Lobby Exit** from Role Composition evidence alone, the **Already-Decided Role Composition** record uses **Shared Victory Outcome** semantics rather than a priority order
- Lobby result lookup can use the **Canonical Simulation Scenario** key for uniform cache access, but an **Already-Decided Role Composition** record must state that its evidence came only from the **Canonical Role Composition**
- Enumeration conceptually starts from **Rules Role Set** plus Player count to generate **Rules-Valid Role Compositions**, then filters to **App-Supported Role Compositions**, **Simulator-Supported Simulation Scenarios**, and **Cacheable Simulation Scenarios**
- Ambiguous **Roles** do not create **Starting Factions**; their choices or later state changes resolve into existing Factions or later outcomes
- Ambiguous **Roles** default to Villager Faction beneficiaries unless their Role definition explicitly says otherwise
- Loner **Roles** do not share a Loner **Faction**; each Loner **Role** defines its own Faction lifecycle
- **Cross-Faction Lovers** are a **Latent Faction**; same-Faction **Lovers** remain only a **Status Effect**
- A Player changing which Faction they benefit from does not create a new **Faction**
- **New Moon Events** do not create **Factions** unless they define a distinct win condition
- Town Crier is a **New Moon Assignment** like Sheriff, not a **Role** in the Role Composition
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
- **Exclusive Outcome Share** aggregates only **Completed Simulation Runs** by **Exclusive Outcome Bucket**: single-Faction wins, sorted Shared Victory Outcome Faction sets, and No-Winner Outcome
- **Faction Win Probability** and **Exclusive Outcome Share** are views derived from **Simulation Result Evidence**; raw simulator run output does not need to materialize those views directly
- A simulation batch's stable source data includes one minimal source record per attempted run: run identity, **Run Seed Material**, completion state, and for **Completed Simulation Runs** the **Game Session Outcome**, ending **Turn**, and ending **Victory Check Window**
- The simulation result contract requires per-run source records, but whether cache artifacts store those records verbatim or compact them is deferred to cache artifact design
- Batch-level **Simulation Result Evidence** includes the Simulation Scenario's **Possible Faction** inventory so unobserved or never-came-into-being Factions can still be shown as 0% probability rows
- Screening and probability batches share the same per-run **Simulation Result Evidence** contract; they differ by batch purpose, requested run count, and interpretation
- Batch-level **Simulation Result Evidence** must support screening and probability interpretation from source data: requested runs, Completed and Incomplete Simulation Run counts, Possible Faction inventory, completed outcomes by ending **Victory Check Window**, completed outcomes by **Exclusive Outcome Bucket**, and winner participation counts derivable from completed outcomes
- **Shared Victory Outcome**, **No-Winner Outcome**, and never-came-into-being **Possible Factions** are represented through the same source evidence rather than special-case summary records
- Simulation result contract design stops at what evidence a run or batch must provide; cache record shape, schema versioning, artifact compression, whether per-run records are retained verbatim, precomputed aggregate projections, and lookup/index layout are cache artifact design concerns
- **Already-Decided Role Composition** classification can only use Faction victory evidence available at **Lobby Exit**; **Degenerate Simulation Scenario** and probability evidence can observe Starting, Possible, Transient, and Latent Factions through completed simulation outcomes
- An **Already-Decided Role Composition** is rejected without simulation
- **Already-Decided Role Composition** records can share **Game Session Outcome** language, including **Shared Victory Outcome**, but they are not simulation runs and do not have **Run Seed Material**, per-run source records, or Completed/Incomplete Simulation Run counts
- A **Degenerate Simulation Scenario** is classified by a 1,000-run baseline screening simulation before running a 10,000-run probability simulation; any incomplete screening run makes the screening batch invalid rather than degenerate
- For **Degenerate Simulation Scenario** classification, "ending by the end of Turn 1" includes completed Game Sessions ending at the Dawn Victory Check Window after Night 1 resolution or at the pre-Night Victory Check Window after Day 1 vote resolution and cascades
- A **Degenerate Simulation Scenario** cache record stores screening evidence, not probability output: the Canonical Simulation Scenario, simulator version/profile, 1,000 requested and completed runs, Turn 1 cutoff definition, aggregate completed outcomes by ending window, and batch seed material
- A failed or incomplete screening batch is a "could not evaluate" state; it is not an **Already-Decided Role Composition**, not a **Degenerate Simulation Scenario**, and does not block **Lobby Exit**
- "Could not evaluate" is a product evaluation state, not a **Game Session Outcome**; it is not included in **Exclusive Outcome Bucket**, **No-Winner Outcome**, **Faction Win Probability**, or degenerate evidence
- A simulation run ending during Turn 1 is a **Completed Simulation Run** when it reaches a **Game Session Outcome**; it is not an **Incomplete Simulation Run**
- **Incomplete Simulation Runs** do not contribute to **Faction Win Probability** or **Exclusive Outcome Share**
- Stable **Simulation Result Evidence** for an **Incomplete Simulation Run** includes replay identity and completion state, while failure details remain diagnostic
- Stable **Simulation Result Evidence** for a completed run includes the **Game Session Outcome**, ending **Turn**, ending **Victory Check Window**, Simulation Scenario/profile identity, and **Run Seed Material**
- Final Player/Faction state snapshots, full transcripts, instruction counts, exception details, timing, memory, raw engine traces, and driver limits are diagnostics rather than stable **Simulation Result Evidence**
- A **Balanced Role Composition** is evaluated by comparing starting Faction win probabilities, not by comparing winning Turn to the **Reference Turn Horizon**
- The Moderator judges whether a Role Composition is balanced; the app only blocks **Already-Decided Role Compositions** and **Degenerate Simulation Scenarios**
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
- "Invalid" simulation inputs — split into **Already-Decided Role Composition** for pre-game win-condition rejection and **Degenerate Simulation Scenario** for legal but probably unfun Turn 1 endings.
- "Simulation failure" vs early game end — resolved: a Game Session ending on Turn 1 is a completed outcome, not a failed simulation; an incomplete screening run invalidates the screening batch and is not evidence for **Degenerate Simulation Scenario** classification.
- "Instruction counts in simulation results" — resolved: instruction counts and driver limits are implementation diagnostics, not stable **Simulation Result Evidence**.
- "Faction count" for balance — resolved: use **Initial Faction Count**, excluding latent or transient Factions from the pre-game balance denominator even if they may appear in simulator outcomes.
- "Ambiguous" Role Group as Faction — resolved: Ambiguous **Roles** do not create a Starting Faction of their own.
- Ambiguous **Role** beneficiary defaults — resolved: Ambiguous Roles default to Villager Faction beneficiaries unless their Role definition explicitly says otherwise.
- "Loners" Role Group as Faction — resolved: Loner **Roles** do not share a Faction; White Werewolf, Piper, Prejudiced Manipulator, and Angel each need their own Faction lifecycle.
- "Cross-team Lovers" — resolved: use **Cross-Faction Lovers**. Only Cross-Faction Lovers create a Latent Faction; same-Faction Lovers remain a Status Effect.
- "New Moon" as Faction — resolved: New Moon Events and Role Groups are not Factions unless a specific effect defines a distinct win condition.
- "Extra Character Cards" as Starting Factions — resolved: any Role present in the Moderator-selected Role Composition can contribute Starting Factions and Possible Factions even if a setup branch means that Role is never assigned in a particular Game Session.
- "Actor cards" as Role Composition — resolved: **Actor Setup Cards** are a separate setup artifact, not part of the Role Composition and not a source of Possible Factions; Actor setup requires three eligible hard-aligned Villager Roles to remain outside the Role Composition.
- "Thief extra cards" as setup artifact — resolved: Thief's two extra Character Cards are part of the **Role Composition**, but they are not preselected as undealt cards before the random deal.
- "Role Composition as full simulator input" — resolved: use **Simulation Scenario** when cache or simulation inputs include setup artifacts or profile assumptions beyond the Role Composition.
- "New Moon Events in Role Composition" — resolved: New Moon Events are outside Role Composition, and New-Moon-dependent simulation is out of v1 scope unless a **Simulation Scenario** explicitly includes New Moon support.
- "Canonical Role Composition" — resolved: count every physical Role card in the Role Composition, including Thief extras, omit zero-count Roles, sort by exact enum identifier, and never include Actor Setup Cards.
- "Player count inference" — resolved: **Canonical Simulation Scenario** and **Run Seed Material** include `players=N` separately from Role counts because Thief changes card count.
- "Prejudiced Manipulator groups in Role Composition" — resolved: group splitting is not part of Role Composition; even split is the baseline simulator profile default, and only non-default group models need explicit **Simulation Scenario** material.
- "Mid-game projection vs pre-game simulation" — resolved: the simulator runs from a **Simulation Start State**. Pre-game cache generation derives that state from a **Simulation Scenario**; mid-game projection uses the same simulation mechanism from a later fully defined Game Session state.
- "Scenario classification vs start states" — resolved: degenerate screening classifies a **Simulation Scenario**, while each completed screening run starts from its own seeded **Simulation Start State** derived from that scenario.
- "Actor as Ambiguous Role" — resolved: Actor is a hard-aligned Villager **Role**, counts toward hard-aligned Villager Role Composition requirements, and **Actor Setup Cards** provide powers only without affecting Faction lifecycle.
- "Valid Role Composition" — resolved: split into **Rules-Valid Role Composition**, **App-Supported Role Composition**, **Simulator-Supported Simulation Scenario**, and **Cacheable Simulation Scenario** instead of overloading "valid."
- "Mandatory Simple Werewolf/Simple Villager" — resolved: the domain requires hard-aligned Werewolf and Villager coverage, not Simple Werewolf or Simple Villager by role name.
- "Already-decided/degenerated on unsupported inputs" — resolved: already-decided and degenerate classification only apply after rules, app, and simulator support checks pass.
- "Supported roles" — resolved: use **Rules Role Set**, **Implemented Role Set**, **Simulator Profile Role Set**, or **Selectable Role Set** depending on the boundary being discussed.
- "Town Crier as Role" — resolved: Town Crier is a **New Moon Assignment** like Sheriff, not part of the **Role Composition**.
- "Player cap vs card count" — resolved: **Supported Player Count** caps Players only; Thief extras and Actor Setup Cards can increase physical card count without increasing Player count.
- "Zero-win Factions" — resolved: probability output includes every **Possible Faction** for the Role Composition, not only Factions observed to win or come into being in simulation.
- "Shared victory probability" — resolved: each winning Faction receives **Faction Win Probability** credit, while **Exclusive Outcome Share** preserves the Shared Victory Outcome as a separate **Exclusive Outcome Bucket**.
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
- "Balance judgment" — resolved: the app surfaces pre-game Faction probabilities for the Moderator to interpret, and only blocks **Already-Decided Role Compositions** and **Degenerate Simulation Scenarios**.
- "Already-decided evidence" — resolved: **Already-Decided Role Composition** means a Role Composition would already trigger a Faction victory at **Lobby Exit** from Role Composition evidence alone.
- "Already-decided shared outcomes" — resolved: if multiple Faction victory predicates are already true at **Lobby Exit**, preserve them as a **Shared Victory Outcome** without priority ordering.
- "Already-decided as simulation result" — resolved: **Already-Decided Role Composition** records share outcome language but are not simulation runs and do not have **Run Seed Material** or per-run simulation evidence.
- "Degenerate threshold" — resolved: do not use a percentage threshold; block legal supported **Simulation Scenarios** when a 1,000-run baseline screening simulation completes every run and only observes Turn 1 endings.
- "Could not evaluate" vs blocked — resolved: incomplete screening is an error state and does not block **Lobby Exit** as already-decided or degenerate.
- "Could not evaluate as outcome" — resolved: "could not evaluate" is a product evaluation state, not a **Game Session Outcome** or probability bucket.
- "Current runtime limits" — resolved: the full Faction model remains the domain contract, but the active **Simulator Profile Role Set** controls which scenarios can be evaluated now; unsupported or unevaluable scenarios are not mislabeled as already-decided or degenerate.
- "Seed" — resolved: store **Run Seed Material** as a canonical string for replay evidence; hash it into a numeric seed only when constructing a random generator.
