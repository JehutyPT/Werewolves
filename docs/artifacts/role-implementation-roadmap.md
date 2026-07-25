# Werewolves role implementation roadmap

> - Analyzed code baseline: `506ed86f`
> - Program authority: [PRD #93](https://github.com/bicheichane/Werewolves/issues/93) and completed [Wayfinder map #95](https://github.com/bicheichane/Werewolves/issues/95)
> - Roadmap date: 19 July 2026
> - Thief card-model settlement incorporated: [#148](https://github.com/bicheichane/Werewolves/issues/148), 20 July 2026
> - Planning rule: implement and integrate one Role at a time. Parallel notes describe a hypothetical multi-team future, not this delivery plan.
> - Status: historical, non-authoritative planning snapshot through 20 July 2026. Use the [canonical game rules](../domain/game-rules.md) and [ADRs](../adr/) for current decisions.

## Decision summary

The roadmap contains all 30 known `MainRoleType` Roles:

| Treatment | Count |
|---|---:|
| Already supported baselines | 4 |
| Sequential implementation slots | 25 |
| Deferred for a New Moon-specific Event dependency | 1 |
| **Total** | **30** |

The recommended critical path is:

1. establish passive and first-night listener patterns;
2. make elimination, reveal, and repeat-vote flow reliable;
3. separate Faction Beneficiaries from Faction Agents and replace the exact-Role wolf action with a collective operation;
4. replace binary victory checks with named Victory Check Windows, structured outcomes, and Durable Voting Power;
5. add living topology and durable delayed effects;
6. add Status Effect and relationship lifecycles;
7. add current Role state, Physical Character Card zones, Permanent Role Swaps, global power availability, and borrowed powers.

The first Role remains **Villager-Villager**. **Actor** remains last because it composes powers implemented by earlier Roles. **Gypsy** is the only deferred Role: its Spiritualism workflow depends on New Moon-specific Event cards. Expansion provenance alone does not defer Defender, Elder, Scapegoat, Village Idiot, or Piper.

Every one of the 29 in-scope Roles, including the four supported baselines, must be explicitly admitted to the versioned simulator surface used for **Degenerate Simulation Scenario** screening, and its required Moderator Instructions must be executable by the headless driver well enough to complete screening runs. This screening admission is part of Role completion but is not automatic from app/catalog support. Probability estimation and broader simulator usefulness remain out of scope.

Thief adds a setup-wide constraint rather than only a Night 1 listener. For `P` Players, Role Lock-In must partition the `P + 2` Role Composition into a `P`-card Deal Pool containing exactly one Thief and two private, distinct non-Thief offer instances. Every Role reachable from the pool or either offer contributes its conditional setup requirements before safety screening, and the committed partition becomes part of Simulation Scenario identity. Only the Deal Pool is physically dealt.

Order numbers are implementation slots, not estimates or sprints. A foundational slot can contain several internal tracer tickets, but the Role is not admitted to `SupportedRoleCatalog` until its entire vertical slice is complete.

## Delivery contract for every slot

A Role is complete only when all applicable parts of the vertical slice are complete:

- canonical rule behavior exists in Core and uses the centrally audited hook/call order;
- consequential runtime mutations are log-backed; setup and derived snapshot state use explicit persisted fields; both survive the accepted stable-boundary recovery model;
- the Moderator can configure and operate the Role through Core-provided instructions and the thin client;
- Portuguese Moderator-facing copy is resource-backed;
- public-API Core integration tests cover the full instruction/response cycle, legal targets, consequences, interactions, and recovery;
- rendered client tests cover any new setup or instruction surface;
- the Role is admitted to the app catalog only after the behavior is complete;
- the Role is explicitly admitted to the versioned degenerate-screening Simulator Profile Role Set, with headless instruction/response coverage and any required profile/cache compatibility change; app support alone does not opt it in;
- when a Role is reachable through a Thief offer, its setup and screening evidence covers that path as well as ordinary Deal Pool ownership. Branch aggregation covers `Offer1`, `Offer2`, and legal `Decline`, blocks on any Degenerate result, passes only when every branch completes non-degenerate, and otherwise leaves failures or timeouts nonblocking as Could Not Evaluate.

Recovery does **not** promise exact mid-listener continuation. Under ADR-0002, a process death resumes the latest stable Main Phase boundary and replays uncommitted phase-tail work. Tests must prove that replay is coherent and that state from an earlier committed boundary is not spent twice. A different recovery promise would require an explicit ADR change before Role work relies on it.

## Entry gate — repair and preserve the shared execution seams

Complete this gate before adding stateful Role implementations:

1. Fix the missing `ShouldAdvanceState` assignment in the listener state-machine stage.
2. Make admission explicit: an admitted active Role must have a registered listener; a deliberately passive Role must be declared passive. Missing factories must not be silently mistaken for support.
3. Audit the centralized hook lists against the canonical first-night, later-night, Dawn, and Day order.
4. Reaffirm the product boundary that the Moderator supplies the final vote result or tie. The app provides authoritative guidance but does not model individual ballots.
5. Introduce a reusable power-execution context before Witch: acting Player, source Role/power, power-instance or resource identity, and one availability decision. Migrate Seer as the first tracer. Every current and future Villager Role Power—chosen, automatic, reactive, passive, recognition-based, or communication-based—must enter through that availability decision from Slot 4 onward rather than checking only `MainRole == X` inside one listener. The decision gates a new use or trigger without undoing prior results. The policy can allow everything until Elder activates Role Power Suppression, and Actor later uses the same context to borrow individual powers.
6. Keep one integration owner for `SupportedRoleCatalog`, `GameFlowManager`, shared enums/resources, serialization converters, and shared test builders.

## Strict sequential roadmap

### Stage 1 — low-risk patterns and the first reusable power

| Slot | Role | Why it is here | Required result before continuing |
|---:|---|---|---|
| 1 | **Villager-Villager** (`VillagerVillager`) | Smallest catalog addition and the passive-Role tracer. | Catalog, lobby, composition, reveal/display, and recovery work without creating a fake no-op listener. |
| 2 | **Two Sisters** (`TwoSisters`) | Establishes the shared first-night recognition pattern with a fixed group of two. | Both Players receive one Night 1 recognition wake-up. On Nights 3, 5, 7, and later odd Nights, the app schedules a required, non-skippable silent-communication interval when both living current Role holders qualify. |
| 3 | **Three Brothers** (`ThreeBrothers`) | Reuses the recognition pattern with a fixed group of three and exposes incorrect hard-coding to pairs. | The shared implementation is cardinality-driven, the lobby preserves the exact-three constraint, and the same odd-Night interval is scheduled separately when any two or three living current Role holders qualify. |
| 4 | **Witch** (`Witch`) | First bounded, stateful reusable power and the night-resolution baseline for later interactions. | None/save/poison/both are supported; each potion commits on confirmation and is once-only across nights; every physical Werewolf-attack target is shown and at most one can be healed; resolver precedence is explicit; full-Night recovery replay is tested. |

**Stage gate:** the supported catalog contains eight complete Roles, all eight are explicitly admitted for degenerate screening, listener registration is auditable, and the reusable power context is exercised by Seer and Witch without Actor-specific code.

### Stage 2 — vote instances and elimination cascades

Before Slot 5, replace the fixed two-pass death-hook assumption with a vote/resolution-scoped elimination work queue. The queue must expose ordered pre-reveal, reveal/assignment, elimination, and reaction seams; drain until no work remains; allow an eliminated Role such as Hunter to react; and distinguish one vote's eliminations from earlier Day eliminations.

| Slot | Role | Why it is here | Required result before continuing |
|---:|---|---|---|
| 5 | **Hunter** (`Hunter`) | The most direct tracer for the elimination queue. Implementing it before the Judge gives repeat voting a real interactive cascade to wait for. | Every actual Hunter Elimination, regardless of cause, produces exactly one final shot; concurrent Eliminations and forced heartbreak resolve in canonical order, the shot enters the same queue, and the complete cascade drains before navigation continues. |
| 6 | **Stuttering Judge** (`StutteringJudge`) | Consumes the completed vote-instance and cascade seams; its slice adds the Day Action command and post-cascade repeat edge in the centralized phase graph. | The Night 1 signal is recorded; a valid once-only Day Action commits a no-Debate second vote after the first cascade regardless of its outcome; no Victory Check Window occurs between the two votes. |
| 7 | **Scapegoat** (`Scapegoat`) | Completes the vote-control track once vote instances exist. | A tie is replaced by the Scapegoat elimination; the Moderator records the next-Day voter policy; it expires deterministically after exactly that Day and does not affect the Judge's same-Day second vote. |

**Stage gate:** two votes cannot reuse the first vote's elimination set, cascades always finish before repeat/outcome navigation, and a future Devoted Servant has a correctly ordered pre-reveal seam. Scapegoat eligibility is explicitly temporary guidance, not Durable Voting Power.

### Stage 3 — Faction semantics and the collective Werewolf operation

Before Slot 8, implement ADR-0010's separate Faction Beneficiary and Faction Agent concepts. Migrate Simple Werewolf group participation, Seer detection, Wild Child transformation, targeting predicates, and baseline win membership off the legacy binary `Team` assumptions. Move the collective victim decision out of `SimpleWerewolfRole`; the operation must discover all current Werewolf Faction Agents. Because these migrations change behavior for simulator-supported baselines, bump the relevant simulator profile/cache identity in this stage rather than waiting for new Role admission.

| Slot | Role | Why it is here | Required result before continuing |
|---:|---|---|---|
| 8 | **Wolf Hound** (`WolfHound`) | Smallest dynamic-membership tracer for the new Faction and collective-operation model. | The Night 1 choice applies the clarified Role-identity ruling, changes Beneficiary and Agent state as required, and joins the same-night wolf group when the Werewolf option is chosen. |
| 9 | **Accursed Wolf-Father** (`AccursedWolfFather`) | Establishes durable infection before Roles query the history of all Werewolf Agents. | Infection replaces the collective elimination, changes Agent but not Beneficiary, preserves the victim's prior powers, joins future collective actions, and is not blocked by Defender protection. |
| 10 | **Big Bad Wolf** (`BigBadWolf`) | Builds on dynamic Agent history from infection and transformation. | The extra victim is offered only while no qualifying non-temporary Werewolf Agent has been eliminated, uses Agent-based target eligibility, and resolves through the shared night contract. |
| 11 | **Little Girl** (`LittleGirl`) | Adds the interstitial spying interval only after the collective action has one owner. | Night 1 identification and the silent spying interval occur in canonical order without giving the Little Girl a modeled success/failure choice that the rules do not define. |
| 12 | **Defender** (`Defender`) | Comes after the real attack, infection, and Little Girl fixtures needed to prove its precedence rules. | Only actual protection by the same current Defender power on the immediately preceding Night makes that Player ineligible. A Night without protection or a fresh power instance resets the sequence. Little Girl is ineligible; protection blocks applicable physical wolf attacks but not infection, poison, Hunter, charm, or other stated exceptions. |

**Stage gate:** all operational “Werewolf” queries mean current Faction Agent; the collective group is not owned by an exact Role; same-night dynamic participation works; and the resolver has a tested interaction matrix rather than precedence hidden in branch order.

### Stage 4 — structured outcomes and Durable Voting Power

Before Slot 13, implement ADR-0011 as a named Game Session Outcome model: explicit Victory Check Windows after resolved Dawn and Day cascades, simultaneous Faction predicates, Shared Victory, No-Winner, and the Werewolf Control Shortcut based on Durable Voting Power. Faction Beneficiary state from Stage 3 is a hard prerequisite. Propagate the structured result through finished instructions and headless execution, then bump simulator profile/cache compatibility because baseline terminal semantics changed.

| Slot | Role | Why it is here | Required result before continuing |
|---:|---|---|---|
| 13 | **Village Idiot** (`VillageIdiot`) | Smallest validation Role for Durable Voting Power and the rewritten outcome engine. | The Day vote is cancelled, the Idiot survives and is revealed, permanent voting power becomes zero, and the Werewolf Control Shortcut observes that durable change. |
| 14 | **White Werewolf** (`WhiteWerewolf`) | Combines the completed collective operation with distinct Agent/Beneficiary state and a solo predicate. | It joins the wolf group, retains the White Werewolf Beneficiary, takes its solo action on Nights 2, 4, 6, and later even Nights, targets another Agent, and can participate in a Shared Victory Outcome. |

**Stage gate:** outcome checks never occur mid-cascade or between Judge votes; all predicates inspect the same resolved state; and the ending Victory Check Window is observable in deterministic Core and simulator evidence.

### Stage 5 — living topology and delayed consequences

| Slot | Role | Why it is here | Required result before continuing |
|---:|---|---|---|
| 15 | **Bear Tamer** (`BearTamer`) | Smallest tracer for canonical living-neighbor queries, but it must wait for named Dawn outcome ordering. | Immediate living neighbors are derived from circular Seating Order after eliminations; the growl uses current Agent state and occurs only after a non-terminal Dawn Victory Check Window. |
| 16 | **Fox** (`Fox`) | Reuses topology while exercising the shared power context and durable power loss. | The duplicate-free target-plus-living-neighbors set is evaluated for current Agents, feedback is immediate, declining is distinguished from a negative result, and a negative result disables the power permanently. |
| 17 | **Knight with the Rusty Sword** (`KnightWithRustySword`) | Adds directional traversal and the first durable delayed consequence after topology is stable. | The target is snapshotted after the triggering Dawn cascade by the clarified clockwise eligibility scan, the disease is persisted independently of listener state, and it resolves at the following Dawn exactly once without retargeting. |

**Stage gate:** topology handles eliminated seats and small living circles consistently; directional traversal is canonical; delayed consequences survive stable-boundary recovery without encoding live listener state.

### Stage 6 — Status Effect and relationship lifecycles

| Slot | Role | Why it is here | Required result before continuing |
|---:|---|---|---|
| 18 | **Piper** (`Piper`) | Establishes durable Status Effect application/notification and expiry-ready lifecycle behavior before Lovers and swaps need it. | Living non-Piper Players who are not already Charmed are eligible; the Piper must charm two when available, the sole target when only one exists, and none when zero exist. The Moderator can identify and notify all Charmed Players, charm is not shared through Lovers, and the Piper predicate evaluates the resolved state alongside simultaneous outcomes. |
| 19 | **Cupid** (`Cupid`) | Builds relationships on the completed outcome and elimination-queue foundations. | Lover identity is durable and symmetric; mutual-vote guidance is available; heartbreak enters the elimination queue; same-Faction and Cross-Faction cases are distinct; beneficiary precedence and the last-two-alive predicate are explicit. |

**Stage gate:** Status Effects and relationships have data-bearing state, application/removal/reset semantics, notification behavior, and outcome integration. They are not encoded as extra Roles.

### Stage 7 — current Role, physical card zones, setup artifacts, and power composition

Before Slot 20, separate Role Composition, Deal Pool, Thief Offer Cards, set-aside cards, Player-owned physical cards, and current Role. Role Lock-In must commit the Player-count Deal Pool with exactly one Thief plus two private, distinct non-Thief offer instances before any Physical Deal, then derive conditional setup from every Role in the pool or offers. A Permanent Role Swap must atomically change current Role, default Beneficiary when allowed, listener activation, and fresh power state while separately applying explicit physical-card, visibility, relationship, and Status Effect rules.

| Slot | Role | Why it is here | Required result before continuing |
|---:|---|---|---|
| 20 | **Thief** (`Thief`) | Canonical first tracer for Role Lock-In, physical card zones, current Role, and Permanent Role Swap. | For `P` Players, commit a `P + 2` Role Composition as a `P`-card Deal Pool containing exactly one Thief plus private `Offer1` and `Offer2` instances that are distinct and non-Thief; require setup for every Role in the pool or either offer; include the partition in scenario identity; screen each semantically distinct legal `Offer1`, `Offer2`, and `Decline` branch; and deal only the pool. Night 1 accepts one machine-stable legal choice. An exchange makes the selected offer Player-owned and moves the original Thief card plus the unchosen offer to Set-Aside; `Decline` retains Thief and moves both offers to Set-Aside. The resulting current Role activates immediately and follows the remaining Night 1 call order. Successful response processing creates ADR-0017's stable checkpoint before returning, so recovery cannot recommit a choice, decline, or exchange. Any Degenerate branch blocks, while failures and timeouts without one do not. |
| 21 | **Angel** (`Angel`) | Bounded second swap consumer and a precise timed-outcome lifecycle test. | Eligible early elimination wins at the next named window; eligibility expires after the Dawn window resolving Night 2; otherwise the Role atomically swaps to Simple Villager. |
| 22 | **Devoted Servant** (`DevotedServant`) | Uses the pre-reveal seam from Stage 2 and the card-zone, infection, charm, and Lover semantics now in place. | On Use, the Servant's printed card becomes public and is discarded while the voted target's card and acquired current Role remain hidden; Decline resumes the target's ordinary reveal with no Servant transition. Lover use is prohibited; Charmed, Sheriff, and Town Crier clear while Player-attached infection and any Scapegoat voting restriction survive; target state is not inherited; the acquired power state is fresh and its first call is the next Night. |
| 23 | **Elder** (`Elder`) | Establishes one global power-availability decision only after attack, swap, and power execution paths exist. | First qualifying wolf attack/infection resistance is correct; other causes bypass it; after the triggering Vote's Elimination Cascade, every current and future Villager Role Power is suppressed through the shared availability seam without undoing prior commitments or results. |
| 24 | **Prejudiced Manipulator** (`PrejudicedManipulator`) | Dedicated setup-artifact and client slice after session/setup persistence has a stable extension point. | The immutable Public Group Partition is validated, persisted, displayed, and included in scenario identity; the independent outcome predicate requires a living beneficiary and evaluates that Player's Opposing Public Group. |
| 25 | **Actor** (`Actor`) | Last because it composes the widest already-tested power surface and must honor Elder suppression. | Three eligible Actor Setup Cards persist; Actor may skip or spend one at the opening Actor call each Night, activating the complete source power through the next Actor call while its action or reaction follows the source's relative boundary. Native Night 1 setup powers can begin on any selected Night, durable results survive expiry, and Role Power Suppression blocks new selections and later uses or triggers without refunding the card or undoing prior results. |

**Final gate:** all 29 non-New-Moon-event-dependent Roles are app-supported and explicitly admitted to the versioned Safety-Screening capability; all 25 new Roles have public-API behavior, recovery evidence, and headless screening-path evidence. Full-Probability admission, build-time enumeration, bundled-cache coverage, and probability output remain separate.

## Hypothetical parallelization map

The current architecture has no safe “merge 25 independent Role branches” path. Every Role eventually collides in the catalog, central ordering, resources, converters, or shared test infrastructure. The groups below become plausible only after their prerequisite APIs are frozen and one integrator retains ownership of those hot files.

| Parallel window | Roles that could be developed concurrently | Conditions and cautions |
|---|---|---|
| A — passive and first-night work | Villager-Villager; a single combined Two Sisters/Three Brothers package; Witch | The admission and power-execution contracts must be fixed first. Sisters and Brothers should normally be one work package because splitting the same recognition helper gains little. |
| B — after vote/elimination foundations | Hunter; Scapegoat | Their Role-specific logic can diverge once the queue and vote-policy APIs are frozen. Stuttering Judge should remain with the central navigation owner because it consumes the repeat edge directly. |
| C — after Stage 3 faction/group APIs are frozen | Accursed Wolf-Father; Big Bad Wolf; Little Girl | Their Role modules can proceed separately, but resolver and hook-order integration must be serialized. Defender may be scaffolded in parallel, but it cannot be accepted until the infection and Little Girl fixtures are integrated and its interaction matrix passes. |
| D — after outcome and topology contracts | Bear Tamer; Fox; White Werewolf | Their adapters use distinct topology, power, and solo-outcome seams. Knight remains a separate integration slice until the delayed-effect scheduler is stable. |
| E — after outcome, lifecycle, and ownership contracts | Angel; Piper; Prejudiced Manipulator | These are independent outcome/lifecycle adapters once each extra setup or swap prerequisite is stable. Their outcome registration and serialization integration still have one owner. |
| Cross-lane opportunity | Thief can overlap the topology stage; topology Roles can overlap outcome adapters | This is only worthwhile with separate ownership of state/schema versus topology modules. Shared DTO and converter edits remain serialized. |

The strongest later parallel group, after **all** shared contracts are stable, is Bear Tamer, Fox, Big Bad Wolf, Accursed Wolf-Father, Little Girl, Defender, White Werewolf, Angel, Piper, and Prejudiced Manipulator. Their catalog registration, centralized order audit, resource merge, degenerate-screening profile admission, and cross-Role interaction matrix would still be integrated sequentially.

Roles that should remain serial even with multiple teams:

- Hunter → Stuttering Judge, because repeat voting consumes the completed elimination/cascade contract;
- Wolf Hound before the other new dynamic wolf Roles, because it is the first admission that validates the already-migrated Faction/group contract;
- Village Idiot before other new outcome predicates, because it proves Durable Voting Power and the ending model;
- Cupid before Devoted Servant, Thief before Angel and Devoted Servant, and Elder before Actor; Devoted Servant and Elder have different prerequisites even though their shared state/schema integration remains serialized;
- Knight until the delayed-effect mechanism has one accepted owner.

## Resolved Wayfinder domain decisions

Wayfinder map #95 is complete: no rules decision owned by that map remains open. The canonical rulings below govern implementation and the linked tickets preserve the human decision record. Role-specific Moderator interaction-shape questions discovered during ticket preparation remain explicitly open in #147.

| Slot(s) | Settled contract | Canonical rule and decision record |
|---|---|---|
| 2–3 | Night 1 is recognition-only. The app schedules separate, required, non-skippable communication intervals on Nights 3, 5, 7, and later odd Nights for qualifying living current Role holders, after Fox and before Defender. | [Sisters and Brothers Communication](../domain/game-rules-clarifications.md#sisters-and-brothers-communication); [#96](https://github.com/bicheichane/Werewolves/issues/96) |
| 4, 9–12, 23 | One-use resources commit on confirmation. Physical Werewolf attacks share the settled Defender/Elder/Witch resolution rules; infection is not Defender-protected; Little Girl spying ends with the collective wake interval. | [Night Action Resolution](../domain/game-rules-clarifications.md#night-action-resolution); [#98](https://github.com/bicheichane/Werewolves/issues/98) |
| 5, 19 | Every actual Hunter Elimination triggers one final shot. Concurrent Eliminations apply first, forced heartbreak precedes the shot, and the full cascade drains before a Victory Check Window. | [Elimination Cascades](../domain/game-rules-clarifications.md#elimination-cascades); [#106](https://github.com/bicheichane/Werewolves/issues/106) |
| 6–7, 13, 21 | A valid Judge signal commits a no-Debate Consecutive Vote after the first cascade regardless of its outcome, with no intervening Victory Check Window. Only the first Vote of Day 1 can qualify Angel. | [Consecutive Day Votes](../domain/game-rules-clarifications.md#consecutive-day-votes); [#103](https://github.com/bicheichane/Werewolves/issues/103) |
| 8 | Wolf Hound keeps its Role and Character Card; its private Night 1 branch changes Beneficiary/Agent state and can grant same-Night collective participation. | [Wolf Hound Identity](../domain/game-rules-clarifications.md#wolf-hound-identity); [#100](https://github.com/bicheichane/Werewolves/issues/100) |
| 14 | White Werewolf's solo action follows the absolute even-Night schedule: Nights 2, 4, 6, and later even Nights. | [White Werewolf Solo-Action Cadence](../domain/game-rules-clarifications.md#white-werewolf-solo-action-cadence); [#105](https://github.com/bicheichane/Werewolves/issues/105) |
| 16 | Fox checks the duplicate-free center-plus-living-neighbors set; a decline gives no result or power loss, while a performed negative check removes the power. | [Fox Checks](../domain/game-rules-clarifications.md#fox-checks); [#101](https://github.com/bicheichane/Werewolves/issues/101) |
| 17 | Rusty Sword selects after the triggering Dawn cascade by clockwise scan from the Knight's fixed seat, snapshots the eligible Agent, and never retargets. | [Rusty Sword Disease](../domain/game-rules-clarifications.md#rusty-sword-disease); [#104](https://github.com/bicheichane/Werewolves/issues/104) |
| 18 | Piper must charm two eligible Players, the sole eligible Player, or none when zero are eligible; the resolved state feeds the next Victory Check Window. | [Piper Charm Targets and Outcome](../domain/game-rules-clarifications.md#piper-charm-targets-and-outcome); [#102](https://github.com/bicheichane/Werewolves/issues/102) |
| 20 | At Role Lock-In, a Thief-enabled Role Composition is partitioned into a Player-count Deal Pool containing exactly one Thief and two private, distinct non-Thief offer instances. Every reachable Role's setup is complete before branchwise screening; only the pool is dealt. `Offer1`, `Offer2`, or legal `Decline` commits once; an exchange sets aside the original Thief card and unchosen offer, changes current Role immediately, and continues forward without replaying setup or earlier calls. Any Degenerate branch blocks, while failures and timeouts without one do not. | [Thief Setup, Choice, and Acquired-Role Timing](../domain/game-rules-clarifications.md#thief-setup-choice-and-acquired-role-timing); [#99](https://github.com/bicheichane/Werewolves/issues/99); [#148](https://github.com/bicheichane/Werewolves/issues/148) |
| 22 | Devoted Servant acquires a fresh hidden Role for the next Night; old-identity effects clear, while Player-attached infection and an in-force Scapegoat restriction survive. | [Devoted Servant Swap Boundary](../domain/game-rules-clarifications.md#devoted-servant-swap-boundary); [#109](https://github.com/bicheichane/Werewolves/issues/109) |
| 23 | Villager Role Powers include chosen, automatic, reactive, passive, recognition-based, and communication-based capabilities. Elder suppression starts after its Vote cascade and blocks new effects without rewriting prior results. | [Role Powers and New Moon Assignments](../domain/game-rules-clarifications.md#role-powers-and-new-moon-assignments); [#108](https://github.com/bicheichane/Werewolves/issues/108) |
| 24 | Prejudiced Manipulator uses one immutable, public, non-empty two-group partition; victory requires a living current beneficiary and no living Player in that holder's opposing group. | [Prejudiced Manipulator Public Groups and Outcome](../domain/game-rules-clarifications.md#prejudiced-manipulator-public-groups-and-outcome); [#107](https://github.com/bicheichane/Werewolves/issues/107) |
| 25 | Actor may skip or spend one card at its opening Night call; the complete fresh borrowed power follows its source boundary through the next Actor call, with no rewind or refund. | [Actor Borrowed-Power Timing](../domain/game-rules-clarifications.md#actor-borrowed-power-timing); [#97](https://github.com/bicheichane/Werewolves/issues/97) |

## Complete known-Role ledger

### Supported baselines — not new implementation slots

Each baseline migration must retain app behavior and explicit admission to the versioned degenerate-screening profile; semantic changes require fresh profile/cache compatibility evidence.

| Role | Roadmap treatment |
|---|---|
| **Simple Werewolf** (`SimpleWerewolf`) | Remains supported; migrate group action ownership and targeting to the Stage 3 collective operation. |
| **Simple Villager** (`SimpleVillager`) | Remains the passive baseline and the target of Angel's expiry swap. |
| **Seer** (`Seer`) | Remains supported; migrate to the shared power context and current Faction Agent detection. |
| **Wild Child** (`WildChild`) | Remains supported; preserve Role identity while changing Beneficiary/Agent state and dynamic wolf participation. |

### Sequential candidates — 25

Villager-Villager; Two Sisters; Three Brothers; Witch; Hunter; Stuttering Judge; Scapegoat; Wolf Hound; Accursed Wolf-Father; Big Bad Wolf; Little Girl; Defender; Village Idiot; White Werewolf; Bear Tamer; Fox; Knight with the Rusty Sword; Piper; Cupid; Thief; Angel; Devoted Servant; Elder; Prejudiced Manipulator; Actor.

### Deferred New Moon Event dependency — 1

| Role | Reason |
|---|---|
| **Gypsy** (`Gypsy`) | Spiritualism requires New Moon-specific Event cards, a constrained question deck, a temporary Medium designation, and a cross-day Event resolution flow. It belongs on a future Event-system roadmap. |

## Adversarial review changes incorporated

- Moved Defender behind Faction Agent/Beneficiary, the collective Werewolf operation, infection, and Little Girl fixtures.
- Put Hunter before Stuttering Judge so the elimination queue has a real tracer before repeat voting consumes it.
- Replaced fixed death-hook passes with a roadmap requirement for vote-scoped elimination work that drains to completion and includes a future pre-reveal seam.
- Put Faction semantics before structured outcomes and Durable Voting Power.
- Put Bear Tamer after named Victory Check Windows so a terminal Dawn outcome precedes the growl.
- Added a reusable acting-Player/source-power/availability seam before Witch so Actor can remain last without forcing Witch, Defender, and Fox rewrites.
- Corrected recovery expectations to whole-phase-tail replay from the last stable boundary.
- Required explicit degenerate-screening admission for all 29 in-scope Roles, plus profile/cache compatibility changes when Faction and outcome semantics change.
- Replaced the Thief's chance-determined undealt leftovers with #148's pre-deal Role Lock-In partition, conditional setup across the pool and offers, branchwise screening, and explicit exchange/set-aside zones.
- Kept central hook order, phase navigation, catalog admission, and schema integration serialized even where Role modules could hypothetically be developed in parallel.

## Evidence sources

- [Program PRD #93](https://github.com/bicheichane/Werewolves/issues/93)
- [Completed Wayfinder map #95](https://github.com/bicheichane/Werewolves/issues/95)
- [Thief physical-deal and Permanent Role Swap decision #148](https://github.com/bicheichane/Werewolves/issues/148)
- [Role invasiveness assessment](./role-implementation-invasiveness-report.md)
- [Canonical game rules](../domain/game-rules.md)
- [Game-rule clarifications](../domain/game-rules-clarifications.md)
- [Domain invariants](../domain/invariants.md)
- [Faction model ADR](../adr/0010-faction-model-separates-beneficiaries-from-agents.md)
- [Victory Check Window ADR](../adr/0011-victory-check-windows-are-resolution-boundaries.md)
- [Recovery ADR](../adr/0002-transient-state-not-serialized.md)
- [Hook-ordering ADR](../adr/0003-hook-listener-ordering-stays-centralized.md)
- [Phase-navigation ADR](../adr/0004-phase-navigation-stays-centralized.md)
- [Headless simulator/profile-admission ADR](../adr/0005-simulator-reuses-engine-via-headless-driver.md)
- [Safety-screening product-boundary ADR](../adr/0013-production-lobby-evaluation-stops-after-safety-screening.md)
- [Core architecture](../../Werewolves.Core/docs/architecture.md)
- [Client architecture](../../Werewolves.Client/docs/architecture.md)
- [QA strategy](../agents/qa-strategy.md)
- `Werewolves.Core/Werewolves.Core.StateModels/Enums/MainRoleType.cs`
- `Werewolves.Core/Werewolves.Core.GameLogic/Roles/SupportedRoleCatalog.cs`
- `Werewolves.Core/Werewolves.Core.GameLogic/Services/GameFlowManager.cs`
- `Werewolves.Core/Werewolves.Core.GameLogic/Services/NightInteractionResolver.cs`
- `Werewolves.Core/Werewolves.Core.GameLogic/Queries/GameSessionQueries.cs`
