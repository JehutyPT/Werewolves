# Werewolves Moderator Helper

A mobile app that assists a human **Moderator** running a physical game of "The Werewolves of Miller's Hollow." The app tracks game state, guides the Moderator through phases, and prompts for input — it never replaces the Moderator or makes decisions for them.

This file is the shared glossary for domain language and avoided synonyms. Stable invariants live in `docs/domain/invariants.md`; rule interaction disambiguations live in `docs/domain/game-rules-clarifications.md`; the exact per-Role Moderator exchange lives in `docs/domain/moderator-role-flows.md`; architectural tradeoffs live in `docs/adr/`; and implementation scope lives in canonical GitHub issue-body Implementation Contracts.

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
The clockwise arrangement of Players around the table. Determines adjacency for abilities like Bear Tamer, Fox, and Knight with the Rusty Sword.
_Avoid_: Player order, turn order

**Living Neighbor**:
The nearest living Player in one direction from a living reference Player around the circular Seating Order, skipping Eliminated Players. When exactly two Players are living, the same other Player is both the clockwise and counterclockwise Living Neighbor.
_Avoid_: Adjacent seat, physical neighbor

### Identity & Allegiance

**Role**:
A Player's current rules identity, determining their abilities, wake-up schedule, and default allegiance. It initially comes from the Character Card received in the Physical Deal and may later change through a Permanent Role Swap.
_Avoid_: Character, class, card (when referring to the assigned identity)

**Physical Character Card Ownership**:
The specific physical Character Card instance a Player currently holds, including its printed Role and card zone. It initially comes from the Physical Deal. A Permanent Role Swap changes the Player's current Role and separately states whether or how the physical card instance changes.
_Avoid_: Current Role, app assignment, known Role, revealed Role

**Deal Pool**:
The exact Player-count subset of the Role Composition that the Moderator commits at Role Lock-In for the Physical Deal. When Thief is enabled, the Deal Pool contains exactly one Thief Character Card; the two Thief Offer Cards are outside it. The app knows this card multiset but not which Player will receive each instance.
_Avoid_: Player assignment, dealt Roles (before the Physical Deal)

**Thief Offer Cards**:
The two distinct physical Character Card instances the Moderator places in private offer slots at Role Lock-In for the Thief Player's Night 1 choice. They remain part of the Role Composition but are not shuffled into the Deal Pool. Thief itself cannot be a Thief Offer Card. Both instances may print the same Role; they retain separate physical identities even though safety screening may share one behaviorally identical choice branch. The pair and later choice remain Moderator-and-Thief-private unless another rule explicitly reveals them. On selection, the chosen instance becomes Player-owned while the original Thief card and unchosen offer move to the Set-Aside zone; on decline, both offers move there while the Player keeps the Thief card.
_Avoid_: Undealt Character Cards, random leftovers, Thief options (when referring to the physical cards)

**Set-Aside Character Cards**:
Physical Character Card instances kept face-down outside active play without being publicly discarded. After a committed Thief exchange, the original Thief card and unchosen offer move into this zone; after a legal decline, both offers move from the Thief Offer zone into this zone while the Player keeps the Thief card. Set-Aside cards are not current Roles or initial holders.
_Avoid_: Discarded cards, undealt cards

**Physical Deal**:
The face-down random distribution of the committed Deal Pool, one physical Character Card to each Player. The app records the Deal Pool but does not shuffle it, distribute it, or know the resulting Player-specific ownership.
_Avoid_: App assignment, generated deal

**Role Power**:
A gameplay capability granted by a Role beyond its identity, physical Character Card, and default allegiance. A Role Power may be chosen, automatic, reactive, passive, recognition-based, or communication-based; information already learned is not itself a Role Power.
_Avoid_: Ability (too broad), Role behavior (includes identity and allegiance)

**Role Power Suppression**:
A continuing rule state that prevents affected Role Powers from beginning or triggering. It does not remove Roles, erase learned information, undo committed or resolved effects, restore spent resources, or cancel durable consequences already scheduled.
_Avoid_: Role removal, power reset

**Rules Role Set**:
The Roles described by the physical rules in `docs/domain/game-rules.md`, regardless of whether the app has implemented them.
_Avoid_: Supported roles, implemented roles

**Implemented Role Set**:
The Roles with working engine behavior in the app.
_Avoid_: Rules Role Set, selectable roles

**Simulator Profile Role Set**:
The Roles one explicitly named simulator capability can execute under its configured setup artifacts and baseline decision behavior. Safety-screening and full-probability capabilities have separate Role sets and compatibility identities.
_Avoid_: Implemented Role Set, selectable roles

**Simulator Capability**:
A versioned evaluation boundary that names its Role set, supported setup artifacts, headless-response policy, evidence depth, and compatibility identity. Role support v1 uses `safety-screening@<version>` and `full-probability@<version>` as the canonical capability identities. `DegenerateScreeningOnly` and `FullProbability` are evaluation-depth requests, not capability identities. Scenario support, cache lookup, and stale-record rejection are always evaluated for a named capability.
_Avoid_: Active profile (when the capability is not named), simulator support (unqualified)

**Safety-Screening Role Set**:
The Roles explicitly admitted to deterministic Already-Decided classification and the 1,000-run Degenerate Simulation Scenario screen for production lobby safety. Membership does not imply probability evaluation or Bundled Simulator Cache coverage.
_Avoid_: Simulator Profile Role Set (when capability matters), full simulator support

**Full-Probability Role Set**:
The Roles explicitly admitted to the dormant probability capability. It is a subset of the Safety-Screening Role Set because full evaluation includes the same earlier safety gates, but it is not required to contain every App-Supported Role in role support v1.
_Avoid_: Safety-Screening Role Set, app-supported roles

**Selectable Role Set**:
The Roles exposed to the Moderator in the current role-selection UI.
_Avoid_: Rules Role Set, implemented roles

**Role Composition**:
The multiset of Roles the Moderator selects and the app records for the physical Game Session before play, independent of which Player receives each Role. Without Thief it is the Deal Pool. With Thief it contains the Player-count Deal Pool plus exactly two Thief Offer Cards, and the committed partition is part of the setup. It excludes Actor Setup Cards, New Moon Events, Player names, Seating Order, Status Effects, and unrelated setup choices.
_Avoid_: Combination, setup (too broad), assignment (implies Player-specific Role knowledge)

**Rules-Valid Role Composition**:
A Role Composition whose complete physical inventory and Deal Pool/Thief Offer partition satisfy card-count and Role-count rules, and whose Deal Pool alone satisfies required hard-aligned Faction coverage, without considering whether the app or active simulator profile implements every included Role.
_Avoid_: Valid (too broad), app-supported

**App-Supported Role Composition**:
A Rules-Valid Role Composition that falls within the app's product support boundaries, including Supported Player Count and supported feature scope.
_Avoid_: Rules-valid, simulator-supported

**Actor Setup Cards**:
The three face-up Character Cards selected by the Moderator on Actor's conditional lobby-setup stage after Role Lock-In and recorded by the app for the Actor to borrow powers from. The app validates but does not generate the live inventory. This stage is required when Actor appears in either the Deal Pool or the Thief Offer Cards, so every reachable acquired-Actor branch is executable. Actor Setup Cards must be hard-aligned Villager Roles with borrowable individual Role Powers that are not already part of the Role Composition. Actor Setup Cards are not part of the Role Composition and do not contribute Starting Factions or Possible Factions. The Actor Role itself is a hard-aligned Villager Role.
_Avoid_: Actor Role Composition, Actor deck

**Borrowed Role Power**:
A fresh, temporary instance of an eligible source Role Power in full, including its benefits, conditions, costs, and consequences, activated when Actor selects and spends its Actor Setup Card and lasting until Actor's call at the start of the next Night. It keeps the source power's relative call or trigger but not a native Role's already-passed one-time setup restriction, and it changes neither the Actor's Role nor Faction Beneficiary; skipping the Actor call creates no instance and spends no card.
_Avoid_: Temporary Role, copied Role

**Public Group Partition**:
A pre-game grouping created and publicly announced by the Moderator on Prejudiced Manipulator's conditional lobby-setup stage after Role Lock-In, then recorded and validated by the app. This stage is required when Prejudiced Manipulator appears in either the Deal Pool or the Thief Offer Cards. Every Player belongs to exactly one of two publicly known, non-empty groups for the full Game Session. The groups may differ in size, including a one-Player group; the live app never generates or balances them.
_Avoid_: Prejudiced Manipulator teams, balanced groups

**Opposing Public Group**:
The block of the Public Group Partition that does not contain the current Prejudiced Manipulator Faction Beneficiary. It is defined only while that Faction has a living beneficiary.
_Avoid_: Target group, enemy team, original Manipulator's opposition

**Simulation Scenario**:
The complete pre-game simulator input used for lobby-level cache lookup or pre-game simulation. A Simulation Scenario always includes a canonical Role Composition and, when Thief is enabled, its committed Deal Pool/Thief Offer partition. It may also include setup artifacts or non-default assumptions, such as Actor Setup Cards, New Moon Event support, or a non-default Public Group Partition. Profile defaults, such as the baseline even split for Prejudiced Manipulator, do not need to be repeated in every Simulation Scenario.
_Avoid_: Role Composition (when setup artifacts are also included), setup (too broad)

**Canonical Role Composition**:
The stable string representation of a Role Composition for cache keys, simulation scenarios, and replay evidence. It contains non-zero Role counts only, sorted alphabetically by exact enum identifier, using enum identifiers rather than localized names. It counts every physical Role card in the Role Composition, including Thief extras, and never includes Actor Setup Cards.
_Avoid_: Display role list, localized composition

**Canonical Simulation Scenario**:
The stable string representation of a Simulation Scenario. It includes Player count separately from the Canonical Role Composition because Thief can make total card count differ from Player count. When Thief is enabled, it also identifies the committed Deal Pool/Thief Offer partition; the same Role Composition with a different partition is a different scenario. It includes other setup artifacts or non-default assumptions that affect simulation, such as Actor Setup Cards, while leaving profile defaults implicit in the profile/version.
_Avoid_: Canonical Role Composition (when Player count or setup artifacts are included)

**Capability-Supported Simulation Scenario**:
A Simulation Scenario that one explicitly named Simulator Capability can execute with its admitted Roles, setup artifacts, and headless-response policy. The capability name is mandatory: use Safety-Screening-Supported or Full-Probability-Supported when that distinction matters.
_Avoid_: Simulator-supported (unqualified), App-supported Role Composition, cacheable

**Safety-Screening-Supported Simulation Scenario**:
A Capability-Supported Simulation Scenario for the production safety-screening capability. It can reach deterministic Already-Decided classification and, when needed, complete the 1,000-run Degenerate Simulation Scenario screen.
_Avoid_: Full-Probability-Supported Simulation Scenario, app-supported setup

**Full-Probability-Supported Simulation Scenario**:
A Capability-Supported Simulation Scenario for the dormant full-probability capability. It must also be Safety-Screening-Supported because its pipeline passes the same earlier gates before probability evaluation.
_Avoid_: Safety-Screening-Supported Simulation Scenario, current production support

**Cacheable Simulation Scenario**:
A Capability-Supported Simulation Scenario eligible for one named record type under that capability's compatibility identity. Safety screening may store only local Already-Decided or Degenerate terminal records; role support v1 does not add new build-time or probability-cache coverage.
_Avoid_: Role Composition, capability-supported

**Bundled Simulator Cache**:
The app-facing collection of precomputed lobby evaluations shipped with the app for cache-first pre-game safety screening. Retained probability payloads belong to the dormant full-evaluation capability, not the current Moderator-facing product.
_Avoid_: Offline cache (ambiguous with on-device fallback), simulator log

**Local Fallback Cache Record**:
A compact terminal lobby evaluation materialized on the Moderator's device after a terminal On-Device Fallback Generation classification. It is keyed and invalidated like the equivalent Bundled Simulator Cache entry and stores no per-run Simulation Result Evidence.
_Avoid_: Local simulation evidence, replay cache, transcript cache

**Build-Time Cache Generation**:
The production of Bundled Simulator Cache artifacts outside the Moderator's phone, such as on a development machine, CI worker, or backend job.
_Avoid_: Offline generation (ambiguous), on-device generation

**On-Device Fallback Generation**:
A local safety-capability evaluation attempted on the Moderator's device when no usable compatible terminal lobby evaluation is available for a Safety-Screening-Supported Simulation Scenario. The production path stops after already-decided and 1,000-run degenerate screening. When fallback materializes a result, it stores only a compact Already-Decided or Degenerate Local Fallback Cache Record, not full per-run Simulation Result Evidence; a successful non-degenerate pass remains session-local. Fallback has two hard failure boundaries: any generation failure or a 10-second timeout.
_Avoid_: Normal pre-game simulation, build-time cache generation

**Minimum Viable Role Composition**:
The smallest Role Composition the app treats as a meaningful Game Session has a five-card Deal Pool for 5 Players: exactly one hard-aligned Werewolf Role and four hard-aligned Villager Roles. If Thief is enabled, the two Thief Offer Cards are additional Role Composition cards and do not count toward that five-card minimum. Ambiguous Roles and Loner Roles are not meaningful at this size.
_Avoid_: Starter deck, tutorial setup

**Already-Decided Role Composition**:
A locked Simulation Scenario whose committed Deal Pool alone would make at least one Faction win at Lobby Exit, before Player assignment, a Thief choice, simulation, or Turn 1. A Role found only among the Thief Offer Cards supplies no initial holder, Starting Faction, or already-decided victory evidence.
_Avoid_: Simulated loss, failed run

**Degenerate Simulation Scenario**:
A legal, supported Simulation Scenario for which at least one semantically distinct legal branch completes all 1,000 baseline screening runs and observes only Game Sessions ending by the end of Turn 1, before Players get meaningful agency. For Thief, the legal branches are each distinct offered-Role behavior plus decline when permitted; a Degenerate branch blocks the whole scenario, while an error, timeout, or incomplete branch is not degeneracy evidence.
_Avoid_: Degenerate Role Composition, invalid (ambiguous with rules-invalid), failed simulation, mathematically proven early ending

**Balanced Role Composition**:
A Role Composition whose Game Result Frequency is not obviously concentrated in one Starting Faction's single-Faction Game Result. This term belongs to the dormant probability vocabulary and is not a current app verdict.
_Avoid_: Fair game (too vague), duration-balanced

**Faction**:
A distinct win condition together with the set of Players who benefit from it being met. A Faction comes into being whenever at least one Player holds a win condition not shared by any existing Faction. Membership can change during the game. Examples: the Villager Faction (all Villagers win when every Werewolf is Eliminated), the Piper Faction (a living Piper Faction Beneficiary wins when every other living Player is Charmed).
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

**Known Faction State**:
The app's legitimately established knowledge of a Player's current Faction Beneficiary and Faction Agent memberships. The rules give every Player one actual Faction Beneficiary, but a live Game Session represents an unobserved value as unknown until a physical observation or explicit Core transition establishes it. A query that needs an unknown Faction fact must obtain that fact before resolving; it never substitutes a Role Composition default or deduces the value from remaining inventory.
_Avoid_: Default team, inferred allegiance, assumed Villager

**Permanent Role Swap**:
A Role replacement that permanently changes a Player's Role for the rest of the Game Session without replacing the Player. By default, a Permanent Role Swap changes the Player's Faction Beneficiary to the new Role's default Faction unless another rule explicitly takes precedence, such as Cross-Faction Lovers.
_Avoid_: Transformation (too broad), conversion (ambiguous with infection)

**Shared Victory Outcome**:
A Game Session Outcome where multiple Factions win because their win conditions become true in the same Victory Check Window.
_Avoid_: Tie (ambiguous with Vote ties), co-winners (too informal)

**No-Winner Outcome**:
A Game Session Outcome where no Faction wins because no Faction win condition is true and every Player is Eliminated.
_Avoid_: Draw, stalemate

**Game Result**:
The mutually exclusive final result category for a completed simulated Game Session: one Faction wins, a specific Shared Victory Outcome occurs, or No-Winner occurs.
_Avoid_: Outcome bucket, winner key

**Possible Game Result**:
A scenario-specific Game Result retained by full probability evaluation even when the completed simulation batch observes it zero times. Possible Game Results include one single-Faction result for each Possible Faction, No-Winner, and each scenario-specific Shared Victory Outcome combination the rules/profile can produce. Factions and shared combinations outside the Simulation Scenario's Possible Game Result inventory are not global catalog rows.
_Avoid_: Global result catalog, observed result

**Game Result Frequency**:
The share of completed simulation runs ending in each Game Result; Game Result Frequencies sum to 100%.
_Avoid_: win rate

**Game Result Frequency by Turn**:
The share of completed simulation runs ending with each Game Result on each ending Turn and Victory Check Window; all cells sum to 100%, and summing a Game Result across Turns and Victory Check Windows gives its Game Result Frequency. Full probability presentation, when enabled, collapses across Victory Check Windows and shows ending Turn only; the Victory Check Window stays in evidence/cache semantics.
_Avoid_: PMF, timing table (too vague)

**Ended-By-Turn Frequency**:
A derived probability view showing the share of completed simulation runs that have ended by a given Turn, optionally filtered to a specific Game Result. It is computed from Game Result Frequency by Turn and is not a separate stored probability model.
_Avoid_: CDF, duration metric

**Unlikely Possible Result**:
A Possible Game Result whose exact frequency is below 1% of completed runs, including zero-frequency results. Unlikely Possible Results can be grouped into a named "possible but unlikely outcomes" list to keep a full probability view readable; grouping is presentation only and the underlying Game Result Frequency remains complete.
_Avoid_: Impossible result, error bucket

**Starting Faction**:
A Faction represented by the committed Deal Pool before Turn 1 as a stable win-condition beneficiary. A Faction represented only by a Thief Offer Card is not a Starting Faction before that card is chosen.
_Avoid_: Initial Faction (ambiguous with Initial Faction Count), default side

**Possible Faction**:
A Faction implied by the Roles present in the Moderator-selected Role Composition, including a Faction reachable only by choosing a Thief Offer Card, regardless of whether that Faction appears as a beneficiary in a particular Game Session or simulation batch. Possible Factions can produce zero-frequency Game Results in dormant full probability evidence when no completed run ends with that Faction winning.
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
The count of Starting Factions represented by the committed Deal Pool.
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
A simulation run that does not reach a Game Session Outcome because the simulator could not generate, drive, or finish the run. Incomplete Simulation Runs are not Game Session Outcomes, are not No-Winner Outcomes, and do not contribute to Game Result Frequency.
_Avoid_: Draw, degenerate run, early ending

**Simulation Batch Source Evidence**:
The execution-layer batch contract containing the Simulation Scenario, simulator profile, and decision-strategy identity; one minimal source record per attempted run in ascending run order; and Completed versus Incomplete Simulation Run counts. Simulation Batch Source Evidence is a precursor to, not the complete inventory-bearing Simulation Result Evidence assembled by downstream terminal evaluation, and it does not construct Possible Faction or Possible Game Result inventories.
_Avoid_: Simulation Result Evidence, probability output, result inventory

**Simulation Result Evidence**:
The stable domain evidence reported by a simulation run or batch: the Simulation Scenario/profile identity, run count evidence, one minimal source record per attempted run, Completed versus Incomplete Simulation Run counts, completed Game Session Outcomes, ending Turns and Victory Check Windows, Run Seed Material needed for replay evidence, the Simulation Scenario's Possible Faction and Possible Game Result inventories, and source data sufficient to derive aggregate Game Result views. Final Player/Faction state snapshots, full transcripts, instruction counts, exception details, timing, memory, raw engine traces, and driver limits are diagnostics rather than Simulation Result Evidence.
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
The final boundary where the Moderator attempts to leave pre-game configuration and start the physical Game Session after Role Lock-In, every required conditional lobby-setup stage, and the applicable safety gate have completed. Role Lock-In alone never exits the Lobby.
_Avoid_: Game start (too broad), setup complete

**Role Lock-In**:
The lobby boundary where the Moderator confirms the complete Role Composition, including the Deal Pool and any Thief Offer Cards. It freezes the selection used to derive the required conditional setup stages, but it neither performs the Physical Deal nor exits the Lobby. Issue #178 decides whether and how the Moderator can edit a locked selection and what that invalidates.
_Avoid_: Lobby Exit, Game Session start, Player assignment

**Simulation Start State**:
The fully defined, simulator-internal Game Session state from which a simulation batch begins. A pre-game Simulation Start State may derive seeded synthetic Player assignments from the committed Deal Pool and profile-default setup from a Simulation Scenario while retaining its fixed Thief Offer Cards; those facts grant no authority to populate live play. A mid-game Simulation Start State can be captured from an in-progress Game Session once that state is representable.
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
When Players open their eyes, victims are announced, debate happens, and the village holds one or more Votes to eliminate suspects.
_Avoid_: Day round

### Communication Contract

**Moderator Instruction**:
A directive from the app to the Moderator — what to announce publicly, what to do privately, and what input to collect next.
_Avoid_: Prompt, message, command

**Moderator Response**:
Input from the Moderator recording an observed physical fact or a Player's completed choice — selected Players, identified or revealed Roles, chosen options, or a Continue acknowledgment. A Moderator Response does not authorize the app to invent a live deal or Player decision.
_Avoid_: Input, answer, user input

**Continue Acknowledgment**:
A one-way Moderator Response confirming that an instruction, announcement, private feedback step, or physical action is complete. It means “continue”; it is not a yes/no gameplay decision and a false value is not a second valid branch.
_Avoid_: Confirmation choice, yes/no prompt

**Role Identification**:
A private Moderator Response recording which Player or Players physically answered an exact-Role call or otherwise established their current Role to the Moderator. It records an observed current Role; it does not perform the Physical Deal, change Physical Character Card Ownership, or make the Role public.
_Avoid_: Role assignment, reveal

**Faction Agent Group Observation**:
A private Moderator Response recording the complete Player group that physically answered a Faction operation, such as the collective Werewolf wake. It records current operational Faction Agent membership without identifying any exact Role and cannot mutate current Role or Physical Character Card Ownership.
_Avoid_: Role Identification, Role assignment, Faction Beneficiary

**Moderator-Known Role**:
A current Role that the Moderator and app legitimately learned through Role Identification, a Role Reveal, or a rules transition explicitly committed by Core, such as a Permanent Role Swap. Unknown live Roles are never deduced merely from remaining inventory or defaulted from missing information. The Role may still be hidden from Players.
_Avoid_: Revealed Role, assigned Role

**Role Reveal**:
A public physical event in which a Player shows the applicable Character Card and the Moderator records that the current Role is now public. If Core already knows the Role, the response acknowledges that the public event occurred; if it does not, the response supplies a complete valid mapping. Reveal changes public knowledge, not the current Role.
_Avoid_: Role assignment, private identification

**Publicly Revealed Role**:
A current or historical Role identity that Players are entitled to know because a Role Reveal or another explicit rule made it public. Public projections use this state and never infer it merely because the Moderator knows the Role.
_Avoid_: Known Role, assigned Role

### Game Actions

**Night Action**:
A specific action performed by a Role during the Night Phase (e.g., Werewolf attack, Seer check, Witch save/kill, Defender protect).
_Avoid_: Night event, ability use

**One-Use Resource**:
A limited Role resource that can be committed only once per Game Session, such as either Witch potion or the Accursed Wolf-Father's infection. It is consumed when the Moderator confirms the action, even if later resolution prevents the effect, makes it redundant, or produces no state change.
_Avoid_: Charge, successful use

**Vote**:
The village's collective decision during the Day Phase to eliminate a suspected Werewolf. Can be modified by events or Roles (e.g., Sheriff's double vote or a Stuttering Judge Consecutive Vote).
_Avoid_: Poll, election

**Consecutive Vote**:
A second, independent Vote held in the same Day Phase when a rule requires one, such as a valid Stuttering Judge signal. The first Vote and all of its resolved state changes remain in force; the Consecutive Vote does not retry or replace it.
_Avoid_: Re-vote, runoff, replacement Vote

**Elimination**:
Removing a Player from the game. Has a specific reason (Werewolf attack, day vote, Hunter shot, Lovers heartbreak, etc.).
_Avoid_: Death, kill (too informal — Elimination is the domain term for the state change)

**Elimination Cascade**:
The complete resolution that begins with one or more initially concurrent Eliminations and includes every resulting Lovers heartbreak and Hunter final shot. It ends only when no new Elimination or required reaction remains, before the next Victory Check Window.
_Avoid_: Death chain, elimination queue (implementation-specific)

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

**Charmed**:
A persistent, non-stacking Status Effect applied by the Piper to another living Player. A Charmed Player keeps their Role, Faction Beneficiary, and powers but cannot be selected by the Piper again.
_Avoid_: Enchanted, Piper ally

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
- Each **Player** has exactly one current **Role**, one current physical Character Card instance when the rules require one, and zero or more **Status Effects**; card ownership, current Role, Moderator knowledge, and public reveal are separate state
- A live **Game Session** starts with unknown Player-specific current Roles and Physical Character Card Ownership and learns them only from observed physical facts or explicit Core-committed rules transitions; simulator runs alone may derive seeded synthetic assignments
- Every Player has one actual **Faction Beneficiary** under the rules, while the live app's **Known Faction State** may remain unknown until an observation or Core-authored transition establishes it; unknown values never fall back to a guessed Beneficiary or Agent membership
- A **Role Composition** contains the Player-count **Deal Pool** plus exactly two **Thief Offer Cards** when Thief is enabled; Actor does not change **Role Composition** size
- A Thief-enabled **Deal Pool** contains exactly one Thief Character Card, so the Physical Deal always produces one initial Thief holder; Thief cannot be a **Thief Offer Card**
- A committed Thief selection is a private one-for-one physical exchange: the selected **Thief Offer Card** becomes Player-owned, while the original Thief card and unchosen offer become **Set-Aside Character Cards**; a legal decline preserves the Thief card's Player ownership and current Role while moving both offers to the Set-Aside zone
- **Actor Setup Cards** are selected through a separate setup flow and are not part of the **Role Composition**
- Live **Actor Setup Cards** and the **Public Group Partition** are Moderator-created facts; simulator profile defaults never populate live setup
- **Role Lock-In** commits the **Role Composition**, **Deal Pool**, and any **Thief Offer Cards**, then the Lobby collects every conditional setup artifact required by any reachable selected Role before **Lobby Exit**
- **Role Composition**, **Actor Setup Cards**, and **Public Group Partition** are staged lobby/configuration inputs outside the in-session **Moderator Instruction** and **Moderator Response** cycle
- Actor is a hard-aligned Villager **Role**; **Actor Setup Cards** provide borrowed powers only and do not change the Actor's **Faction Beneficiary**
- Actor counts toward hard-aligned Villager **Role** requirements for supported **Role Compositions**
- **Actor Setup Cards** must be hard-aligned Villager **Roles** with borrowable individual **Role Powers** that are not already selected in the **Role Composition**; Simple Villager, Villager-Villager, Two Sisters, and Three Brothers are not eligible
- If Actor is in the **Deal Pool** or among the **Thief Offer Cards**, the app must require exactly three eligible **Actor Setup Cards** to remain outside the **Role Composition** before setup can advance
- **New Moon Events**, Player names, **Seating Order**, **Status Effects**, Sheriff, Lovers, Charmed, Prejudiced Manipulator groups, and physical traits such as youngest Player are not part of the **Role Composition**
- New-Moon-dependent Roles and Event effects are outside the v1 simulator scope unless a **Simulation Scenario** explicitly includes New Moon support
- Cache and simulation inputs use a **Simulation Scenario** when setup artifacts or profile assumptions matter beyond the **Role Composition**
- The **Bundled Simulator Cache** is produced by **Build-Time Cache Generation**, not by trying to enumerate every possible scenario on the Moderator's device
- **Bundled Simulator Cache** lookup identity is the **Canonical Simulation Scenario** plus the named **Simulator Capability** compatibility identity; batch sizes are generation policy rather than cache identity
- A legacy `core-simulator@1` probability record may satisfy the current `safety-screening@<version>` gate only through the explicit bridge: the scenario must lie in the intersection of that legacy producer/profile and the current Safety-Screening capability, whose compatibility identity must declare the exact legacy semantics compatible. The bridge never covers a newly admitted safety-only Role or changed setup, Role, outcome, or headless-response semantics
- A **Local Fallback Cache Record** carries the safety capability and terminal-record identity and is reused across app restarts only while both remain current
- **On-Device Fallback Generation** is allowed only when no usable compatible terminal lobby evaluation exists for a **Safety-Screening-Supported Simulation Scenario** and must follow the safety capability's classification pipeline
- **On-Device Fallback Generation** is skipped when the selected setup is rules-invalid, app-unsupported, safety-screening-unsupported, already has usable compatible terminal evidence, or changes before the fallback attempt finishes
- A successful **On-Device Fallback Generation** classification has the same safety meaning as equivalent bundled evidence. Only terminal classifications materialize a compact terminal evaluation; a screening pass need not be persisted
- **Lobby Exit** waits while a **Safety-Screening-Supported Simulation Scenario** has no resolved safety determination. If fallback evaluation fails, the safety gate releases; safety-only production does not surface the dormant "could not evaluate" panel.
- **Lobby Exit** does not offer a manual skip or dismiss action while fallback evaluation is running; it proceeds only after a safety classification passes, or after fallback fails or reaches its 10-second timeout
- A failed **On-Device Fallback Generation** attempt is remembered only for the current unchanged lobby setup and current app session; it is not persisted like a **Local Fallback Cache Record**
- Explicit fallback retry belongs to the dormant full-evaluation presentation. Safety-only production exposes no retry action after failure
- App-supported but safety-screening-unsupported setups do not attempt **On-Device Fallback Generation** and do not block **Lobby Exit** only because evaluation is unavailable
- The simulator runs from a **Simulation Start State**; pre-game cache generation derives that state from a **Simulation Scenario**, while mid-game projection can use the same simulation mechanism from a later fully defined Game Session state
- **Degenerate Simulation Scenario** classification applies to the **Simulation Scenario**; each screening run derives its own seeded pre-game **Simulation Start State** from that scenario, including random assignment and profile/default setup choices
- A Thief-enabled **Simulation Scenario** screens every semantically distinct legal selection or decline branch; any branch classified as **Degenerate** blocks Lobby Exit, while a mix containing only screening passes, failures, or timeouts does not block. If every branch completes without Degenerate classification the aggregate is a screening pass; otherwise it remains **Could Not Evaluate** without becoming blocking evidence
- **Canonical Role Composition** omits zero-count Roles, uses exact enum identifiers rather than localized names, and sorts Role entries alphabetically by enum identifier
- **Canonical Simulation Scenario** includes `players=N` separately from **Canonical Role Composition** and records the committed **Deal Pool**/**Thief Offer Cards** partition because Thief can make total Role card count differ from Player count
- A **Public Group Partition** is not part of **Role Composition**; an even split is the baseline simulator profile default, and only non-default partitions need explicit **Simulation Scenario** material
- Roles in the committed **Deal Pool** can contribute **Starting Factions**; Roles found only among **Thief Offer Cards** contribute **Possible Factions** and contribute a beneficiary only in the branch where the Thief Player chooses them
- A **Cacheable Simulation Scenario** is limited by the Role set and record policy of its named **Simulator Capability**, not the full **Rules Role Set**
- A Role becomes **App-Supported** only when the app can actually guide the Moderator through it; Roles that exist only in the **Rules Role Set** are Rules-Valid but not App-Supported
- **Supported Player Count** caps Players only, not total physical cards; Thief can make a **Role Composition** larger than the Player count, and **Actor Setup Cards** are additional physical cards outside the **Role Composition**
- A **Rules-Valid Role Composition** must have a valid partition whose **Deal Pool** includes at least one hard-aligned Villager **Role** and at least one hard-aligned Werewolf **Role**; an offer-only card cannot supply initial coverage, and Simple Villager and Simple Werewolf are not mandatory by role name
- Role-count constraints such as single-copy special Roles, exactly two Sisters, and exactly three Brothers are domain rules; Supported Player Count, currently implemented Roles, New Moon exclusion, and simulator profile support are app/product constraints
- The **Minimum Viable Role Composition** has a five-card **Deal Pool** containing one hard-aligned Werewolf **Role** and four hard-aligned Villager **Roles**
- A supported **Role Composition** must have a **Deal Pool** containing at least one hard-aligned Villager **Role** and at least one hard-aligned Werewolf **Role**
- Production classification order is: **Rules-Valid Role Composition**, **App-Supported Role Composition**, **Safety-Screening-Supported Simulation Scenario**, **Already-Decided Role Composition**, then **Degenerate Simulation Scenario**. A dormant full-probability request must additionally be **Full-Probability-Supported** before continuing to probability simulation
- **Already-Decided Role Composition** classification runs every Faction victory trigger that can be evaluated from the committed **Deal Pool** projection of the locked **Simulation Scenario** at **Lobby Exit**; possible Player assignments, offer-only Roles, setup branches, and Night 1 choices are not evidence for this classification
- **Already-Decided Role Composition** classification does not derive or simulate a **Simulation Start State**
- If multiple Faction victory triggers are true at **Lobby Exit** from committed **Deal Pool** evidence alone, the **Already-Decided Role Composition** record uses **Shared Victory Outcome** semantics rather than a priority order
- Lobby result lookup uses the **Canonical Simulation Scenario** for uniform cache access, and **Already-Decided Role Composition** classification reads only that scenario's committed **Deal Pool** projection
- Enumeration conceptually starts from **Rules Role Set** plus Player count to generate **Rules-Valid Role Compositions**, then filters to **App-Supported Role Compositions**, the requested **Capability-Supported Simulation Scenarios**, and that capability's **Cacheable Simulation Scenarios**
- Ambiguous **Roles** do not create **Starting Factions**; their choices or later state changes resolve into existing Factions or later outcomes
- At the rules level, ambiguous **Roles** use a Villager **Faction Beneficiary** unless their Role definition explicitly says otherwise; this rule does not let the live app assign a Beneficiary to a Player whose current Role is still unknown
- Loner **Roles** do not share a Loner **Faction**; each Loner **Role** defines its own Faction lifecycle
- **Cross-Faction Lovers** are a **Latent Faction**; same-Faction **Lovers** remain only a **Status Effect**
- A Player changing which Faction they benefit from does not create a new **Faction**
- **New Moon Events** do not create **Factions** unless they define a distinct win condition
- Town Crier is a **New Moon Assignment** like Sheriff, not a **Role** in the Role Composition
- Elimination-style Faction win conditions are evaluated against **Faction Beneficiaries**
- In the current ruleset, a Player has exactly one beneficiary Faction at a time; changes such as Cross-Faction Lovers, Wild Child transformation, Wolf Hound choice, Thief swap, Devoted Servant swap, or Double Agent replace the Player's previous beneficiary link
- Wolf Hound's Night 1 choice preserves the Player's **Role** and physical Character Card. The choice changes **Faction Beneficiary** and Werewolf **Faction Agent** state rather than causing a **Permanent Role Swap**
- Infection changes a Player's **Faction Agent** status, not their **Faction Beneficiary**
- A **Permanent Role Swap** changes the Player's **Faction Beneficiary** to the new Role's default Faction unless an explicit precedence rule says otherwise
- Thief's Night 1 **Permanent Role Swap** takes effect immediately; the acquired Role follows the remaining canonical Night 1 call order without replaying earlier calls
- Cross-Faction Lovers immediately replace both Lovers' **Faction Beneficiary** links; same-Faction **Lovers** remain only a **Status Effect**
- Detailed Role interaction rulings, including Cross-Faction Lovers precedence, Devoted Servant, Miracle, Elder, Big Bad Wolf, Full Moon Rising, Double Agent, Angel, and other edge cases, live in `docs/domain/game-rules-clarifications.md`
- Devoted Servant's successful swap is a **Permanent Role Swap**
- The Villager Faction wins when every living non-Villager **Faction Beneficiary** has been Eliminated
- The Werewolf Faction's full win condition is eliminating all other **Faction Beneficiaries**; **Werewolf Control Shortcut** is only a shortcut for Villager-vs-Werewolf endgames
- **Werewolf Control Shortcut** uses **Durable Voting Power**, not temporary vote effects
- The White Werewolf Faction wins when the White Werewolf is the sole surviving Player
- White Werewolf is a Werewolf **Faction Agent** for night targeting and Seer detection, and a White Werewolf **Faction Beneficiary** for win conditions
- Generic rules that check, target, count, or react to "Werewolves" use Werewolf **Faction Agents** unless the rule explicitly says Role or Character Card
- Werewolf group attacks cannot target Werewolf **Faction Agents**
- White Werewolf's solo attack targets another Werewolf **Faction Agent**
- White Werewolf's solo **Night Action** follows the absolute Game Session Night number: it is unavailable on Night 1, then occurs on Nights 2, 4, 6, and every later even-numbered Night, after the Accursed Wolf-Father and before the Big Bad Wolf. Declining the action, lacking a legal target, or having the attack prevented or made ineffective does not shift that cadence
- A Fox check inspects the duplicate-free set containing its chosen living Player and that Player's clockwise and counterclockwise **Living Neighbors**. With exactly two living Players, the set contains both Players. Declining performs no check, gives no feedback, and preserves the Fox power; only a performed negative check removes it
- A Knight with the Rusty Sword actually Eliminated by a physical Werewolf attack snapshots its diseased target after that Dawn's complete **Elimination Cascade**. Starting clockwise from the Knight's fixed seat, select the first surviving Werewolf **Faction Agent** then eligible; successful same-Night infection and same-cascade transformation count, and a triggering-Night temporary Agent remains eligible for this check. The disease attaches to that Player identity, resolves once at the following Dawn if the Player is living, and never retargets
- An eligible Piper charm target is a living Player other than the Piper who is not already **Charmed**. The Piper must charm two distinct eligible Players when at least two exist, the sole eligible Player when exactly one exists, and no Player when none exist
- The Piper Faction predicate is true at a **Victory Check Window** only when a living Piper **Faction Beneficiary** exists and every other living Player is **Charmed**
- The Prejudiced Manipulator Faction predicate is true at a **Victory Check Window** only when a living Prejudiced Manipulator **Faction Beneficiary** exists and no living Player remains in that beneficiary's **Opposing Public Group**, regardless of those Players' **Faction Beneficiaries**
- A Player can be a **Faction Agent** for one Faction while benefiting from another Faction's win condition, such as White Werewolf waking with Werewolves or Double Agent benefiting from Werewolf victory without waking with Werewolves
- The Seer detects Werewolf **Faction Agents**, not Werewolf **Faction Beneficiaries**
- Win conditions are evaluated only during **Victory Check Windows**
- Angel's transient Faction expires immediately after the Dawn **Victory Check Window** that resolves Night 2 if the Angel did not win, and the Angel then becomes a Simple Villager
- During a **Victory Check Window**, all win-condition predicates are evaluated against the same resolved Game Session state; if multiple Factions' predicates are true, the Game Session ends with a **Shared Victory Outcome**
- A **No-Winner Outcome** is evaluated only when no Faction win condition is true in the Victory Check Window and every Player is Eliminated
- A **Game Session** ends with exactly one **Game Session Outcome**
- A **No-Winner Outcome** can occur when mutually assured Elimination leaves no Faction able to win
- The current runtime victory check is a two-Faction `Team` shortcut and is not the complete future **Faction** win-condition model
- Faction lifecycle describes how **Initial Faction Count** is computed; **Initial Faction Count** counts **Starting Factions** and excludes **Transient Factions** and **Latent Factions**
- **Reference Turn Horizon** is derived from **Player** count and **Initial Faction Count**
- **Game Result Frequency** includes mutually exclusive **Game Results** only: single-Faction wins, specific **Shared Victory Outcomes**, and **No-Winner Outcome**
- **Game Result Frequency by Turn** is the source timing view for deriving **Game Result Frequency** and **Ended-By-Turn Frequency**
- **Simulation Result Evidence** defines stable replayable evidence; diagnostics and cache artifact boundaries are architectural concerns covered by ADR-0009
- Moderator-facing **Game Result Frequency** and **Ended-By-Turn Frequency** presentation is disabled under ADR-0013; the underlying full-evaluation capability is dormant
- The measured current-profile **Bundled Simulator Cache** ships inside the app package under ADR-0012; its probability payloads remain dormant, and distribution for a future expanded or full-role profile may be reconsidered after its realistic artifact size and operating constraints are known
- "Could not evaluate" is a product evaluation state, not a **Game Session Outcome**, **Game Result**, **No-Winner Outcome**, or probability bucket
- A simulation run ending during Turn 1 is a **Completed Simulation Run** when it reaches a **Game Session Outcome**; **Incomplete Simulation Runs** do not contribute to **Game Result Frequency**
- A **Balanced Role Composition** is considered from **Game Result Frequency**, not by comparing winning Turn to the **Reference Turn Horizon**
- The Moderator judges whether a Role Composition is balanced; the app only blocks **Already-Decided Role Compositions** and **Degenerate Simulation Scenarios**
- A **Role** belongs to one **Role Group** and determines a default **Team**, but a Player's actual **Team** can change via **Status Effects** (e.g., infection)
- A **Turn** consists of one **Night Phase**, one **Dawn Phase**, and one **Day Phase**, in that order
- During each **Night Phase**, Roles perform **Night Actions** which are resolved during **Dawn Phase**
- During **Day Phase**, the village holds a **Vote** and, when a rule requires one, a **Consecutive Vote**; each may result in an **Elimination**
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
- "Thief Offer Cards" as Starting Factions — resolved: offer-only Roles are not initial holders and do not contribute Starting Factions or Lobby Exit victory evidence; they do contribute Possible Factions and become active only in the branch where the Thief Player chooses them.
- "Actor cards" as Role Composition — resolved: **Actor Setup Cards** are a separate setup artifact, not part of the Role Composition and not a source of Possible Factions; Actor setup requires three eligible hard-aligned Villager Roles to remain outside the Role Composition.
- "Thief extra cards" as setup artifact — resolved: the Moderator commits two **Thief Offer Cards** before Lobby Exit. They remain part of the **Role Composition** but are excluded from the **Deal Pool** rather than emerging as chance-determined leftovers.
- "Role Composition as full simulator input" — resolved: use **Simulation Scenario** when cache or simulation inputs include setup artifacts or profile assumptions beyond the Role Composition.
- "New Moon Events in Role Composition" — resolved: New Moon Events are outside Role Composition, and New-Moon-dependent simulation is out of v1 scope unless a **Simulation Scenario** explicitly includes New Moon support.
- "Canonical Role Composition" — resolved: count every physical Role card in the Role Composition, including Thief extras, omit zero-count Roles, sort by exact enum identifier, and never include Actor Setup Cards.
- "Player count inference" — resolved: **Canonical Simulation Scenario** and **Run Seed Material** include `players=N` separately from Role counts because Thief changes card count.
- "Prejudiced Manipulator groups in Role Composition" — resolved: group splitting is not part of Role Composition; even split is the baseline simulator profile default, and only non-default group models need explicit **Simulation Scenario** material.
- "Prejudiced Manipulator groups" — resolved: use **Public Group Partition** for the immutable two-group setup artifact and **Opposing Public Group** for the block not containing the current living Prejudiced Manipulator **Faction Beneficiary**.
- "Mid-game projection vs pre-game simulation" — resolved: the simulator runs from a **Simulation Start State**. Pre-game cache generation derives that state from a **Simulation Scenario**; mid-game projection uses the same simulation mechanism from a later fully defined Game Session state.
- "Scenario classification vs start states" — resolved: degenerate screening classifies a **Simulation Scenario**, while each completed screening run starts from its own seeded **Simulation Start State** derived from that scenario.
- "Actor as Ambiguous Role" — resolved: Actor is a hard-aligned Villager **Role**, counts toward hard-aligned Villager Role Composition requirements, and **Actor Setup Cards** provide powers only without affecting Faction lifecycle.
- "Valid Role Composition" — resolved: split into **Rules-Valid Role Composition**, **App-Supported Role Composition**, named **Capability-Supported Simulation Scenario**, and **Cacheable Simulation Scenario** instead of overloading "valid."
- "Mandatory Simple Werewolf/Simple Villager" — resolved: the domain requires hard-aligned Werewolf and Villager coverage, not Simple Werewolf or Simple Villager by role name.
- "Already-decided/degenerated on unsupported inputs" — resolved: already-decided and degenerate classification only apply after rules, app, and simulator support checks pass.
- "Supported roles" — resolved: use **Rules Role Set**, **Implemented Role Set**, **Safety-Screening Role Set**, **Full-Probability Role Set**, or **Selectable Role Set** depending on the boundary being discussed.
- "Town Crier as Role" — resolved: Town Crier is a **New Moon Assignment** like Sheriff, not part of the **Role Composition**.
- "Player cap vs card count" — resolved: **Supported Player Count** caps Players only; Thief extras and Actor Setup Cards can increase physical card count without increasing Player count.
- "Zero-frequency results" — resolved: cache-facing probability output includes the Simulation Scenario's **Possible Game Results** as rows, including zero-frequency single-Faction rows for every **Possible Faction**, zero-frequency **No-Winner Outcome**, and zero-frequency scenario-specific **Shared Victory Outcome** combinations.
- "Shared victory probability" — resolved: cache-facing probability output represents a **Shared Victory Outcome** as its own **Game Result** instead of crediting each winning Faction separately.
- "Werewolf parity" — resolved: parity is a **Werewolf Control Shortcut** for Villager-vs-Werewolf endgames, not the Werewolf Faction's full win condition once active solo or latent Factions are present.
- "Operational Faction vs beneficiary Faction" — resolved: use **Faction Beneficiary** for win-condition membership and **Faction Agent** for operational behavior such as waking, acting, or being perceived with a Faction.
- "Seer detection" — resolved: Seer checks whether the target is a Werewolf **Faction Agent**, not whether the target is a Werewolf **Faction Beneficiary**.
- "Dual Faction beneficiaries" — resolved: in the current ruleset, each Player has exactly one beneficiary Faction; beneficiary changes are exclusive replacements even when operational behavior, wake groups, or public identity stay unchanged.
- "Infection beneficiary" — resolved: infection changes **Faction Agent** status but does not change **Faction Beneficiary**.
- "Permanent Role Swap beneficiary" — resolved: permanent Role swaps change **Faction Beneficiary** to the new Role's default Faction unless an explicit precedence rule says otherwise.
- "Wolf Hound choice as Role swap" — resolved: the Player remains the Wolf Hound **Role** with the Wolf Hound Character Card after either choice; the chosen nature is Faction state, not a **Permanent Role Swap**.
- "Wolf Hound reveal identity" — resolved: revealing the physical Character Card identifies the **Role** as Wolf Hound but does not automatically disclose the private Villager-or-Werewolf choice.
- "Cross-Faction Lovers precedence" — resolved: Cross-Faction Lovers keep their beneficiary precedence over later effects; Lover status blocks Devoted Servant's swap ability, and Miracle reviving only one eliminated Lover breaks the Cross-Faction Lovers outcome.
- "Werewolf voting control" — resolved: **Werewolf Control Shortcut** uses **Durable Voting Power** and ignores temporary vote effects.
- "Generic Werewolf checks" — resolved: use Werewolf **Faction Agent** unless a rule explicitly refers to Role or Character Card.
- "Elder power loss" — resolved: **Role Power Suppression** begins after the Elder's village-vote **Elimination Cascade**, affects all current and future Villager **Roles** including Actor regardless of **Faction Beneficiary**, and blocks only new power effects rather than undoing committed or resolved ones.
- "Big Bad Wolf disablement" — resolved: the extra attack is disabled once any non-temporary Werewolf **Faction Agent** has been Eliminated; Full Moon Rising's temporary Werewolf Faction Agents do not count.
- "Temporary Werewolf Faction Agents" — resolved: Full Moon Rising affects operational Werewolf Faction Agent checks while active, but does not change Faction Beneficiaries or Big Bad Wolf disablement.
- "Double Agent eligibility" — resolved: the target must be a living non-Werewolf **Faction Agent**.
- "Angel expiry" — resolved: Angel's transient Faction expires immediately after the Dawn **Victory Check Window** that resolves Night 2 if the Angel did not win.
- "Victory timing" — resolved: win conditions are checked at **Victory Check Windows**: Dawn after Night resolution, and before the next Night after Day resolution. There is no separate Dusk Phase.
- "Win-condition priority" — resolved: evaluate all win-condition predicates in the same **Victory Check Window** before deciding the **Game Session Outcome**.
- "Tie" as final result — resolved: use **Shared Victory Outcome** for multiple Factions winning in the same **Victory Check Window**; keep "tie" for Vote ties.
- "Everybody dies" — resolved: use **No-Winner Outcome** for completed Game Sessions where no Faction wins.
- "Balanced" vs "long enough" — resolved: **Balanced Role Composition** is interpreted from **Game Result Frequency**; **Reference Turn Horizon** is not used to block Role Compositions.
- "Balance judgment" — resolved: the production app does not currently present **Game Result Frequency** or **Ended-By-Turn Frequency** as balance guidance; it blocks **Already-Decided Role Compositions** and **Degenerate Simulation Scenarios** only.
- "Already-decided evidence" — resolved: **Already-Decided Role Composition** means the committed **Deal Pool** projection of the locked **Simulation Scenario** would already trigger a Faction victory at **Lobby Exit**; offer-only Roles do not count.
- "Already-decided shared outcomes" — resolved: if multiple Faction victory predicates are already true at **Lobby Exit**, preserve them as a **Shared Victory Outcome** without priority ordering.
- "Already-decided as simulation result" — resolved: **Already-Decided Role Composition** records share outcome language but are not simulation runs and do not have **Run Seed Material** or per-run simulation evidence.
- "Degenerate threshold" — resolved: do not use a percentage threshold; a legal supported branch is **Degenerate** only when its 1,000-run baseline screening completes every run and observes only Turn 1 endings. Screen every semantically distinct legal Thief branch and block the **Simulation Scenario** if any one branch is Degenerate.
- "Could not evaluate" vs blocked — resolved: incomplete screening is an error state and does not block **Lobby Exit** as already-decided or degenerate.
- "Could not evaluate as outcome" — resolved: "could not evaluate" is a product evaluation state, not a **Game Session Outcome** or probability bucket.
- "Current runtime limits" — resolved: the full Faction model remains the domain contract, but each named **Simulator Capability** controls which scenarios it can evaluate; unsupported or unevaluable scenarios are not mislabeled as already-decided or degenerate.
- "Cache miss vs Lobby Exit" — resolved: no usable or current terminal lobby evaluation blocks **Lobby Exit** while evaluation is pending; a failed evaluation releases the gate, and safety-only production does not surface the dormant "could not evaluate" panel.
- "Local fallback reuse" — resolved: a successful terminal fallback classification materializes a compact **Local Fallback Cache Record** that can be reused across app restarts while its cache identity remains current; a screening pass need not be persisted.
- "Cache/fallback UX distinction" — resolved: Moderator-facing lobby evaluation status does not distinguish bundled lookup from fallback generation; cache hits simply complete the same evaluation flow faster.
- "Fallback result storage" — resolved: **On-Device Fallback Generation** may compute enough to classify a scenario locally, but the materialized result is only a compact terminal lobby evaluation, not full per-run simulation evidence.
- "Fallback failure" — resolved: incomplete fallback, timeout, instruction-limit exhaustion, runtime cancellation, start-state generation failure, and incomplete screening collapse to "could not evaluate" rather than already-decided or degenerate claims; dormant full evaluation likewise never publishes partial probability, and failure releases the Lobby Exit safety gate.
- "Setup changes during fallback" — resolved: changing the selected setup discards any in-progress fallback for the stale **Simulation Scenario** and starts evaluation for the new stable scenario instead of releasing the safety gate.
- "Fallback timeout" — resolved: the only product hard boundaries for **On-Device Fallback Generation** are any generation failure and a 10-second timeout.
- "Manual fallback skip" — resolved: the Moderator cannot dismiss or skip an in-progress fallback evaluation; proceeding without an evaluation requires fallback failure or timeout.
- "Fallback failure memory" — resolved: failed fallback state is session-only for the current unchanged setup, is not persisted across app restarts, and prevents automatic retry loops.
- "Fallback retry" — resolved: explicit retry remains part of dormant full-evaluation presentation; safety-only production exposes no retry action after failure.
- "Simulator-unsupported setup" — resolved: app-supported setups outside the **Safety-Screening Role Set** do not show an evaluation panel and do not block **Lobby Exit** solely because simulation is unavailable.
- "Seed" — resolved: store **Run Seed Material** as a canonical string for replay evidence; hash it into a numeric seed only when constructing a random generator.
