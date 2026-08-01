# Werewolves Moderator Helper

A mobile app that assists a human **Moderator** running a physical game of "The Werewolves of Miller's Hollow." The app tracks game state, guides the Moderator through phases, and prompts for input — it never replaces the Moderator or makes decisions for them.

This file is the shared glossary for domain language and avoided synonyms.

## Language

### Game Participants

**Moderator**:
The human who guides the physical game and records table facts.
_Avoid_: User, admin, game master, GM

**Player**:
A person seated at the table and participating in the Game Session.
_Avoid_: Participant, character

**Supported Player Count**:
The range of Player counts supported by the product, currently 5–30.
_Avoid_: Sweet spot, recommended size

**Seating Order**:
The clockwise arrangement of Players around the table.
_Avoid_: Player order, turn order

**Living Neighbor**:
The nearest living Player in one direction from a reference Player around the circular Seating Order.
_Avoid_: Adjacent seat, physical neighbor

### Identity & Allegiance

**Role**:
A Player's current rules identity, including its default allegiance and Role Powers.
_Avoid_: Character, class, card (when referring to the assigned identity)

**Living Role Holder**:
A living Player whose current Role is a specified Role.
_Avoid_: Role Composition count, selected Role count, active holder (ambiguous with Role Power availability)

**Physical Character Card Ownership**:
The relationship between a Player and one Character Card from the locked Role Composition. Same-printed copies are interchangeable for ownership bookkeeping; configured Offer 1 and Offer 2 slots remain distinct.
_Avoid_: Current Role, app assignment, known Role, revealed Role, observed copy identity

**Deal Pool**:
The Player-count subset of the Role Composition used for the Physical Deal.
_Avoid_: Player assignment, dealt Roles (before the Physical Deal)

**Thief Offer Cards**:
The two physical Character Card instances reserved outside the Deal Pool for the Thief Player's private choice.
_Avoid_: Undealt Character Cards, random leftovers, Thief options (when referring to the physical cards)

**Set-Aside Character Cards**:
Physical Character Card instances kept face-down outside active play.
_Avoid_: Discarded (for face-down cards), undealt cards

**Discarded**:
The public, out-of-play zone for Character Cards removed from active play. It is distinct from both face-down Set-Aside Character Cards and the historical fact that a card was publicly revealed.
_Avoid_: Set-Aside (for public out-of-play cards), discarded Role

**Physical Deal**:
The face-down random distribution of one Deal Pool card to each Player.
_Avoid_: App assignment, generated deal

**Role Power**:
A gameplay capability granted by a Role beyond its identity and default allegiance.
_Avoid_: Ability (too broad), Role behavior (includes identity and allegiance)

**Role Power Suppression**:
A continuing rule state that prevents new Role Power effects without removing Roles or undoing prior effects.
_Avoid_: Role removal, power reset

**Rules Role Set**:
The Roles defined by the physical rules supported by this project.
_Avoid_: Supported roles, implemented roles

**Implemented Role Set**:
The Roles for which the product has working game behavior.
_Avoid_: Rules Role Set, selectable roles

**Simulator Profile Role Set**:
The Roles a named simulation capability can execute.
_Avoid_: Implemented Role Set, selectable roles

**Simulator Capability**:
A named and versioned boundary for automated Game Session evaluation.
_Avoid_: Active profile (when the capability is not named), simulator support (unqualified)

**Safety-Screening Role Set**:
The Roles supported by pre-game safety screening.
_Avoid_: Simulator Profile Role Set (when capability matters), full simulator support

**Full-Probability Role Set**:
The Roles supported by full probability evaluation.
_Avoid_: Safety-Screening Role Set, app-supported roles

**Selectable Role Set**:
The Roles available for the Moderator to include in a Role Composition.
_Avoid_: Rules Role Set, implemented roles

**Role Composition**:
The pre-game multiset of physical Role cards selected for a Game Session.
_Avoid_: Combination, setup (too broad), assignment (implies Player-specific Role knowledge)

**Rules-Valid Role Composition**:
A Role Composition that satisfies the physical game's setup rules.
_Avoid_: Valid (too broad), app-supported

**App-Supported Role Composition**:
A Rules-Valid Role Composition through which the product can guide a complete Game Session.
_Avoid_: Rules-valid, simulator-supported

**Actor Setup Cards**:
The three face-down Character Cards from which Actor can borrow Role Powers.
_Avoid_: Actor Role Composition, Actor deck

**Borrowed Role Power**:
A temporary instance of a source Role Power used by Actor without changing Actor's Role.
_Avoid_: Temporary Role, copied Role

**Public Group Partition**:
A fixed, publicly announced division of every Player into one of two non-empty groups.
_Avoid_: Prejudiced Manipulator teams, balanced groups

**Opposing Public Group**:
The Public Group Partition block that does not contain the current Prejudiced Manipulator Faction Beneficiary.
_Avoid_: Target group, enemy team, original Manipulator's opposition

**Simulation Scenario**:
The complete set of pre-game facts used to evaluate a Role Composition.
_Avoid_: Role Composition (when setup artifacts are also included), setup (too broad)

**Canonical Role Composition**:
The stable string representation of a Role Composition.
_Avoid_: Display role list, localized composition

**Canonical Simulation Scenario**:
The stable string representation of a Simulation Scenario.
_Avoid_: Canonical Role Composition (when Player count or setup artifacts are included)

**Capability-Supported Simulation Scenario**:
A Simulation Scenario executable by a specified Simulator Capability.
_Avoid_: Simulator-supported (unqualified), App-supported Role Composition, cacheable

**Safety-Screening-Supported Simulation Scenario**:
A Simulation Scenario supported by the safety-screening capability.
_Avoid_: Full-Probability-Supported Simulation Scenario, app-supported setup

**Full-Probability-Supported Simulation Scenario**:
A Simulation Scenario supported by the full-probability capability.
_Avoid_: Safety-Screening-Supported Simulation Scenario, current production support

**Cacheable Simulation Scenario**:
A Simulation Scenario eligible for a stored terminal evaluation under its exact named and versioned Simulator Capability.
_Avoid_: Role Composition, capability-supported, cross-capability record

**Local Fallback Cache Record**:
A terminal lobby evaluation stored on the Moderator's device and valid only for its exact Simulator Capability identity.
_Avoid_: Local simulation evidence, replay cache, transcript cache

**On-Device Fallback Generation**:
A local safety evaluation performed on the Moderator's device.
_Avoid_: Normal pre-game simulation, cache migration

**Minimum Viable Role Composition**:
The smallest Role Composition treated as a meaningful Game Session.
_Avoid_: Starter deck, tutorial setup

**Already-Decided Role Composition**:
A locked Simulation Scenario whose Deal Pool already satisfies at least one Faction's win condition at Lobby Exit.
_Avoid_: Simulated loss, failed run

**Degenerate Simulation Scenario**:
A legal Simulation Scenario whose baseline runs end by the end of Turn 1 before Players have meaningful agency.
_Avoid_: Degenerate Role Composition, invalid (ambiguous with rules-invalid), failed simulation, mathematically proven early ending

**Balanced Role Composition**:
A Role Composition whose Game Result Frequency is not obviously concentrated in one Starting Faction's result.
_Avoid_: Fair game (too vague), duration-balanced

**Faction**:
A distinct win condition together with the Players who currently benefit from it.
_Avoid_: Team (implies cooperation — the Piper's Charmed targets aren't allies), side, alignment

**Werewolf Control Shortcut**:
The Villager-versus-Werewolf endgame rule that treats Werewolves as winning once they control the Day vote.
_Avoid_: Werewolf win condition, parity win

**Durable Voting Power**:
A Player's stable voting weight for Werewolf Control Shortcut evaluation.
_Avoid_: Vote count, current poll result

**Game Session Outcome**:
The final result of a Game Session.
_Avoid_: Winning Team, result (too generic)

**Faction Beneficiary**:
A Player who would win if a specified Faction's win condition were satisfied now.
_Avoid_: Team member, ally, Role owner

**Role-Card Victory Eligibility**:
A time-bounded win condition attached to a physical Character Card and qualifying Game Session history rather than to a Player's Faction Beneficiary. Angel is the current example; its eligibility can join a Shared Victory Outcome without identifying its holder early or changing that holder's Villager Faction Beneficiary.
_Avoid_: Transient Faction, Angel Beneficiary, known Angel holder

**Faction Agent**:
A Player who acts for, wakes with, is perceived as, or is counted by a Faction for operational rules.
_Avoid_: Team member, ally, operative

**Known Faction State**:
The Faction Beneficiary and Faction Agent facts legitimately known about a Player.
_Avoid_: Default team, inferred allegiance, assumed Villager

**Initial Beneficiary Closure**:
The one-time Night 1 rules boundary at which complete committed Faction evidence establishes every still-unknown initial Faction Beneficiary atomically before a dependent rule consumes it.
_Avoid_: Default assignment, inferred allegiance, correction pass

**Permanent Role Swap**:
A Role replacement that lasts for the rest of the Game Session without replacing the Player.
_Avoid_: Transformation (too broad), conversion (ambiguous with infection)

**Shared Victory Outcome**:
A Game Session Outcome in which multiple win conditions are satisfied at the same Victory Check Window.
_Avoid_: Tie (ambiguous with Vote ties), co-winners (too informal)

**No-Winner Outcome**:
A Game Session Outcome in which every Player is Eliminated and no win condition is satisfied.
_Avoid_: Draw, stalemate

**Game Result**:
A mutually exclusive final result category for a completed simulated Game Session.
_Avoid_: Outcome bucket, winner key

**Possible Game Result**:
A Game Result that a particular Simulation Scenario can produce.
_Avoid_: Global result catalog, observed result

**Game Result Frequency**:
The share of Completed Simulation Runs ending in each Game Result.
_Avoid_: win rate

**Game Result Frequency by Turn**:
The share of Completed Simulation Runs ending in each Game Result on each Turn.
_Avoid_: PMF, timing table (too vague)

**Ended-By-Turn Frequency**:
The share of Completed Simulation Runs that have ended by a specified Turn.
_Avoid_: CDF, duration metric

**Unlikely Possible Result**:
A Possible Game Result with a frequency below one percent.
_Avoid_: Impossible result, error bucket

**Starting Faction**:
A Faction represented by a stable beneficiary in the committed Deal Pool before Turn 1.
_Avoid_: Initial Faction (ambiguous with Initial Faction Count), default side

**Possible Faction**:
A Faction reachable from the Roles and choices present in a Simulation Scenario.
_Avoid_: Observed Faction, winning Faction

**Hard-Aligned Role**:
A Role whose starting Faction is fixed as Villager or Werewolf without depending on setup choices, Status Effects, or Events.
_Avoid_: Basic Role, normal Role, Team Role

**Transient Faction**:
A Faction that exists only during a limited opportunity to win.
_Avoid_: Temporary Team, short-lived side

**Latent Faction**:
A Faction that can arise after the beginning of a Game Session.
_Avoid_: Possible Faction, hidden Team

**Initial Faction Count**:
The number of Starting Factions represented by the committed Deal Pool.
_Avoid_: Possible outcome count, Role Group count

**Reference Turn Horizon**:
A descriptive duration baseline equal to Player count divided by Initial Faction Count.
_Avoid_: Expected Turn count, predicted duration

**Run Seed Material**:
The stable description of the random choices for a reproducible simulation run.
_Avoid_: Seed (ambiguous with PRNG integer), random key

**Completed Simulation Run**:
A simulation run that reaches one Game Session Outcome.
_Avoid_: Successful run (ambiguous with desirable outcome), valid game

**Incomplete Simulation Run**:
A simulation run that does not reach a Game Session Outcome.
_Avoid_: Draw, degenerate run, early ending

**Simulation Batch Source Evidence**:
The raw attempted-run evidence from a simulation batch before result aggregation.
_Avoid_: Simulation Result Evidence, probability output, result inventory

**Simulation Result Evidence**:
The stable facts needed to replay and summarize a simulation run or batch.
_Avoid_: Debug log, transcript

**Team** _(deprecated — use Faction for win-condition grouping)_:
A legacy term for runtime allegiance grouping.
_Avoid_: Using in new domain discussions; prefer Faction

**Role Group**:
A category used to organize Roles with related themes.
_Avoid_: Faction (overloaded with Team)

**Status Effect**:
A persistent condition applied to a Player that modifies their state or abilities.
_Avoid_: Buff, debuff, modifier, secondary role (historically used, now unified under Status Effect)

**New Moon Assignment**:
A non-Role responsibility or state introduced by New Moon rules or Events.
_Avoid_: Role, Character Card

### Game Flow

**Game Session**:
One physical playthrough from setup to a Game Session Outcome.
_Avoid_: Game, match, room

**Lobby Exit**:
The boundary at which pre-game configuration ends and the physical Game Session begins.
_Avoid_: Game start (too broad), setup complete

**Role Lock-In**:
The pre-Lobby acceptance of the current Role Composition and its Deal Pool partition. The Moderator may supersede it with another Role Lock-In before Lobby Exit.
_Avoid_: Lobby Exit, Game Session start, Player assignment

**Simulation Start State**:
The fully defined Game Session state from which a simulation begins.
_Avoid_: Role Composition (when current Game Session state matters), snapshot (too implementation-specific)

**Turn**:
One Night, Dawn, and Day cycle.
_Avoid_: Round

**Victory Check Window**:
A resolved boundary where all win conditions are evaluated against the same Game Session state.
_Avoid_: Instant win check, Dusk Phase

**Night Phase**:
The phase in which called Roles perform secret actions while other Players keep their eyes closed.
_Avoid_: Night round

**Dawn Phase**:
The phase in which Night Actions and their consequences resolve before Players wake.
_Avoid_: Morning (ambiguous — Dawn is resolution, Day is when players are awake)

**Day Phase**:
The phase in which Players wake, receive announcements, debate, and Vote.
_Avoid_: Day round

### Communication Contract

**Moderator Instruction**:
A directive telling the Moderator what to announce, perform, or record next.
_Avoid_: Prompt, message, command

**Moderator Response**:
A record of an observed physical fact or a Player's completed choice.
_Avoid_: Input, answer, user input

**Continue Acknowledgment**:
A Moderator Response confirming that the instructed physical or presentation step is complete.
_Avoid_: Confirmation choice, yes/no prompt

**Role Identification**:
A private Moderator Response recording which Players were observed to hold an exact Role.
_Avoid_: Role assignment, reveal

**Faction Agent Group Observation**:
A private Moderator Response recording the Players observed to act together for a Faction.
_Avoid_: Role Identification, Role assignment, Faction Beneficiary

**Moderator-Known Role**:
A current Role privately known to the Moderator and the product.
_Avoid_: Revealed Role, assigned Role

**Role Reveal**:
A public physical event in which a Player shows the applicable Character Card.
_Avoid_: Role assignment, private identification

**Publicly Revealed Role**:
A current or historical Role identity that Players are entitled to know.
_Avoid_: Known Role, assigned Role

### Game Actions

**Night Action**:
A specific action performed by a Role during the Night Phase.
_Avoid_: Night event, ability use

**One-Use Resource**:
A Role resource that can be committed only once per Game Session.
_Avoid_: Charge, successful use

**Vote**:
The village's collective Day Phase decision, ending in a tie or one Vote Target.
_Avoid_: Poll, election

**Vote Target**:
A living Player selected by a non-tied Vote before that Vote resolves.
_Avoid_: Lynched Player, eliminated Player (before resolution), victim

**Consecutive Vote**:
A second independent Vote held in the same Day Phase when a rule requires one.
_Avoid_: Re-vote, runoff, replacement Vote

**Elimination**:
The state change that removes a Player from the Game Session.
_Avoid_: Death, kill (too informal — Elimination is the domain term for the state change)

**Elimination Cascade**:
One or more initial Eliminations together with every Elimination and required reaction they cause.
_Avoid_: Death chain, elimination queue (implementation-specific)

### Special Relationships

**Sheriff**:
An elected Status Effect that grants additional voting power.
_Avoid_: Leader, mayor

**Lovers**:
A pair of Players linked by Cupid.
_Avoid_: Couple, pair

**Cross-Faction Lovers**:
Lovers whose Faction Beneficiaries differ when Cupid links them.
_Avoid_: Cross-team Lovers, mixed Lovers

**Charmed**:
A persistent Status Effect applied by the Piper to another living Player.
_Avoid_: Enchanted, Piper ally

**New Moon Event**:
An Event card that modifies the physical game's rules.
_Avoid_: Event card (when referring to the game mechanic), Spirit card

### Persistence

**Serialization**:
A durable representation of Game Session state.
_Avoid_: Save game, checkpoint

**Rehydration**:
The reconstruction of a Game Session from its serialized state.
_Avoid_: Load game, restore
