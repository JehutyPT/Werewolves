# Moderator interaction contracts for Role support v1

This document states what the app tells the Moderator, what the Moderator records, what stays private, and what the headless simulator must be able to answer for every Role in PRD #93. It is a normative domain constraint and shared vocabulary, not a competing implementation spec. Each Role's canonical GitHub issue-body Implementation Contract remains the implementation authority and must copy its owned flow from here.

A flow is settled unless it is marked **Open in #147**. An implementation ticket affected by an open item remains blocked until issue #147's canonical acceptance checklist records one answer and that answer is copied here and into the affected contract.

## Shared contract

### Physical authority and Role knowledge

- In a live Game Session, the physical table is authoritative. The Moderator selects the Role Composition and records observed setup or play facts; the app validates and persists them but never shuffles, deals, randomly assigns, or deduces an unknown Player-specific Role from missing information.
- Players draw their face-down Character Cards. Knowing the Role Composition does not tell the app which Player holds a physical card or current Role. Physical Character Card Ownership, current Role, Moderator knowledge, and public reveal are separate state.
- **Role Identification** privately records which Player or Players physically answered an exact-Role call or otherwise established that current Role to the Moderator. It does not perform the Physical Deal, mutate Physical Character Card Ownership, or reveal the Role publicly.
- **Faction Agent Group Observation** privately records the complete Player group that physically answered a collective Faction call. It records operational Agent membership without identifying or mutating any exact Role.
- **Role Reveal** always records a public physical event when the Role is not already publicly revealed, even if it was Moderator-known. A known Role uses a Continue acknowledgment after the physical reveal; an unknown Role uses a complete mapping for exactly the requested Players. Reveal changes public knowledge, not current Role.
- For a pending Dawn or Vote victim, an explicit pre-reveal interception (such as Devoted Servant) runs first. Generic public reveal commits next. Core commits the actual Elimination or its rule-defined replacement only after that reveal, then drains every resulting reaction in the same Elimination Cascade.
- Every recorded identification, reveal, card-zone fact, and mapping must respect Role Composition multiplicity, one-to-one Player/card ownership, unique physical card instances, prior observations, public history, and confirmed dealt or undealt zones.
- An unidentified Role is not treated as Simple Villager and supplies no guessed Faction, trigger, or outcome fact. A rules step that needs the fact must obtain it before resolving.
- Rules-level Faction Beneficiary and Faction Agent truth is separate from Known Faction State. A query over an unknown live Faction fact returns unresolved; it cannot silently derive that fact from Role Composition or the remaining cards.
- A Core-authored rules transition may explicitly commit a new current Role; that is not inference about an unknown deal. Every Permanent Role Swap separately defines current Role, physical-card handling, and visibility.
- In simulation, seeded Simulation Start State is the hidden truth. The headless strategy answers identification and reveal instructions from seeded card truth plus Core-committed swaps, never by randomly assigning a remaining Role when prompted or exposing hidden truth to ordinary strategy choices.
- **Open in #147:** the correction workflow for an incorrectly recorded setup fact, Role Identification, Faction Agent Group Observation, or Role Reveal.
- **Open in #147:** the exact private acquisition flow when Fox, Cupid, a Victory Check Window, or another earlier rule needs a Faction Beneficiary or Agent fact that no physical observation or Core-authored transition has established yet. The ruling must define timing, requested key set, response shape/cardinality, visibility, validation, commitment, recovery, and headless behavior. Once settled, #120 owns the shared runtime exchange; consumer tickets request facts through it rather than inventing Role-specific prompts.
- **Open in #147:** which Role cards may remain undealt when Thief is selected, how exact-Role identification cardinality derives from the active dealt card zone, and what a scheduled call does when no Player holds that Role. Role Composition count alone never authorizes the app to invent a holder.
- Until #147 settles that rule, any Role row below that says to identify “the holder” or a printed holder count is conditional shorthand, not permission to assume that a holder exists. The affected implementation ticket must remain blocked and replace the shorthand with the selected zero/one/many path before preparation.

### Pre-game configuration

- Role Composition is always a lobby input. Actor Setup Cards are required only when Actor is in Role Composition, and Public Group Partition is required only when Prejudiced Manipulator is in Role Composition. All three are validated lobby/configuration inputs outside the in-session Moderator Instruction/Response cycle; PRD #93 does not add a Setup phase to Core.
- The client presents public setup copy for the Moderator to announce at the table. Persisted config records what the Moderator created; it never generates or balances live setup.

### Decision agency

- Players or called groups make gameplay choices physically. The Moderator records their completed target, option, or subset; wording such as “select” in a response shape never transfers gameplay agency to the Moderator.
- The Moderator owns only explicitly named table-management choices: Role Composition, Actor Setup Cards, Public Group Partition, and recording the already-resolved final Day Vote target or tie.
- Core owns automatic consequences and derived targets. The Moderator never supplies a choice merely to confirm a rule Core can derive.

### Visibility

- A Moderator Instruction separates public-table copy, Moderator-only guidance, and any designated Player-private message. Only public-table copy is read aloud to everyone. The Moderator may discreetly convey a designated private result to the named Player without making it public.
- A public Character Card reveal discloses the Role printed on the card. It does not disclose a separate private choice such as Wolf Hound's Faction branch.
- Private facts may appear in the Moderator-only dashboard after they are legitimately known. They must not leak through public copy, public logs, public roster/history projections, sound descriptions, or validation errors.

### Response language

| Phrase | Exact meaning |
|---|---|
| **Continue acknowledgment** | One-way confirmation that an instruction, announcement, feedback step, or physical action is complete. It means “continue,” not yes/no; `false` is not another valid branch. |
| **Identify exactly N Players** | Record exactly N observed exact-Role holders. The instruction carries machine-stable Role identity separately from Portuguese text. |
| **Observe a Faction Agent group** | Record the complete observed group for one machine-stable Faction operation without identifying exact Roles. |
| **Acquire required Faction facts** | Pause the requesting rule on a Moderator-private instruction naming the exact Player/fact keys. #147 settles the allowed values and cardinality; #120 validates and commits the response once, then resumes that same rule. |
| **Select exactly one Player** | Select one Player from the current legal set. Empty, multiple, illegal, and stale selections are rejected. |
| **Select zero or one Player** | Select one legal Player or nobody. Nobody means decline only where the Role row says so. |
| **Select the required number** | Submit the exact distinct-Player count calculated from current state. |
| **Select a Player subset** | Submit any distinct subset of the stated legal set. Empty is valid only when the Role row says so. |
| **Choose exactly one option** | Choose one machine-stable semantic option with a separately localized label. |
| **No-legal-target path** | The app never creates an empty-candidate Player selector. **Open in #147:** whether each affected call is omitted or presents a Moderator-only explanation plus Continue acknowledgment. |
| **Automatic** | Core derives the consequence from committed state. The Moderator supplies no gameplay choice; a row may still require an informational acknowledgment. |
| **Generic public reveal** | Every not-yet-public requested Player physically reveals. Already-known Roles use a Continue acknowledgment; unknown Roles require a complete, physically valid mapping for exactly the requested key set. Both paths commit public-knowledge state before automatic consequences. |

The response foundation must make Continue acknowledgments distinct from yes/no gameplay choices, carry machine-stable option identities separately from localized labels, and correlate a response to the exact pending instruction so stale same-shape responses are rejectable. #110 owns that shared migration. #113 owns typed exact-Role identification and public-reveal responses; #121 owns typed Faction Agent Group Observation; after #147 settles its exact shape, #120 owns the private acquisition exchange for required unknown Faction facts; #111 owns acting-Player/source-power/resource identity; #135 and #136 own physical card-instance and zone payloads; #140 owns the typed Public Group Partition payload.

The headless contract is mechanical, not strategic: it must produce a legal deterministic response for every reachable instruction and complete safety-screening runs. It does not promise intelligent play, probability usefulness, a 10,000-run batch, or bundled-cache coverage.

## Shared Day Vote

The app asks the Moderator to record the final physical Vote result. The Moderator selects exactly one living Player or nobody; nobody means a tie. The app rejects illegal, multiple, and stale selections and never collects individual ballots or calculates vote counts.

## Role flows

### Simple Werewolf

- The collective Werewolf operation owns the Night flow. Each Night, public-table copy wakes the current group and opens the Little Girl spying interval. At the first unresolved call—or whenever current Agent membership is not fully known—a private Faction Agent Group Observation records the complete distinct living group physically answering that call; when the complete group is already known, the Moderator instead submits a correlated Continue after the physical wake. Neither path identifies an exact Role.
- A committed Group Observation is table-authoritative: observed living Players become known current Agents and the unobserved living complement becomes known non-Agents for that moment. Validation rejects duplicates, dead Players, stale or mismatched responses, and contradictions with legitimately known state; it cannot reject a “missing” hidden Player by inferring the deal.
- The woken group physically chooses exactly one living non-Agent victim and the Moderator records it, then submits Continue after the public sleep instruction, which closes the Little Girl spying interval.
- If no legal non-Agent exists, no victim selector is shown. **Open in #147:** the shared no-legal-target presentation.
- **Open in #147:** the zero-Agent/empty Group Observation path, including whether any wake, empty observation, victim, or sleep step appears and how recovery and headless execution complete it.

### Simple Villager

- No Role-specific instruction or response exists. The Role participates only in shared setup, Vote, Elimination, and generic public-reveal flows.

### Seer

- On the first call, privately identify exactly one Seer if unknown. Each Night, wake the Seer, record exactly one other living Player chosen by the Seer, discreetly convey the Core-computed Werewolf Faction Agent result to the Seer with a Continue acknowledgment, and acknowledge returning the Seer to sleep.
- The target and result remain Moderator-and-Seer-private. Recovery cannot repeat a committed check or lose a pending feedback step.

### Wild Child

- On Night 1, privately identify exactly one Wild Child, wake that Player, record exactly one other Player chosen as model, and acknowledge sleep.
- Model Elimination later changes Faction Beneficiary and Werewolf Faction Agent state automatically and privately while preserving the Wild Child Role. There is no Moderator choice or public transformation announcement.

### Villager-Villager

- No Role-specific Night or Day action, and no fake no-op confirmation, exists. Its two-sided physical card may prove the Role through an authorized reveal.
- **Open in #147:** which event authorizes proof while the Player is alive, whether it is immediately public, and whether generic reveal is sufficient.

### Two Sisters

- Night 1 publicly calls the Role without naming Players; the Moderator privately records the complete observed active-holder set using #147's settled dealt-zone cardinality and no-holder rule. The eligible group recognizes one another, public-table copy instructs sleep, and the Moderator submits a correlated Continue acknowledgment. Identities remain private.
- On Nights 3, 5, 7, and later odd Nights, the #147-settled eligible living-holder set receives a required interval: the Moderator submits Continue after communication, then a separate Continue after the sleep instruction. No choice, decline, content, or timer is recorded. An unavailable interval is omitted without erasing earlier recognition.
- **Open in #147:** the zero-, one-, and two-active-holder recognition and later-interval paths when a Sisters card can be undealt or acquired.

### Three Brothers

- Night 1 uses the same public call, private complete observed-holder response, recognition, sleep, and correlated Continue flow as Two Sisters, with the holder set derived from #147 rather than Role Composition count.
- On Nights 3, 5, 7, and later odd Nights, the #147-settled eligible living-holder set uses the same separate communication and sleep acknowledgments. No choice, decline, content, or timer is recorded.
- **Open in #147:** the zero-, one-, two-, and three-active-holder recognition and later-interval paths when a Brothers card can be undealt or acquired.

### Witch

- The first active call privately identifies and wakes the Witch. Any potion use records a choice physically made by the Witch Player; confirming a non-empty use commits and spends that potion, while a legal decline preserves it. The flow ends by returning the Witch to sleep.
- **Open in #147:** exact prompt order and breakdown; heal and poison candidate sets; self-poison and same-target legality; decline, unavailable, and no-target presentation; and whether a fully spent Witch is called.

### Hunter

- At the cascade's generic reveal step, every not-yet-public selected Player physically reveals before Elimination commits: a Moderator-known Hunter uses a Continue acknowledgment and an unknown Role uses a complete valid mapping. Once the Hunter is actually Eliminated and earlier forced heartbreak reactions finish, if the current Hunter power is available, record exactly one other living Player physically chosen by the Hunter for the mandatory final shot inside the same Elimination Cascade. Role Power Suppression prevents a later trigger.
- **Open in #147:** whether zero remaining legal targets uses a no-target acknowledgment or automatic completion.

### Stuttering Judge

- Night 1 privately identifies the Judge, instructs the Moderator and Player to establish—but never enter—the physical signal, and accepts a Continue acknowledgment.
- During an eligible first Vote, choose exactly one option: signal occurred or signal did not occur. Occurred commits the Consecutive Vote and spends the power; did not occur preserves it. The Vote target-or-tie uses the shared Vote flow.
- **Open in #147:** signal-question placement relative to Vote result and reveal, and only the presentation/completion path when an already committed Consecutive Vote has no viable surviving participants. The commitment itself is not reopened.

### Scapegoat

- A qualifying tie replaces the tied result with the Scapegoat Elimination, establishes the following Day's voting restriction, never changes who may be targeted, and does not affect a same-Day Consecutive Vote.
- **Open in #147:** exact identification and generic public-reveal sequence; restriction representation; Player-choice timing within the cascade; candidate roster and cardinality including empty; public announcement timing; and zero-survivor behavior.

### Wolf Hound

- Night 1 privately identifies and wakes exactly one holder, then records exactly one option—Villagers or Werewolves—and acknowledges sleep. No decline exists.
- The branch commits before the collective Werewolf call and remains app-private after a later Role Reveal. The app never automatically announces it, although the canonical physical rule permits the Moderator to disclose it verbally.

### Accursed Wolf-Father

- The Role participates in the collective group without becoming exact-role-known through that group observation. At the first individual call, privately identify the exact holder, wake that Player, and after the collective victim commits record exactly one private choice made by that Player: replace the fixed victim's collective physical Elimination with infection, or decline. No different Player target is accepted. A confirmed infection spends the resource immediately; spent state omits later infection choices. The Moderator then acknowledges sleep.

### Big Bad Wolf

- The Role participates in the collective group without becoming exact-role-known through that group observation. At the first individual call, privately identify the exact holder. While the extra attack remains available and a legal target exists, wake that Player, record exactly one living non-Agent target physically chosen by them other than the collective victim, and acknowledge sleep. The enabled attack cannot be declined.
- With no legal target, no Player selector is shown. **Open in #147:** the shared no-legal-target presentation.
- #129 owns the complete collective → Accursed → White → Big Bad Wolf integration order.

### Little Girl

- Night 1 privately identifies one holder. The collective Werewolf wake acknowledgment opens the spying interval and its sleep acknowledgment closes it.
- No separate target, peek, detection, success, or failure response exists. Suppression omits both reminders and never erases learned information.

### Defender

- Privately identify the holder on the first call. Each active Night, wake the Defender, record exactly one legal living Player physically chosen by that Player, and acknowledge sleep. Self is legal; Little Girl is not.
- A Player protected by this current Defender power on the immediately preceding Night is ineligible. A Night in which this power produces no protection—including suppression, unavailability, or no legal target—or acquisition of a fresh Defender power through Permanent Role Swap leaves no previous target to compare, so an older target is eligible at the next active call.
- **Open in #147:** whether zero legal targets is explicitly acknowledged or silently omitted under the shared zero-target ruling.

### Village Idiot

- The ordinary Vote selects the Player. If the Role is not already public, the Player completes generic public reveal: an already-known Role uses an acknowledgment and an unknown Role uses a complete valid mapping.
- Core automatically cancels the first qualifying Vote Elimination, spends the pardon, sets voting power to zero, and presents a public consequence acknowledgment. The Moderator cannot decline the pardon. Later Votes eliminate normally.

### White Werewolf

- Night 1 uses only the collective group flow. On absolute even Nights, wake the exact White Werewolf holder and record zero or one other legal living Werewolf Faction Agent physically chosen by that Player; empty declines. No legal target follows the no-target path, then sleep is acknowledged. Odd Nights have no solo instruction.
- **Open in #147:** when the exact holder is privately identified if Night 1 records only the mixed Agent group, and the shared zero-target presentation.

### Bear Tamer

- Privately identify the holder before the first Dawn evaluation. After a non-terminal Dawn Victory Check Window, Core automatically checks Living Neighbors.
- A true condition emits the public semantic growl and accepts a Continue acknowledgment; false, terminal, dead, or suppressed paths produce no Role instruction.
- **Open in #147:** whether the sound also requires rendered public wording beyond Moderator guidance.

### Fox

- Privately identify the holder on the first call. Each available Night, wake the Fox and record zero or one living center Player physically chosen by the Fox, including the Fox; empty declines.
- A performed check produces Core-computed feedback conveyed privately to the Fox with a Continue acknowledgment. A positive result preserves the power; a negative result permanently removes it. Decline preserves the power and produces no feedback. The flow then acknowledges sleep. **Open in #147:** before the first collective Group Observation, how the app obtains any unknown Agent facts needed for this result without guessing.

### Knight with the Rusty Sword

- The Role has no chosen Night input. A qualifying Dawn victim completes generic public reveal when not already public. After the cascade, Core automatically snapshots the first eligible surviving Agent clockwise; the Moderator never chooses that Player. The following Dawn automatically applies or expires the disease. Suppression prevents a new trigger but never cancels an already scheduled disease.
- **Open in #147:** whether Core privately shows the snapshotted Player and pauses, or schedules it silently, including the no-target presentation.

### Piper

- Privately identify the holder on the first call and wake that Player. Each Night, the Piper physically chooses exactly two distinct eligible targets when at least two exist, exactly one when one exists, and none when none exist; the Moderator records that exact count and later acknowledges sleep. Eligible means living, not the Piper, and not already Charmed. The action cannot be declined when a target exists.
- After Charmed state commits, wake all living Charmed Players for recognition and return them to sleep without recording content.
- **Open in #147:** the number and placement of recognition/sleep acknowledgments and the no-Charmed-Player presentation.

### Cupid

- Native Cupid's Night 1 call privately records the observed holder set using #147's active-card/no-holder ruling, then wakes the eligible holder. Cupid physically chooses exactly two distinct living Lovers and the Moderator records them; Cupid may be one. Wrong count, duplicates, dead, illegal, and stale Players are rejected. Actor-borrowed Cupid follows its borrowed-power contract instead of this native Night 1 restriction.
- The relationship commits once. The Moderator privately wakes or taps the selected Players, guides them to recognize one another and their mutual-vote constraint, acknowledges completion and sleep, and never exposes the pair in public text unless a later rule reveals it. Later heartbreak Elimination is automatic inside the same Elimination Cascade.
- **Open in #147:** how the app privately obtains any still-unknown Faction Beneficiary facts needed to classify the new relationship as same-Faction or Cross-Faction before committing it.

### Thief

- The Physical Deal, not the app, leaves two physical Character Card instances undealt. At the Night 1 call, privately identify and wake the Thief holder, record the exact two observed card instances, record the one instance physically chosen by that Player, and acknowledge sleep.
- The mandatory one-of-two choice commits a Permanent Role Swap once, preserves initial deal history, starts fresh power state, and continues only through calls that remain ahead. The headless simulator derives its own seeded deal and undealt instances.
- **Open in #147:** whether the Thief card or any selected Role may be undealt; what happens when no Player holds Thief; post-swap current Role and physical card zones; same-Role card-instance identity; and correction of an incorrectly recorded pair or choice.

### Angel

- Angel has no strategy choice. A qualifying Elimination and outcome predicate are automatic once the Role is legitimately known. If Angel does not win by the Dawn Victory Check Window resolving Night 2, Core atomically swaps the holder to Simple Villager and does not allow the Moderator to veto expiry.
- **Open in #147:** the latest safe private identification step for a surviving Angel; the physical Character Card action at expiry; how the new Simple Villager current Role is conveyed to the Player; whether it is public or private; and whether an informational acknowledgment is required.

### Devoted Servant

- After the Vote target is fixed and before its Character Card is publicly revealed, the Devoted Servant Player physically chooses decline or use and the Moderator records exactly one private option. A Lover, spent power, already revealed target, or stale response is invalid. Decline preserves the power, performs no Servant self-reveal, transfer, or swap, and resumes the voted target's ordinary generic reveal and Elimination Cascade.
- On use, one atomic commit spends the power, publicly reveals and discards the Servant's printed Character Card, transfers the target's physical Character Card to the Servant while keeping that card hidden, and separately gives the Servant the target's still-hidden current Role and default Faction Beneficiary with fresh power state. The voted target remains Eliminated and the cascade continues; only the acquired Role's first call waits until the next Night. Charmed, Sheriff, and Town Crier clear; infection and an in-force Scapegoat restriction remain attached to the Servant Player; the target's relationships and Status Effects are not inherited. Headless truth comes from the seeded assignment plus committed swaps. Decline preserves the power and causes none of those transitions.
- **Open in #147:** how the Moderator privately records an as-yet-unknown target Role before commit and how the new hidden identity is conveyed to the Servant.

### Elder

- Resistance to a qualifying attack and later Role Power Suppression are automatic rules, not Moderator choices. A private resistance notice may be acknowledged; a village-Vote Elimination completes generic public reveal when needed, drains the cascade, then publicly presents the suppression consequence before any Consecutive Vote.
- **Open in #147:** how an unknown attacked Elder is privately identified without publicly revealing the card, and the exact informational/no-input presentation of resistance and suppression.

### Prejudiced Manipulator

- When Prejudiced Manipulator is in Role Composition, before Lobby Exit the Moderator creates and publicly announces an exhaustive two-group partition, then records every Player in exactly one non-empty group through typed lobby configuration. Core validates but never generates or balances live groups. No Core Setup phase or Role choice exists.
- Before the first Victory Check Window that needs the hidden beneficiary, the Moderator privately records the observed active holder through the exact-Role flow selected by #147. Core never infers a holder from Role Composition or remaining cards. The predicate itself is automatic.
- Simulator profile defaults may create only synthetic simulation setup; a non-default partition belongs to Simulation Scenario identity.
- **Open in #147:** when the hidden holder becomes privately known before the first outcome evaluation, exact deterministic odd-player profile default, stored public labels or criterion text, and correction policy.

### Actor

- When Actor is in Role Composition, during physical setup the Moderator chooses and records exactly three distinct eligible face-up Actor Setup Cards outside the Role Composition through lobby configuration. The app validates and never generates the live inventory; no setup-phase Moderator Instruction is introduced.
- At Actor's first opening call, privately identify and wake the exact Actor holder. At every opening call, that Player chooses to skip or select one remaining card and the Moderator records zero or one stable card instance, then acknowledges sleep. Selection immediately spends the card and activates one fresh Borrowed Role Power until the next Actor call; Actor's Role and Faction Beneficiary do not change.
- Each borrowed power uses only the complete source Role Power contract with machine-stable acting Player, source Role, power/resource identity, legal and stale validation, commitment, recovery, and headless response semantics. Actor remains the acting Role: source-holder identification and source-card reveal never run, and identity-specific public copy is adapted to Actor. Issue #142 owns Seer, Cupid, Witch, Little Girl, Defender, Fox, and Stuttering Judge; #143 owns Hunter, Elder, Scapegoat, Village Idiot, Bear Tamer, and Knight; #144 proves all thirteen together and admits Actor.
- **Open in #147:** whether selecting a face-up source remains public or becomes private, the presentation of a suppressed Actor call, typed card-instance handling, and the exact deterministic three-card simulator default (including eligibility and fallback) when Actor Setup Cards are omitted from a Simulation Scenario.

## Foundation and final-gate flows

- Elimination Cascade foundation #112 introduces no standalone response. It orders generic private identification or public reveal and then yields the exact Role reaction described above.
- Faction foundation #120 introduces the shared private acquisition response only when a requesting rule needs a still-unknown Beneficiary or Agent fact. It names the exact requested Player/fact keys, commits a complete #147-settled response once, resumes the suspended rule, and otherwise leaves Faction state private unless a Role explicitly reveals it.
- Outcome foundation #127 introduces no Core Moderator Response after a final Game Session Outcome. The client may offer a local close action for the finished presentation; it is not a Continue response, is not persisted as gameplay state, and cannot change or replay the outcome. Rehydration shows the same finished outcome again.
- Program conformance #145 must execute a manifest covering every Role and setup row: semantic instruction kind, actor/source identity, public/private split, exact response cardinality, legal/illegal/stale and no-input paths, commitment, recovery without duplicate or leak, and deterministic headless handling. Any reachable unsupported instruction fails screening as Incomplete rather than receiving a guessed response.

## Open decision register

Issue [#147](https://github.com/bicheichane/Werewolves/issues/147) owns the unresolved choices marked above. Its canonical issue-body acceptance checklist is the decision inventory; affected implementation tickets carry a native blocker and repeat their exact open questions. The register includes:

- Villager-Villager proof timing and publicity.
- Witch step granularity and remaining target rules.
- Hunter's zero-target reaction.
- Stuttering Judge signal placement and no-survivor presentation for an already committed Consecutive Vote.
- Scapegoat identification/reveal, cascade timing, restriction representation and cardinality, and publicity.
- The shared zero-legal-target presentation.
- Bear Tamer wording, Knight consequence visibility, and Piper acknowledgment count.
- Exact White Werewolf identity timing.
- Acquisition of still-unknown Faction Beneficiary or Agent facts before Fox feedback, Lovers classification, or a Victory Check Window needs them; #147 owns the decision and #120 owns the runtime exchange once that decision is copied into its contract.
- Thief undealt eligibility, active-holder cardinality for every exact-Role call, and the no-holder path.
- Angel physical-card handling and private delivery; Elder resistance/suppression presentation; Prejudiced Manipulator hidden-holder timing, stored public labels/criterion, and odd-player simulator default; and Devoted Servant hidden-Role acquisition and private delivery.
- Thief undealt-holder, post-swap card-zone, same-Role card-instance, and pair/choice-correction edge cases.
- Actor selected-source visibility, suppressed-call presentation, typed card-instance handling, and the exact deterministic simulator-default three-card setup.
- The zero-Agent/empty Faction Agent Group Observation path and its wake, victim, sleep, recovery, and headless behavior.
- Correction of an incorrectly recorded setup fact, Role Identification, Faction Agent Group Observation, or Role Reveal.

Until #147 records one answer for an item, no affected ticket may replace the open marker with an implementation choice.
