# Werewolves role implementation invasiveness

> - Original evidence anchor: `3cb40a64e3d7`
> - Code-status refresh: `506ed86feaa4`
> - Assessment completed: 17 July 2026; planning status audited: 19 July 2026
> - Thief card-model settlement incorporated: [#148](https://github.com/bicheichane/Werewolves/issues/148), 20 July 2026
> - Categories describe architectural reach against the refreshed code baseline, not hours, points, or lines of code.
> - Status: historical, non-authoritative planning snapshot through 20 July 2026. Use the [canonical game rules](../domain/game-rules.md) and [ADRs](../adr/) for current decisions.

## Executive summary

All 30 documented `MainRoleType` values are included.

| Treatment | Count |
|---|---:|
| Supported baselines | 4 |
| Ranked implementation candidates | 25 |
| Excluded for a New Moon event dependency | 1 |
| **Total known roles** | **30** |

Candidate distribution:

| Refreshed-baseline category | Count |
|---|---:|
| Localized / low | 3 |
| Contained / moderate | 1 |
| Cross-cutting / high | 4 |
| Foundational / epic | 17 |

The smallest next implementation is **Villager-Villager**. The best tracer for improving shared engine behavior is **Stuttering Judge**.

This report is completed investigation evidence for [PRD #93](https://github.com/bicheichane/Werewolves/issues/93). Its role counts and invasiveness findings remain current at the refreshed baseline. PRD #93's published Role-owned foundation sequence supersedes the original work-order recommendations preserved below; those recommendations are not the implementation queue.

## Scope rule for New Moon

For this report, a **New Moon role** means a role whose implementation depends on New Moon-specific events. Historical expansion origin is not an exclusion criterion.

The canonical [game rules](../domain/game-rules.md) put only **Gypsy** under “Roles Specific to New Moon (Require New Moon Events).” Gypsy therefore remains visible in the inventory but is excluded from the ranked roadmap. Defender, Elder, Scapegoat, Village Idiot, and Piper are ranked normally because their mechanics do not depend on New Moon events.

## What the categories mean

- **Supported baseline:** already admitted by the current catalog. Notes identify compatibility debt, not missing basic support.
- **Localized / low:** fits an existing narrow seam, usually passive behavior or first-night recognition.
- **Contained / moderate:** needs a bounded stateful workflow, but no new foundational domain model.
- **Cross-cutting / high:** crosses timing plus another shared system such as elimination, voting, recovery, or interaction precedence.
- **Foundational / epic:** cannot be faithful until a reusable missing concept is implemented, such as faction semantics, outcomes, topology, durable voting power, active role ownership, or power availability.
- **Residual category:** likely role-specific reach after the named shared prerequisites exist.

No role currently occupies a separate Architectural / very-high tier. Knight with the Rusty Sword has an architectural residual after its faction and topology prerequisites land.

## Original recommended next work (superseded by PRD #93)

The following was the report's invasiveness-derived recommendation. Use PRD #93's Role-owned foundation sequence as the execution authority wherever its ordering differs.

1. Implement **Villager-Villager**.
2. Batch **Two Sisters** and **Three Brothers** using the same first-night recognition pattern.
3. Repair shared flow seams: assign `ShouldAdvanceState`, add the day-action command, and add a post-cascade repeat-vote edge.
4. Implement **Witch**, then use **Stuttering Judge** as the cross-cutting tracer.
5. Add **Hunter**, **Defender**, and then **Scapegoat** on their respective elimination, night-resolution, and vote-control tracks.
6. Build the shared foundations before taking on the epic roles. Implement **Actor last** within the role-power composition work.

## Complete role assessment

### Supported baseline — 4 roles

| Role | Current assessment |
|---|---|
| **Simple Werewolf** (`SimpleWerewolf`) | Group selection, logging, dawn resolution, persistence, and tests exist. Debt: the group action is owned by this exact role rather than by all current Werewolf Faction Agents. |
| **Simple Villager** (`SimpleVillager`) | Complete passive baseline. It needs no runtime power listener. |
| **Seer** (`Seer`) | Identification, selection, feedback, and persistence exist. Debt: detection reads legacy binary `Team` rather than current Werewolf Faction Agent status. |
| **Wild Child** (`WildChild`) | Model selection and transformation are implemented. Debt: transformation replaces the role with `SimpleWerewolf` instead of preserving role identity while changing Agent and Beneficiary state. |

### Localized / low — 3 candidates

| Role | Why it belongs here |
|---|---|
| **Villager-Villager** (`VillagerVillager`) | Its distinction is physical proof of innocence, not an app action. Work is catalog admission, composition/display behavior, reveal handling, and tests. |
| **Two Sisters** (`TwoSisters`) | Requires a first-night two-player recognition wakeup plus required odd-Night communication intervals while both living current Role holders qualify. The identity-only listener and fixed-count lobby behavior remain narrow seams. |
| **Three Brothers** (`ThreeBrothers`) | Reuses the recognition and odd-Night communication pattern with a required group of three and current-holder cardinality rather than pair-specific assumptions. |

### Contained / moderate — 1 candidate

| Role | Why it belongs here | Main work |
|---|---|---|
| **Witch** (`Witch`) | A bounded optional night workflow with two once-only resources. Existing save/kill action and resolver vocabulary already cover the consequences. | Show every physical Werewolf-attack target, support optional save/poison choices, commit potion use durably on confirmation, and test the settled interaction precedence. |

### Cross-cutting / high — 4 candidates

| Role | Why it belongs here | Residual after shared seams |
|---|---|---|
| **Hunter** (`Hunter`) | Every actual Elimination, regardless of cause, must produce exactly one final-shot choice within the same draining Elimination Cascade, and a committed shot must not replay twice after recovery. The repeated elimination hook and `HunterShot` reason already exist. | Contained / moderate after a reusable reactive-elimination protocol. |
| **Stuttering Judge** (`StutteringJudge`) | The once-only signal must survive recovery, allow the first vote’s reveal and death cascade to finish, then follow a new post-cascade edge into a second vote. | Contained / moderate after the day command and repeat-vote edge. |
| **Defender** (`Defender`) | Target choice is straightforward, but the previous-target restriction and protection precedence span nights and interact with Little Girl, ordinary wolf attacks, and infection. The current resolver incorrectly lets protection block Wolf-Father infection. | Contained / moderate after collective-victim semantics and resolver precedence are reusable. |
| **Scapegoat** (`Scapegoat`) | A tie must be replaced by the Scapegoat’s elimination, followed by a persisted voter allow/deny set shown to the moderator for exactly the next day and then expired. | Contained / moderate after reusable reactive vote-result and temporary-eligibility behavior. |

### Foundational / epic — 17 candidates

| Role | Why it is foundational at the refreshed baseline | Residual after prerequisites |
|---|---|---|
| **Little Girl** (`LittleGirl`) | Needs first-night identification plus a silent spying interval coordinated inside the collective Werewolf action, which does not yet exist as a shared operation. | Cross-cutting / high. |
| **Bear Tamer** (`BearTamer`) | Requires current Faction Agent queries and immediate living-neighbor topology after dawn eliminations. | Cross-cutting / high. |
| **Fox** (`Fox`) | Requires living-neighbor topology, current Agent detection, immediate private feedback, and durable permanent power loss. | Cross-cutting / high. |
| **Knight with the Rusty Sword** (`KnightWithRustySword`) | Requires Agent semantics and directional living-seat traversal before its delayed next-night consequence can be represented correctly. | Architectural / very high because the delayed-effect mechanism still remains. |
| **Big Bad Wolf** (`BigBadWolf`) | Must participate in a collective wolf operation and retain a second kill only while no qualifying non-temporary Agent has died. | Contained / moderate. |
| **Accursed Wolf-Father** (`AccursedWolfFather`) | Infection changes operational Agent status without changing the victim’s Beneficiary, then joins future collective wolf actions. | Contained / moderate. |
| **White Werewolf** (`WhiteWerewolf`) | Is both a Werewolf Agent and a distinct solo Beneficiary, acts every other night, targets other Agents, and needs a solo outcome predicate. | Cross-cutting / high. |
| **Cupid** (`Cupid`) | Lovers need durable symmetric relationship identity, heartbreak cascades, moderator-facing vote guidance, cross-faction Beneficiary precedence, and shared outcome handling. | Cross-cutting / high. |
| **Actor** (`Actor`) | Actor Setup Cards are validated but dropped before runtime, and #148 requires that setup even when Actor appears only as a Thief offer. The engine also lacks a reusable way to execute another Role's power without transferring Role identity. | Cross-cutting / high; client setup work remains material. |
| **Thief** (`Thief`) | Physical composition, deal zones, and active Role ownership are conflated. The app needs a pre-deal Role Lock-In partition for a Player-count Deal Pool containing exactly one Thief plus two private, distinct non-Thief offer instances; conditional setup across the pool and offers; branchwise safety and scenario identity; machine-stable `Offer1`, `Offer2`, and legal `Decline` responses; and durable exchange/set-aside zones with an immediate Permanent Role Swap. | Cross-cutting / high after the shared ownership foundation because lobby, simulator/cache, client instruction, recovery, and Core state must integrate. |
| **Devoted Servant** (`DevotedServant`) | Needs a pre-reveal interruption, permanent role swap, selective status/relationship clearing, fresh ability state, and faction reset. | Cross-cutting / high. |
| **Wolf Hound** (`WolfHound`) | The first-night choice is simple, but its durable consequences require separate Agent/Beneficiary state and dynamic participation in the wolf group. | Localized / low. |
| **Angel** (`Angel`) | Needs a transient faction, exact Victory Check Window eligibility, solo/shared outcome handling, expiry, and permanent transformation to Simple Villager. | Contained / moderate. |
| **Prejudiced Manipulator** (`PrejudicedManipulator`) | Needs a persisted pregame Player partition carried through lobby, session, recovery, dashboard, scenario identity, and a solo outcome predicate; #148 requires it even when the Role appears only as a Thief offer. | Contained / moderate. |
| **Elder** (`Elder`) | First-hit resistance is partly scaffolded. A village-vote death must disable every current and future Villager power, including swapped roles and Actor-borrowed powers, through one reusable power-availability model. | Contained / moderate. |
| **Village Idiot** (`VillageIdiot`) | Vote cancellation and survival are largely implemented, but permanent zero Durable Voting Power must affect the Werewolf Control Shortcut and outcome evaluation. The app should guide the moderator rather than model individual ballots. | Localized / low. |
| **Piper** (`Piper`) | Night selection and `Charmed` storage are scaffolded, but the role needs a durable Charmed lifecycle plus a Piper Faction predicate evaluated alongside simultaneous outcomes. | Contained / moderate. |

### Excluded New Moon event dependency — 1 role

| Role | Why excluded | Indicative reach if later included |
|---|---|---|
| **Gypsy** (`Gypsy`) | Spiritualism requires New Moon Event cards, a supporting event deck, constrained question data, the temporary Medium designation, and a cross-day resolution flow. | Foundational / epic. |

## Shared foundations and blockers

### Faction Agent and Beneficiary

Current player state still reduces allegiance to binary `Team`. The engine needs separate, time-aware Agent and Beneficiary concepts before infection, transformed roles, mixed-allegiance roles, and solo factions can be correct.

### Collective Werewolf operation

The group attack is currently owned by `SimpleWerewolfRole`. A shared operation must discover all current Werewolf Faction Agents, coordinate one decision, and support role-specific follow-up actions.

### Game Session Outcome and Durable Voting Power

Victory currently uses binary team parity. The replacement needs named Victory Check Windows, multiple simultaneous faction predicates, shared/no-winner outcomes, and Durable Voting Power for the Werewolf Control Shortcut. Village Idiot is the smallest validation role once this exists.

### Living topology

Seating is stored but is not exposed as a canonical query for immediate living neighbors or directional traversal. Bear Tamer, Fox, and Knight depend on this.

### Active role ownership, swaps, and power availability

Physical cards are conflated with active role ownership. Thief first needs explicit Role Composition, Deal Pool, Thief Offer Card, Player-owned, and set-aside zones plus a Role Lock-In boundary that precedes the Physical Deal. Thief, Devoted Servant, Angel, Actor, and Elder then need durable swaps, ability freshness, listener activation, and a shared power-availability decision.

### Relationships and status lifecycle

Status handling is mostly apply-only and lacks a general listener-registration seam. Cupid, Piper, Devoted Servant, and temporary faction effects need durable relationship, removal, reset, notification, and activation behavior.

### Day and elimination flow

The engine needs a day-action command, a post-cascade repeat-vote edge, reusable reactive elimination, and temporary voter-eligibility guidance. The existing `ShouldAdvanceState` constructor defect should be fixed before adding more state machines.

### Recovery boundary

Accepted recovery semantics do not resume an active listener mid-step. Committed state survives at stable boundaries; uncommitted phase-tail work is replayed. Tests must prove committed once-only resources are not spent twice.

### Degenerate-screening simulator admission

Engine or App Support does not automatically admit a Role to the simulator. Under PRD #93, every in-scope Role's completion slice must explicitly admit that Role to the versioned simulator profile/cache surface used by `LobbyEvaluationDepth.DegenerateScreeningOnly` and prove that the headless instruction/response path can complete the 1,000-run Degenerate Simulation Scenario screen. For Thief-enabled scenarios, the committed Deal Pool/offer partition is part of identity and the screen covers each semantically distinct legal `Offer1`, `Offer2`, and `Decline` branch. Any Degenerate branch blocks; if none is Degenerate, screening failures and timeouts remain nonblocking and preserve Could Not Evaluate unless every branch completed non-degenerate. This narrow admission does not imply probability estimation, probability-quality evidence, or broader simulator usefulness.

## Original dependency-aware sequence (superseded by PRD #93)

This sequence records the investigation's dependency analysis. PRD #93's published one-Role-at-a-time foundation sequence is the canonical delivery order.

- **Near-term:** Villager-Villager → Two Sisters → Three Brothers → Witch → Stuttering Judge.
- **Elimination and night resolution:** Hunter → Defender.
- **Vote control:** Stuttering Judge foundations → Scapegoat.
- **Wolf/faction foundation:** Agent/Beneficiary + collective operation → Little Girl, Big Bad Wolf, Accursed Wolf-Father, Wolf Hound; add topology before Bear Tamer, Fox, and Knight.
- **Outcome foundation:** Game Session Outcome + Durable Voting Power → Village Idiot, Piper, Angel, White Werewolf, Cupid, and Prejudiced Manipulator.
- **Ownership and power foundation:** explicit physical-card zones + Role Lock-In + permanent swaps + power availability → Thief, Devoted Servant, Elder; setup persistence and borrowed powers → Actor last.

## Adversarial review results incorporated

- Corrected the inventory from an obsolete 28-role statement to the canonical 30-role enum.
- Replaced historical expansion provenance with the dependency-only New Moon rule; Gypsy is the sole exclusion.
- Separated refreshed-baseline categories from residual work after shared foundations.
- Raised roles that depend on missing faction, outcome, topology, voting-power, or power-availability models.
- Corrected recovery wording to match stable-boundary replay rather than exact mid-listener continuation.
- Removed assumptions that the app validates individual ballots; vote-related roles use moderator-facing guidance plus authoritative domain queries.
- Refreshed the code baseline after the production `DegenerateScreeningOnly` lobby path landed and narrowed future Role admission to PRD #93's explicit screening-only simulator contract.
- Incorporated #148's Thief-wide setup consequences: pre-deal pool/offer partitioning, conditional setup for all reachable Roles, partitioned scenario identity, branchwise safety, and durable exchange/set-aside zones.

## Evidence sources

- [Canonical game rules](../domain/game-rules.md)
- [Game-rule clarifications](../domain/game-rules-clarifications.md)
- [Domain invariants](../domain/invariants.md)
- [Faction model ADR](../adr/0010-faction-model-separates-beneficiaries-from-agents.md)
- [Victory Check Window ADR](../adr/0011-victory-check-windows-are-resolution-boundaries.md)
- [Recovery ADR](../adr/0002-transient-state-not-serialized.md)
- [Central hook-ordering ADR](../adr/0003-hook-listener-ordering-stays-centralized.md)
- [Central phase-navigation ADR](../adr/0004-phase-navigation-stays-centralized.md)
- [Simulator reuse ADR](../adr/0005-simulator-reuses-engine-via-headless-driver.md)
- [Production screening-depth ADR](../adr/0013-production-lobby-evaluation-stops-after-safety-screening.md)
- [QA strategy](../agents/qa-strategy.md)
- [PRD #93](https://github.com/bicheichane/Werewolves/issues/93)
- [Thief physical-deal and Permanent Role Swap decision #148](https://github.com/bicheichane/Werewolves/issues/148)
- `Werewolves.Core/Werewolves.Core.StateModels/Enums/MainRoleType.cs`
- `Werewolves.Core/Werewolves.Core.GameLogic/Roles/SupportedRoleCatalog.cs`
- `Werewolves.Core/Werewolves.Core.GameLogic/Services/GameFlowManager.cs`
- `Werewolves.Core/Werewolves.Core.GameLogic/Services/NightInteractionResolver.cs`
- `Werewolves.Core/Werewolves.Core.GameLogic/Services/DayPhaseHandlers.cs`
