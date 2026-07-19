# Witch Healing Potion and Elder Interaction Research

Research date: 2026-07-17

Canonical status reviewed: 2026-07-19

## Question

What happens to the Elder's Werewolf resistance when the Witch uses the healing potion on the Werewolves' Elder target, and what information should the Witch receive?

## Conclusion

The strongest reading of the official rules is that healing restores **one life actually lost to the current Werewolf attack**, up to the Elder's normal initial two lives. It does not grant a third life or reset the Elder to two lives regardless of prior attacks.

| State before the Night | Werewolf action | Witch heals | State after the Night |
| --- | --- | --- | --- |
| Fresh Elder: 2 lives, resistance available | First physical attack costs 1 life | Restores 1 life | Alive with 2 lives; resistance available again |
| Wounded Elder: 1 life, resistance spent | Second physical attack would cost the last life | Restores 1 life | Alive with 1 life; resistance remains spent |
| Fresh Elder protected by the Defender | Attack costs no life | Potion has nothing to restore | Alive with 2 lives; no extra-life credit |

The Witch should be shown the Elder as the Werewolves' victim even when the Elder would survive without healing. This is a strong rules inference rather than an Elder-specific FAQ sentence: the Witch is shown the Werewolves' selected victim, while the Elder rule says only that the Elder's first survival is not publicly announced.

The project has since adopted these result-state and disclosure conclusions as canonical product rules in the [game-rule clarifications](../domain/game-rules-clarifications.md). The distinctions below between direct publisher text and product inference remain part of the evidence record, not open implementation choices.

## Official rules

### Current English and French rulebooks

The current publisher-hosted *The Pact* rulebook gives the three relevant rules together:

- the Witch is shown the Werewolves' latest victim and may use the healing potion;
- the Elder survives the first Werewolf attack and is eliminated by the second; and
- if the Elder is healed, they “recover only one life.”

It also says that the healing potion does not remove a Wolf-Father infection. See pages 9–10 of the [official English *The Pact* rulebook](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf) and the equivalent text on pages 9–10 of the [official French *The Pact* rulebook](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_FR_Rules.pdf).

The publisher-hosted standalone *New Moon* rulebooks repeat the Elder rule: the Elder starts with two Werewolf lives, loses on the second attack, and healing recovers one life. See page 25 of the [official English *New Moon* rulebook](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesNewMoon_EN_Rules.pdf) and page 25 of the [official French *New Moon* rulebook](https://cdn.svc.asmodee.net/staging-asmodeeca/uploads/2023/07/WerewolvesNewMoon_FR_Rules.pdf).

This wording is not a recent novelty. An [older English *New Moon* rulebook scan](https://manuals.plus/m/68a0f4e989914064de00148f55017e13db3d71e1cd6fc9742cf2ab7c100c1da5.pdf) says on page 25 that a cured Elder recovers one life, and the July 2017 third-edition [French *Best Of* booklet](https://en.play-in.com/pdf/rules_games/best_of__les_loups-garous_de_thiercelieux_regles_fr.pdf) contains the same one-life rule on printed page 15.

Asmodee France's current Role overview describes the healing action as resurrecting the Werewolves' victim rather than granting a free-standing health bonus. It is useful corroboration for an undo/restore model, although it does not discuss the Elder specifically. See [Asmodee France's Witch overview](https://www.asmodee.fr/les-roles-des-loups-garous-de-thiercelieux-que-vous-allez-adorer/).

### What follows directly, and what is inferred

**Direct official text:** healing an Elder restores one life, not all lives; an Elder normally takes two Werewolf attacks to eliminate; an actual Wolf-Father infection is not cured by healing.

**Strong result-state inference:** on a fresh Elder's first physical hit, the hit removes one of two lives and healing restores that one, ending at two. On a wounded Elder's second hit, healing restores only the life lost that Night, ending at one. This is the only reading that gives ordinary meaning to “recover,” “one,” and the Elder's two-hit limit at the same time.

**Strong information-flow inference:** the Witch sees the selected Elder. The Witch instruction does not condition disclosure on whether the target will ultimately die, and the Elder's no-announcement sentence concerns the concealed outcome of the first attack. No publisher rule found creates a special exception that hides an Elder target from the Witch.

**Cap inference:** healing cannot raise the Elder above the normal initial two lives. The rule says “recover,” not gain, and permits healing only in response to the current Werewolf victim. No official source found defines a bankable third life.

## First-party FAQ search

No publisher-hosted FAQ answering these compound cases was found.

An archived document labels a section “FAQ (officielle, améliorée par P. Marty)” and says that redundantly healing a Defender-protected victim does not grant a one-life credit. Because the document is community-hosted, expressly includes later additions, and provides no publisher provenance, it should not be treated as verified first-party authority. It is still useful evidence against banking an extra life. See the [archived FAQ transcription](https://www.yumpu.com/fr/document/view/17094548/les-loups-garous-de-thiercelieux-ma-page-perso), lines 91–106 in the web transcription.

## Moderator and implementation evidence

The [MyGames moderator reference](https://games.gameandme.fr/loupgaroudethiercelieux/settings) states that Defender protection means the Elder was not attacked, while Witch healing cancels only the effect of the latest attack. Applied to the two states above, a healed first hit returns the Elder to two lives and a healed second hit leaves the Elder on one. This is a concrete moderator ruling, not publisher authority.

The archived open-source [Werewolves Assistant API](https://github.com/antoinezanardi/werewolves-assistant-api) implements the same outcomes at commit `a0fa987`:

- Werewolf attacks against the Elder are counted from the configured initial life count, but an attack is excluded when a same-turn Witch life-potion play exists. Therefore a first healed attack leaves 2 lives and a second healed attack leaves 1, rather than resetting the Elder. See [`isAncientKillable`](https://github.com/antoinezanardi/werewolves-assistant-api/blob/a0fa987173a4842da154858a2098a50bed689bb5/src/controllers/Player.js#L263-L291).
- Every ordinary collective Werewolf target receives the `eaten` marker, including an Elder, and the Witch's life potion is restricted to a target carrying that marker. This concretely exposes the surviving Elder target to the Witch. See [Werewolf target recording](https://github.com/antoinezanardi/werewolves-assistant-api/blob/a0fa987173a4842da154858a2098a50bed689bb5/src/controllers/Player.js#L594-L612) and [life-potion target validation](https://github.com/antoinezanardi/werewolves-assistant-api/blob/a0fa987173a4842da154858a2098a50bed689bb5/src/controllers/Player.js#L75-L84).

This implementation is particularly probative because it records full attack history rather than merely paraphrasing the Role card, but it remains unofficial community software.

## Wolf-Father infection edge

The official text settles two endpoints but not their combination:

1. A fresh Elder is not affected by the Wolf-Father on the first bite.
2. Witch healing does not remove an actual Wolf-Father infection.

It does **not** say whether healing restores an Elder life that an implementation chooses to spend on a resisted infection. That life expenditure is itself an explicit product resolution of an official bookkeeping gap, so the healing result must also be recorded as a product resolution rather than attributed verbatim to the publisher.

There is nevertheless concrete support for restoring the resistance. The Werewolves Assistant API treats a resisted infection against a fresh Elder as an `eaten` target, which makes the target eligible for the Witch's life potion; its same-turn healing check then excludes that bite from the Elder's accumulated attacks. See the [resisted-infection branch](https://github.com/antoinezanardi/werewolves-assistant-api/blob/a0fa987173a4842da154858a2098a50bed689bb5/src/controllers/Player.js#L594-L607) together with [`isAncientKillable`](https://github.com/antoinezanardi/werewolves-assistant-api/blob/a0fa987173a4842da154858a2098a50bed689bb5/src/controllers/Player.js#L263-L291).

For this app's already-chosen rule that a resisted infection spends the fresh Elder's resistance, the most coherent resolution is therefore:

> The Witch is shown the collective target. If the Elder resists the infection and the Witch heals that target, the potion restores the one Elder life just spent. The Elder ends alive, uninfected, and with resistance available again. The healing potion does not “cure infection,” because no infection took hold; the already-committed Wolf-Father use and Witch potion remain spent.

This is a strong combined inference supported by one concrete implementation, not an explicit official ruling. If infection succeeds against an already-wounded Elder, healing cannot remove that infection under the direct official rule.

## Canonical product ruling (adopted)

The [game-rule clarifications](../domain/game-rules-clarifications.md) now model the healing potion as restoring exactly one Elder life lost to the selected physical attack, capped at the Elder's normal two lives:

- first physical hit plus healing: `2 → 1 → 2`;
- second physical hit plus healing: `1 → 0 → 1`;
- blocked hit plus redundant healing: no increase;
- fresh Elder's resisted infection plus healing, under this app's resistance-spending rule: `2 → 1 → 2`, still uninfected;
- a successful infection is not cured.

The Moderator shows the Witch every Player targeted that Night by the collective Werewolf attack, the Big Bad Wolf's extra attack, or the White Werewolf's solo attack before final resolution, including a protected or fresh Elder who would survive. A confirmed redundant or ineffective healing choice still spends the potion under the canonical One-Use Resource rule.
