# Thief acquired-Role activation timing

Research date: 2026-07-18

Canonical status reviewed: 2026-07-19

Local-policy supersession incorporated: 2026-07-20

Status: historical, non-authoritative research snapshot through 2026-07-20. Current product rules live in the [game rules](../domain/game-rules.md), [game-rule clarifications](../domain/game-rules-clarifications.md), and [Thief setup ADR](../adr/0017-thief-offer-is-committed-before-the-physical-deal.md).

## Question

After the Thief exchanges their Character Card on Night 1, when does the acquired
Role first operate? In particular, does it act in that Night, how do Night-1-only
and pre-game powers behave, and can a Role's call already have passed?

## Conclusion

The best reading of the physical rules is **same-Night activation from the completed
Thief exchange**, at the acquired Role's ordinary remaining Night-1 call. It is a
strong interpretation, rather than an explicit publisher sentence: the official
rule says that the Thief assumes the acquired Role for the rest of the game, puts
Thief first in the Night-1 call order, and continues with every relevant call after
it. It does not say the word “immediately” or expressly say “act this Night.”

This is still stronger than a next-Night rule. The same official page expressly says
that a **Devoted Servant**'s new Role “must be called on the next night”; no such
delay qualifies the Thief. Treating the Thief as inactive until Night 2 would add a
delay that the physical rules did not state.

Because the Thief is first, no other **Night-1 role call** can already have passed.
Pre-game preparations have passed, but they are setup state, not Role calls and
must not be replayed.

The project has since adopted this timing as its canonical product rule in the
[game-rule clarifications](../domain/game-rules-clarifications.md) and
[game rules](../domain/game-rules.md). Same-Night activation is therefore settled
for this app even though it remains an interpretation rather than a verbatim
publisher sentence.

## Primary-source evidence

### Official *The Pact* rulebook

The current Asmodee-hosted English [*The Werewolves of Miller's Hollow: The Pact*
rulebook](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf)
is the primary physical-rules source used here. Its contents describe *The Pact* as
the compilation containing the base game, *New Moon*, *The Village*, and
*Characters* ([printed p. 3](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=3)), so it is the appropriate unified source for this
cross-expansion call-order question.

**Direct facts:**

- The Thief is designated for the first Night. Two added Simple Villager cards leave
  two undealt cards; during that Night the Thief may exchange their card (and must
  do so if both are Werewolves). If they take one, “they assume that role for the
  rest of the game.” [Official rulebook, p. 13](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=13).
- The exact printed first-Night sequence is **Thief → Actor → Cupid → Seer → Fox
  → Lovers → Wandering Judge → Two Sisters → Three Brothers → Wild Child → Bear
  Tamer → Scandalmonger → Pyromaniac → Defender → all Werewolves** (including a
  Werewolf Wolf-Hound, White Werewolf, Accursed Wolf-Father, and Big Bad Wolf)
  **→ Little Girl spying interval → Baker → Accursed Wolf-Father → Big Bad Wolf
  → Witch → Gypsy → Piper → Charmed players**. The same page separately lists the
  ongoing “each night” order. [Official rulebook, p. 24](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=24).
- The book makes a different timing rule for the Devoted Servant: after that swap,
  “The Servant's new role must be called on the next night.” It also says that role
  is brand-new and its powers are refreshed. [Official rulebook, p. 13](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=13).
- All Werewolves wake together each Night and choose a non-Werewolf victim.
  [Official rulebook, p. 8](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=8).

**Inference from those facts:** a physical moderator completes the Thief exchange,
then continues the printed sequence with the player now holding the acquired card.
That player receives one ordinary activation when the acquired Role's later call is
reached. The rulebook contains no instruction to postpone that new card, restart
Night 1, or replay calls. This is the normal physical-table interpretation, but it
should be recorded as a project decision because the exact phrase “same Night” is
absent.

### Timing distinctions that must not be generalized

- The Accursed Wolf-Father's infected target becomes a Werewolf immediately but
  joins Werewolf feasts only on later Nights. That is a specific
  conversion rule, not the Thief rule. [Official rulebook, p. 8](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=8).
- The Wolf-Hound makes its choice before the printed Werewolf-group call on Night
  1. The book therefore supports same-Night group participation through order, but
  does not supply a general sentence for all transformations. [Official rulebook,
  p. 14](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=14); [turn overview, p. 24](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=24).
- The Actor's three available cards, the Gypsy's Spiritualism cards, and the
  Prejudiced Manipulator's public groups are prepared before play. The Actor and
  Gypsy still have later Night calls, whereas group division is already complete.
  [Actor, p. 13](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=13), [Prejudiced Manipulator, p. 15](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=15), [Gypsy, p. 23](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=23), [turn overview, p. 24](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=24).

## Current local-policy supersession from #148

The settled local decision in [#148](https://github.com/JehutyPT/Werewolves/issues/148)
supersedes this research's chance-determined “two undealt cards” premise for product
implementation only; it does not rewrite or weaken the historical publisher evidence
above. For the app, a Thief-enabled Role Composition has `P + 2` instances and is
partitioned at Role Lock-In into a `P`-card Deal Pool containing exactly one Thief
plus two private, distinct non-Thief offer instances. Conditional setup covers every
Role in the pool or offers, the partition is Simulation Scenario identity, and only
the Deal Pool is physically dealt. Night 1 therefore consumes the already-committed
`Offer1`, `Offer2`, or legal `Decline` branch: an exchange moves the original Thief
card and unchosen offer to Set-Aside, while Decline keeps Thief and moves both offers
from their offer slots to Set-Aside.
Any Degenerate screening branch blocks; absent one, screening failures and timeouts
remain nonblocking.

This supersession leaves [#99](https://github.com/JehutyPT/Werewolves/issues/99)'s
same-Night activation ruling intact; it changes the local card-source, setup, and
screening implementation model, not the adopted activation timing.

## Consequences by acquired-Role category

| Category | Physical-rule result | Implementation consequence of the adopted timing |
| --- | --- | --- |
| Later same-Night active Role | The acquired Role is called once at its normal remaining Night-1 slot. This covers Actor, Cupid, Seer, Fox, Defender, the Werewolf group and its later actions, Witch, Piper, and applicable expansion Roles. This is the strong sequence inference from the [Night-1 order, p. 24](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=24), not an express timing sentence. | Resolve the selected configured offer as one atomic private exchange before advancing the call cursor: make that instance Player-owned, set aside the original Thief card and unchosen offer, commit the Permanent Role Swap with fresh power state, then calculate each remaining call from the current Role. Do not create an additional “Thief-acquired” turn. |
| Night-1-only or recognition/setup action | The Night-1 call is still ahead of Thief for Cupid/Lovers, Wandering Judge, Sisters/Brothers, Wild Child, Wolf-Hound, Bear Tamer identification, and the other printed Night-1 calls. An acquired Cupid can therefore choose Lovers and the following Lovers call happens normally; an acquired Wild Child or Wolf-Hound receives its normal Night-1 choice. [Official turn order, p. 24](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=24). | Grant the one ordinary Night-1 action if its slot is reached. Do not replay an earlier setup or grant a second use. For Sisters/Brothers, wake only the current eligible group; do not invent a solo “recognition” power if the remaining card distribution leaves no partner(s). |
| Pre-game-only setup | Dealing, Actor-card preparation, Gypsy materials, and Prejudiced Manipulator grouping precede the Thief call and are not rerun by the exchange. [Official turn overview, p. 24](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=24); [Actor, p. 13](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=13); [Gypsy, p. 23](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=23); [Prejudiced Manipulator, p. 15](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=15). | After Role Lock-In, derive and persist every conditional setup artifact required by a Role in the Deal Pool or either offer, independently of active Role ownership. A later Actor or Gypsy uses the already prepared material; an acquired Prejudiced Manipulator uses the already assigned public group. Missing required setup blocks progress before screening and Lobby Exit rather than being improvised mid-Night. |
| Passive, daytime, or reaction Role | There is no wake-up slot to defer. The player now has the card and its future condition can apply: e.g., Bear Tamer at the coming Dawn, Hunter on a later elimination, Elder protection when attacked, or Vote-triggered abilities at their normal event. [Hunter and Elder, p. 10](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=10); [Bear Tamer, p. 12](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=12). | Make the active Role effective at swap completion, while firing its ability only from its ordinary trigger. Do not synthesize a trigger that occurred before acquisition. |
| Actual Werewolf Role | The acquired player is an actual Werewolf before the later collective group call. The physical rule says all Werewolves wake together; this supports joining that Night's group and receiving any later role-specific Werewolf call in its printed position. The group still cannot target a Werewolf. [Official Werewolf rule, p. 8](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=8); [Night-1 order, p. 24](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=24). | Update current Werewolf **Faction Agent** state atomically with the Role/Faction swap, so the player joins Night 1's group and is excluded from its target set. Preserve role-specific cadence: the project's White Werewolf solo action is unavailable on Night 1, even though that Role joins the collective group. See the [canonical glossary](../../CONTEXT.md) and [game rules](../domain/game-rules.md). |
| Role whose call has passed | Under the official Night-1 sequence, none: Thief is first. Only pre-game setup has elapsed. [Official turn order, p. 24](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf#page=24). | Generalize safely: never rewind or replay a passed call. If a future event or variant moves Thief later, the acquired Role first acts at its next eligible future call. This general rule is not needed in the normal Night-1 sequence. |
| Not a swappable Character Card | Lovers and Charmed are Status Effects, Sheriff and Town Crier are assignments, and Actor Setup Cards are outside Role Composition—not spare Character Cards. See the [game rules](../domain/game-rules.md) and [canonical glossary](../../CONTEXT.md). | Keep card-zone validation separate from status effects and assignments; only the two configured, distinct non-Thief offer instances are legal choices, plus `Decline` when the current local rule permits it. |

## Local canonical rules and code

### Domain rules

The local domain agrees that a Role determines abilities and wake-up schedule and
defines a Permanent Role Swap as a lasting Role and default Faction Beneficiary
replacement. It specifically treats the Thief swap as changing the beneficiary
link, subject to Cross-Faction Lovers precedence. The
[game rules](../domain/game-rules.md) and
[game-rule clarifications](../domain/game-rules-clarifications.md) now explicitly
settle activation timing: the swap takes effect immediately, the canonical Night 1
order continues forward, an acquired Role receives its ordinary remaining call,
and neither passed calls nor pre-game setup are replayed.

The local adapted Night-1 order in the [game rules](../domain/game-rules.md) also
begins with Thief and places every relevant call after it. The same rules explicitly
give Wolf Hound the same-Night group result—its Werewolf choice joins the collective
action later that Night—but that is an explicit local Wolf Hound ruling, not the
source of the now-settled Thief decision.

Two local constraints apply to the canonical implementation:

- The [canonical glossary](../../CONTEXT.md) separates Werewolf Faction Agent
  participation from beneficiary Faction, while the
  [game rules](../domain/game-rules.md) fix White Werewolf's solo-action cadence as
  Nights 2, 4, 6, …, never Night 1.
- The current rules already apply a Permanent Role Swap to current group membership
  for Sisters/Brothers in the
  [game-rule clarifications](../domain/game-rules-clarifications.md), which is
  consistent with querying current active holders at the later call.

### Current implementation status

There is no implemented Thief behavior to treat as precedent:

- Configuration models Thief only as two additional physical Role cards
  (`Werewolves.Core/Werewolves.Core.StateModels/Models/GameSessionConfig.cs:99-108`,
  `:299-302`); related tests only cover that count
  (`Werewolves.Core/Werewolves.Core.Tests/Integration/GameLifecycleTests.cs:323-350`).
- `SupportedRoleCatalog` registers only Simple Werewolf, Seer, Wild Child, and
  Simple Villager (`Werewolves.Core/Werewolves.Core.GameLogic/Roles/SupportedRoleCatalog.cs:12-18`), and game creation rejects unsupported Roles
  (`Werewolves.Core/Werewolves.Core.GameLogic/Services/GameService.cs:143-154`).
- The central dispatcher nevertheless lists Thief first
  (`Werewolves.Core/Werewolves.Core.GameLogic/Services/GameFlowManager.cs:34-65`) and
  processes listeners sequentially (`Werewolves.Core/Werewolves.Core.GameLogic/Models/StateMachine/SubPhaseStage.cs:186-234`). A hypothetical swap would be visible to a later listener that looks up current `MainRole`
  (`Werewolves.Core/Werewolves.Core.StateModels/Extensions/StringExtensions.cs:48-54`), but no earlier listener is revisited. This is an implementation consequence, **not** an adopted policy.
- Existing `AssignRole` merely overwrites `MainRole`
  (`Werewolves.Core/Werewolves.Core.StateModels/Core/GameSession.cs:220-236`,
  `Werewolves.Core/Werewolves.Core.StateModels/Log/AssignRoleLogEntry.cs:25-33`). It
  does not own card zones, active-Role resources, beneficiary/agent state, or swap
  audit information. A Thief slice needs an atomic swap command rather than reuse
  of that thin assignment primitive.

Centralized ordering is an established correctness property, so the timing decision
belongs in the central schedule and behavioral tests, not listener-local priority
logic (`docs/adr/0003-hook-listener-ordering-stays-centralized.md:1-7`).

## Canonical product ruling (adopted)

The canonical domain documents settle the product behavior as follows:

> The Thief's Night 1 Permanent Role Swap takes effect immediately. Continue
> forward through the canonical Night 1 call order; the acquired Role receives its
> ordinary call if that call is still ahead, and no earlier call is replayed. Since
> Thief is first, an acquired first-Night-only power acts that Night, while pre-game
> setup is not repeated.

The research also produced this implementation-directed formulation:

> On Night 1, resolve the Thief's exchange atomically. From that point the player
> holds and operates the acquired Role. Continue the existing Night call order
> without rewind: when a remaining call reaches that Role, grant its ordinary action
> once; passive and reactive effects apply only to later ordinary triggers. Pre-game
> setup is not repeated. Since Thief is first in the normal Night-1 order, no Role
> call has already passed. If the acquired Role is an actual Werewolf Role, the
> player joins the later Night-1 collective Werewolf group; role-specific cadence
> restrictions still apply.

The first statement is the adopted local ruling. The second remains planning
evidence for implementing that ruling; its atomicity and Agent-update details must
be realized through the broader Permanent Role Swap and Faction contracts. Both
remain faithful to the official order while preserving the source distinction: the
same-Night point is a local interpretation, not a verbatim publisher ruling.
