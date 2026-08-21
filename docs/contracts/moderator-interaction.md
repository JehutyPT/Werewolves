# Moderator interaction contract

This document defines the durable, observable interface between the app, the Moderator, designated Players, and the physical table. It records when an exchange occurs, who may see it, who physically decides, what the Moderator records, and when the exchange commits.

When a Role specialization is silent, the shared interaction rules apply.

## Scope and ownership

- This contract owns observable Moderator exchanges and reusable interaction semantics.
- [Game rules](../domain/game-rules.md) and [game-rule clarifications](../domain/game-rules-clarifications.md) own physical-rule eligibility, effects, outcomes, and canonical ordering. Role specializations link to or summarize those rules only where needed to make an observable exchange unambiguous.
- [Domain invariants](../domain/invariants.md) own short stable facts. [ADRs](../adr/) own architectural trade-offs and rationale.
- Implementation issues own their delivery slice, dependency assumptions, acceptance criteria, and verification evidence. Code and tests own the implementation.
- Concrete UI layout, localized copy, payload type names, persistence schema, and test structure stay outside this contract unless a particular interaction gesture is itself a settled product rule.

## Shared interaction rules

### Physical authority and Role knowledge

- The physical table is authoritative. The app records the Role Composition and facts the Moderator observes; it does not shuffle, deal, guess, or probabilistically deduce an unknown Player-specific Role. It may deterministically constrain possible Roles when complete committed facts plus the locked Role Composition entail the constraint, as defined by [ADR-0015](../adr/0015-role-ownership-knowledge-and-public-reveal-are-separate.md); a candidate remains distinct from a known Role.
- Physical Character Card Ownership, current Role, Moderator knowledge, and public reveal are separate facts.
- A Player-to-Role observation records the printed Role and never asks the Moderator to distinguish identical copies; internal accounting-ID binding and configured slot identity are defined by the [domain invariants](../domain/invariants.md) and [ADR-0015](../adr/0015-role-ownership-knowledge-and-public-reveal-are-separate.md).
- A Permanent Role Swap separately defines the new current Role, physical-card handling, and visibility.
- For a Dawn victim, any applicable pre-reveal intervention occurs before the public Role Reveal and Elimination.

### Participation and Role Identification

- **Role Identification** privately records the complete living holder set observed during an exact-Role call. It neither assigns Character Cards nor reveals the Role publicly.
- **Faction Agent Group Observation** privately records the complete non-empty group observed during a collective Faction call. It does not identify exact Roles.
- **Role Reveal** records a public physical reveal. It changes public knowledge, not current Role.
- An instruction that names, wakes, or privately addresses an exact Role holder requires an exact holder set. At the first genuine Role step that needs that set, the app requests Role Identification only while it is unknown. A known set suppresses only repeated identification; any remaining action, recognition, communication, feedback, or sleep step still occurs.
- When the app knows that no living Player can answer a scheduled Night Role call, it omits that entire call. This rule does not suppress public reveals, Elimination reactions, automatic consequences, or other non-Night exchanges. An unknown participant set is not treated as empty.
- An explicitly named identification-only slot occurs at its stated time and is omitted when the required identity is already known.

### Initial Beneficiary Closure

- Faction Agent Group Observation commits Agent membership only; it does not commit a Faction Beneficiary or exact Role.
- After all applicable Night 1 observations, choices, and automatic transitions that can establish or override an initial Beneficiary have committed, the app performs Initial Beneficiary Closure automatically before any Beneficiary-dependent interaction or consequence.
- Closure creates no Moderator Instruction, Moderator Response, Continue acknowledgment, or separate recovery checkpoint. It never asks the Moderator to confirm a derived Beneficiary.
- An incomplete prerequisite set makes the automatic closure readiness check a no-op and commits no residual or deferred Beneficiary facts. Independent canonical interaction may continue, but the app cannot enter a Beneficiary-dependent consumer while a required fact remains unresolved; reaching one is an invariant failure. Neither case creates a gameplay choice or acquisition exchange.
- The Moderator Response or automatic transition that supplies the final prerequisite commits that prerequisite, one closure batch containing only newly entailed residual and deferred facts that retain their effective historical boundaries, and the next Pending Instruction at one ordinary stable boundary. Previously committed explicit facts are neither rewritten nor re-appended. Rehydration observes either the pre-closure state, from which the readiness check may run again, or the complete post-closure projection; it never exposes a partial result, a provisional Beneficiary, or a correction path.
- Candidate calculations and private Faction facts never appear in public-table copy, public history, or validation errors. Only final legitimately Known facts may appear on Moderator-only surfaces.

### Pre-game configuration

- The Moderator commits the Role Composition, Deal Pool, and any Thief Offer Cards before the Physical Deal.
- The Moderator completes any Role-required setup before the Physical Deal.
- The app records setup created by the Moderator; it does not generate or balance the physical setup.
- Role Lock-In and Actor Setup Card acceptance commit the current staged artifacts, not the final Game Session setup. Until Lobby Exit, the Moderator may replace the complete Role Composition—including its Deal Pool and any Thief Offer Cards—or the complete three-card Actor Setup Card set any number of times. Each replacement wholly supersedes the earlier artifact. After Lobby Exit, correction requires a new Game Session.

### Decision agency

- Players and called groups make gameplay choices physically. The Moderator records their completed choices.
- The Moderator chooses only table-management facts explicitly assigned to them, such as the Role Composition, required setup, and resolved Day Vote result.
- Automatic consequences require no gameplay choice from the Moderator.
- When no legal target exists, the app omits only the target-selection step unless a Role specialization explicitly omits the whole call. Independent wake, sleep, recognition, feedback, and consequence steps remain.

### Audience and visibility

- Instructions distinguish public-table copy, Moderator-only guidance, and designated Player-private messages.
- A public Character Card reveal discloses the printed Role but not a separate private choice such as Wolf Hound's Faction.
- Private facts may be shown to the Moderator after they are legitimately known but never appear in public copy or public history.

### Response language

| Phrase | Meaning |
|---|---|
| **Continue acknowledgment** | Confirm that the instructed announcement, feedback, or physical step is complete. |
| **Identify exactly N Players** | Record the complete observed set of N exact-Role holders. |
| **Observe a Faction Agent group** | Record the complete observed non-empty group for one Faction operation without identifying exact Roles. |
| **Select exactly one Player** | Record one Player chosen from the current legal set. |
| **Select zero or one Player** | Record one legal Player or a permitted decline. |
| **Select the required number** | Record the number of distinct Players required by the current state. |
| **Select a Player subset** | Record any permitted subset of the stated legal set. |
| **Choose exactly one option** | Record one gameplay option. |
| **No-legal-target path** | Omit only the target-selection step; any separate wake, sleep, recognition, feedback, or consequence still occurs. |
| **Automatic** | The app derives the consequence; the Moderator makes no gameplay choice. |
| **Generic public reveal** | Every requested Player whose Role is not public physically reveals it before the consequence resolves. |

### Submission and commitment

- Every advance that irreversibly commits a Moderator Response or observed physical event uses press-and-hold submission. A successful submission commits immediately; there is no later review or correction boundary.
- This immediate commitment applies to Role Identification, Faction Agent Group Observation, and both forms of Role Reveal response: acknowledging a known Role's physical reveal and recording a Player-to-Role mapping when it was unknown. Invalid, stale, incomplete, or canceled submissions are rejected before they change state or advance.
- If the Moderator later notices that an accepted observation recorded the wrong Players or Roles, the active Game Session is not rewritten. The Moderator must start a new Game Session.
- Recovery may re-present only a response that never committed. Successful acceptance durably preserves the observation or public event and the next pending instruction, so Rehydration never reopens it as a same-session correction path.
- A Continue acknowledgment may use a standard tap only when it records no irreversible decision, observation, public event, or other game state.

## Shared Day Vote

The Moderator resolves the physical Vote and records one living Vote Target or a tie. The app does not collect individual ballots or calculate vote totals.

## Role interaction specializations

These sections state only the observable differences from the shared interaction rules. The linked game rules remain authoritative for eligibility, effects, outcomes, and the [canonical call order](../domain/game-rules.md#turn-order-summary-adapted-from-page-24-excluding-building-dependencies-and-incorporating-settled-rulings).

### Simple Werewolf

- On each collective call, public copy wakes the current Werewolf Faction Agents and opens the Little Girl spying interval.
- When the app does not yet know the complete group, the Moderator performs Faction Agent Group Observation for the complete non-empty group that woke. When the complete group is already known, the Moderator only confirms that the physical wake occurred.
- The group physically chooses one living non-Agent victim, which the Moderator records. Public copy then returns the group to sleep and closes the Little Girl spying interval.
- On a later Night when the app knows the living Werewolf Faction Agent group is empty, the entire collective call is omitted and the app continues at the next canonical Night slot.
- If one or more expected Agents fail to wake during the collective call, the app records no error or recovery branch; the Moderator handles that table mistake.

### Simple Villager

- No Role-specific instruction or response occurs.

### Seer

- Each Night, perform Role Identification if needed, wake the Seer, record the other living Player chosen for inspection, convey the Werewolf Faction Agent result privately, and return the Seer to sleep.
- The target and result remain known only to the Seer and Moderator unless another rule reveals them.

### Wild Child

- On Night 1, perform Role Identification if needed, wake the Wild Child, record the other Player chosen as role model, and return the Wild Child to sleep.
- If the model is Eliminated, the resulting Faction change is private and automatic; no transformation announcement occurs.

### Villager-Villager

- Its two-sided Character Card makes the holder public during the Physical Deal. No later proof interaction occurs.

### Two Sisters

- On Night 1, publicly call Two Sisters. If their holders are unknown, perform Role Identification for both Players; they recognize one another and then return to sleep.
- The app requires a silent-communication interval on Nights 3, 5, 7, and later odd Nights while both current holders are living. The Moderator continues after communication and again after the group returns to sleep.
- The physical rule is discretionary; the fixed odd-Night schedule is the app-supported presentation.

### Three Brothers

- On Night 1, publicly call Three Brothers. If their holders are unknown, perform Role Identification for all three Players; they recognize one another and then return to sleep.
- The app requires a silent-communication interval on Nights 3, 5, 7, and later odd Nights while at least two current holders are living.
- The physical rule is discretionary; the fixed odd-Night schedule is the app-supported presentation.

### Witch

- Wake the Witch and privately show every Player targeted by a physical Werewolf attack that Night.
- The Moderator records any potion choice made by the Witch. Candidate generation and validation enforce the [canonical Witch target rule](../domain/game-rules.md#the-villagers). Unavailable potions and target steps with no candidates are omitted.
- Return the Witch to sleep after all available choices are complete.

### Hunter

- When Hunter is actually Eliminated, complete the public Role Reveal and earlier forced heartbreak reactions first.
- If another Player is living and the power is available, record the Hunter's mandatory final-shot target within the same Elimination Cascade.

### Stuttering Judge

- On Night 1, perform Role Identification if needed, establish the physical signal without recording it, and return the Judge to sleep.
- During an eligible first Vote, the Moderator records whether the signal occurred before that Vote resolves.
- A valid signal spends the power and commits a Consecutive Vote after the first Vote and its Elimination Cascade.
- If no Player can vote in the committed Consecutive Vote, no physical Vote, tie, or Elimination occurs.

### Scapegoat

- A qualifying tie publicly reveals and Eliminates the Scapegoat instead of the tied Players.
- The Scapegoat chooses one or more other living Players who may vote the following Day. The Moderator records and publicly announces that fixed list.
- The restriction does not apply to a same-Day Consecutive Vote.

### Wolf Hound

- On Night 1, perform Role Identification if needed, wake the Player, record the private Villager-or-Werewolf choice, and return the Player to sleep.
- The accepted identification and choice establish the pre-choice Villager Beneficiary through Cupid's earlier boundary and the selected branch transition at the Wolf Hound call; Initial Beneficiary Closure preserves both effective times.
- A later Character Card reveal discloses Wolf Hound but not the private choice.

### Accursed Wolf-Father

- Participation in the collective Werewolf group does not identify the exact Role.
- After the collective victim is fixed, perform Role Identification if needed, wake the holder, and record whether the holder infects that victim instead of allowing the physical attack to Eliminate them. The [canonical Role rule](../domain/game-rules.md#the-werewolves) owns the resulting state changes.
- Return the holder to sleep.

### Big Bad Wolf

- Participation in the collective Werewolf group does not identify the exact Role.
- While the extra attack is available, perform Role Identification if needed, wake the holder, and return the holder to sleep. When at least one legal target exists, record the Player physically chosen by the holder; otherwise the shared no-legal-target path omits only that selection.

### Little Girl

- On Night 1, perform Role Identification in an identification-only slot if the holder is unknown. If already known, omit that slot.
- When the collective Werewolf call occurs, its wake and sleep steps delimit the spying interval.
- No peek, detection, success, or failure is recorded.

### Defender

- Each active Night, wake Defender, record one living Player chosen from the legal set, and return Defender to sleep.
- The app derives that legal set from the [canonical Defender rule](../domain/game-rules.md#the-villagers); the contract adds no separate eligibility rule.

### Village Idiot

- After pre-resolution Vote interventions pass, an unrevealed Village Idiot who still holds an available pardon publicly reveals.
- The app cancels that Vote's Elimination, spends the pardon, removes the Player's voting right, and presents the public consequence.
- If Devoted Servant acts first, Village Idiot transfers before this check; the original Vote Target is Eliminated without reveal, pardon spend, or voting-right loss.
- Later Votes eliminate the Player normally once the pardon is spent.

### White Werewolf

- On Night 1, privately identify White Werewolf only if the holder is unknown, at its ordinary relative slot after Accursed Wolf-Father and before Big Bad Wolf; no solo attack occurs.
- The committed identification establishes the Agent-with-different-Beneficiary exception used by Initial Beneficiary Closure. Collective-group membership alone never establishes a Werewolf Beneficiary for White Werewolf.
- On each even-numbered Night, wake the holder and record either one other living Werewolf Faction Agent target or a permitted decline.
- Odd Nights have no solo call.

### Bear Tamer

- Perform Role Identification for Bear Tamer before the first Dawn evaluation only if the holder is unknown.
- After Dawn victims and their complete Elimination Cascades resolve, the app begins the Dawn Main Action Loop by checking the holder's Living Neighbors.
- When the condition is true, the app tells the Moderator to grunt publicly and waits for confirmation. Otherwise no Bear Tamer instruction appears.
- Gypsy and then Town Crier complete their Dawn actions after that confirmation or silent Bear Tamer completion.
- The Dawn Victory Check Window begins only after the entire Dawn Main Action Loop completes.

### Fox

- Each Night while the power remains available, wake Fox and record either a living center Player, including Fox, or a decline.
- The app computes the result from known current Werewolf Faction Agent state. A positive result preserves the power; a negative result removes it. The Moderator conveys the result privately.
- Declining gives no result and preserves the power. Return Fox to sleep afterward.

### Knight with the Rusty Sword

- No chosen Night input occurs.
- The [canonical Knight rule](../domain/game-rules.md#the-villagers) determines whether a disease is scheduled and for whom without a Moderator prompt.
- At the following Dawn, the app tells the Moderator whom the disease Eliminates and what cause to announce. If no living Player remains subject to the scheduled disease, no instruction appears.

### Piper

- Wake Piper and record exactly two distinct eligible targets when at least two exist, the sole target when one exists, and none when none exist.
- After Piper sleeps, show the living Charmed roster only to the Moderator, wake those Players for recognition, return them to sleep, and continue after they are all asleep.
- No recognition content is recorded.

### Cupid

- Wake Cupid and record exactly two distinct living Players chosen as Lovers; Cupid may be one.
- Privately wake or tap the pair, guide them to recognize one another, and return them to sleep. The pair remains private.
- The pair selection and physical recognition commit at Cupid's ordinary call even when classification remains pending. The [canonical Cupid rule](../domain/game-rules.md#the-villagers) owns relationship classification; Initial Beneficiary Closure commits that classification without another Moderator Response.

### Thief

- The Night 1 call uses the two offers already committed during [physical setup](../domain/game-rules.md#setup); the Moderator does not enter them again.
- On Night 1, publicly call Thief, perform Role Identification for the one Player who woke, and show that Player the two committed offers.
- Record the chosen offer or a decline. Decline is unavailable when both offers are hard-aligned Werewolf Roles.
- A chosen offer immediately becomes the Player's owned Character Card and current Role. The original Thief card and unchosen offer become face-down Set-Aside Character Cards.
- A committed choice or decline resolves the offer-dependent Initial Beneficiary Closure branch. The acquired Role and Beneficiary are effective immediately; unchosen Set-Aside offers are no longer closure prerequisites.
- Continue only through Night 1 calls still ahead. The acquired holder is not identified again, but all remaining genuine Role behavior still occurs.
- A legal decline keeps the Thief card and Role and moves both offers face-down to the Set-Aside zone.
- The offers, choice, resulting card zones, and current Role remain private. Return the Thief Player to sleep after the choice.

### Angel

- Angel has no identification call or strategy choice. Its presence comes from the locked Role Composition; the app never privately establishes its holder merely because Angel is present.
- Angel creates no Initial Beneficiary Closure prerequisite or exception. Its holder uses ordinary Villager Faction Beneficiary mechanics throughout, whether or not that holder is known.
- Every actual Elimination of the physical Angel card's holder during Night 1, any part of Day 1, or Night 2 qualifies the card's Role-Card Victory Eligibility for the next shared Victory Check Window. This includes standard and Consecutive Votes and reaction or cascade Eliminations. The ordinary public reveal and ownership observation occur before the eligibility is consumed; they are not an Angel identification flow.
- If Angel has not won by the Dawn Victory Check Window that resolves Night 2, its eligibility expires immediately after that window. From then on, the physical Angel card supplies Simple Villager current-Role mechanics without a physical card action, ownership or visibility change, Player notification, acknowledgment, or response. An unknown holder remains unknown; a later ordinary reveal or ownership observation projects Simple Villager mechanics without retroactive identification.

### Devoted Servant

- After the Moderator records a non-tied standard or Consecutive Vote Target, the app pauses before any target reveal or Role-specific handling. No prompt appears for a tie or a non-Vote Elimination.
- If nobody acts, the Moderator continues and the power remains unused.
- On Use, the Servant reveals and discards their own Character Card, then takes and reads the Vote Target's card while keeping it hidden from the table. The Moderator also reads and records the acquired Role.
- The app commits the swap and resumes the same Vote against the unchanged target without running that target's former Role-specific handling.
- Reading and recording the acquired card establishes the new exact Role; no later generic re-identification occurs.

### Elder

- On the first unblocked attack requiring an Elder check, perform Role Identification only if the holder is still unknown.
- The first resistance is publicly silent and requires no acknowledgment.
- After a qualifying village-Vote Elimination and its cascade, the app publicly announces Role Power Suppression before any Consecutive Vote.

### Prejudiced Manipulator

- When the Role appears in the Deal Pool or as a Thief Offer Card, the Moderator creates and publicly announces two non-empty groups, then records every Player in exactly one group.
- The app stores the Player-to-group mapping and never creates or balances the groups.
- At the canonical Night 1 Prejudiced Manipulator slot, publicly call the Role for an otherwise-unknown Deal Pool holder, perform Role Identification for exactly one Player, and return that Player to sleep. If a committed Thief choice or another transition already established the current holder, or if the card is Set-Aside, present no instruction or response and continue at the next canonical slot.
- The call adds no Role Power, gameplay choice, feedback, or public Role Reveal. Once the identification commits, it is never repeated. Later Eliminations, Faction changes, and Permanent Role Swaps create neither another Prejudiced Manipulator identification response nor a pause at a Victory Check Window. The [game-rule clarification](../domain/game-rules-clarifications.md#prejudiced-manipulator-public-groups-and-outcome) owns why the exact early identity remains required.
- If the recorded mapping differs from the announced groups, the Moderator may replace it before Lobby Exit only with the complete announced partition. This corrects the app's record without regrouping Players. After Lobby Exit, correction requires a new Game Session.

### Actor

- When Actor appears in the Deal Pool or as a Thief Offer Card, the Moderator chooses and records three distinct eligible face-down Actor Setup Cards outside the Role Composition.
- Each Night, wake Actor and record either one remaining Actor Setup Card chosen by the Player or a decline. After a choice is recorded, the app does not offer that card again and presents its Borrowed Role Power at the ordinary boundary until the next Actor call.
- The selected card is private to Actor and the Moderator. Actor remains the current Role; source-holder identification and source-card reveal do not occur.
- Prompts for a Borrowed Role Power identify Actor as the acting Player while preserving the source power's ordinary choices and consequences.
- While Role Power Suppression is active, the Actor slot expires any previous Borrowed Role Power and advances without waking Actor or requesting a response.
