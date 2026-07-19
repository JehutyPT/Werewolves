# Elder and Accursed Wolf-Father Infection Research

Research date: 2026-07-17

Canonical status reviewed: 2026-07-19

## Question

When the Accursed Wolf-Father replaces the collective Werewolf attack with infection and the target is the Elder, does the Elder resist the infection? If so, does the attempt consume the Elder's resistance, the Wolf-Father's one-use infection, both, or neither?

## Finding

The strongest current publisher evidence supports this resolution:

1. An Elder whose resistance is unused remains alive and is not infected.
2. The infection attempt counts as the Elder's first Werewolf bite, so the Elder's resistance is spent.
3. The Accursed Wolf-Father's one-use infection is also spent when the action is confirmed.
4. If the Elder had already lost that resistance, the infection succeeds.

Only the first and fourth points are stated nearly directly by the current rulebook. Spending both resources is the strongest combined reading of the two Role descriptions, but no first-party FAQ found in this research spells out that resolution sequence verbatim.

The project has since adopted this complete resolution as its canonical product rule in the [game-rule clarifications](../domain/game-rules-clarifications.md). That local ruling settles the implementation choice; the publisher-evidence qualifications below remain relevant provenance.

## Official wording and evidence

### Current *The Pact* rulebook

Asmodee's current [product page for *The Pact*](https://www.asmodee.ca/en/product/werewolves-of-millers-hollow-the-the-pact/) links the publisher-hosted English and French rulebooks. Both editions contain the same interaction.

The Accursed Wolf-Father's description says that, after the Werewolves choose their victim and go to sleep, the Wolf-Father raises a hand to replace devouring that victim with infection. It also says that this special power is usable only once per Game Session. See page 8 of the [official French rulebook](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_FR_Rules.pdf) and page 8 of the [official English rulebook](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf).

The Elder's description then adds the specific exception that the Elder is not affected by the Wolf-Father "if it's the first time that they've been bitten." It appears on page 10 of the same [English rulebook](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_EN_Rules.pdf); the [French rulebook](https://cdn.svc.asmodee.net/production-asmodeeca/uploads/2023/07/WerewolvesThePact_FR_Rules.pdf) uses the equivalent first-bite qualification.

This is stronger than a general claim that the Elder cannot be infected. The qualification ties infection immunity to the Elder's existing first-bite resistance:

- If the infection is the Elder's first bite, it has no infection effect.
- If the Elder has already survived an earlier Werewolf bite, the exception no longer applies and infection succeeds.

The same rulebook separately says that Defender protection does not stop infection and that Witch healing does not remove an infection. Those statements reinforce that the Elder sentence is a deliberate Role-specific exception, not an application of ordinary physical protection.

### Older *Best Of* wording

The official *Best Of* booklet's third edition, dated July 2017, says simply that the Elder is not affected by the Accursed Wolf-Father, without the first-bite qualification. The booklet is available through this [retailer-hosted scan of the publisher rulebook](https://www.play-in.com/pdf/rules_games/best_of__les_loups-garous_de_thiercelieux_regles_fr.pdf), with the Elder wording on printed page 15 and its edition information on the final page.

Read literally, that edition gives the Elder permanent immunity to infection. It conflicts with the more precise conditional wording in the current Asmodee-hosted *Pact* rulebooks. This likely explains why some online summaries report blanket immunity.

For an implementation combining the full *Pact* Role set, the current bilingual *Pact* wording is the better authority: it is publisher-hosted, later, explicitly includes both Roles, and states the limiting condition that the older edition omits.

## What the official text does and does not settle

### Infection applicability: high confidence

The Elder resists infection only while the first-bite resistance remains. An already-bitten Elder can be infected. This follows directly from the current rulebook's “first time” condition.

### Accursed Wolf-Father resource: high-confidence inference

The Wolf-Father's use should be spent even when the Elder resists. The rule defines the hand signal as exercising a special power usable once per Game Session; it does not define a refundable attempt or say that only a successful conversion counts.

This is an inference because the official rules do not contain a separate failed-target paragraph. Nevertheless, refunding the use would add a rule that is absent from the text and would let a committed hidden-information action be retried.

### Elder resistance: strong but not explicit inference

The Elder's resistance should also be spent. The rule calls this event the first time the Elder is “bitten,” which is the state that distinguishes the protected first bite from later bites. Treating the infection as blocked while leaving the Elder unbitten would make the first-bite phrase do two different things: stop infection now but not advance the bite state.

The rules never say, in so many words, “remove one Elder life after a resisted infection.” That leaves a narrow ambiguity. Spending the resistance is still the most coherent reading because it preserves one shared bite history across ordinary Werewolf attacks and infection.

## Community rulings and practice found

The community evidence is limited and should not outweigh the rulebooks, but it shows why tables may disagree:

- The [MyGames Moderator reference](https://games.gameandme.fr/loupgaroudethiercelieux/settings) follows the current first-bite approach: infection does not work when the Elder is infected for the first time. It does not say whether either resource is consumed.
- [Règles 2 Jeux](https://regles2jeux.fr/la-regle-du-loup-garou/) says the Elder is not affected by the Accursed Wolf-Father without a first-bite condition. This matches the older *Best Of* wording but does not explain resource consumption.
- The archived open-source [Werewolves Assistant API](https://github.com/antoinezanardi/werewolves-assistant-api) implements the outcome subsequently adopted by this project. Its [Elder hit accounting](https://github.com/antoinezanardi/werewolves-assistant-api/blob/a0fa987173a4842da154858a2098a50bed689bb5/src/controllers/Player.js#L263-L291) spends the Elder's remaining life on an unhealed Werewolf `eat` action; its [infection resolution](https://github.com/antoinezanardi/werewolves-assistant-api/blob/a0fa987173a4842da154858a2098a50bed689bb5/src/controllers/Player.js#L594-L607) leaves a still-protected Elder unconverted; and its [history query](https://github.com/antoinezanardi/werewolves-assistant-api/blob/a0fa987173a4842da154858a2098a50bed689bb5/src/controllers/GameHistory.js#L70-L74) treats any recorded infection action as the unique use. Together, those code paths consume both resources. This is concrete community implementation practice, not publisher authority.
- A heavily customized [Puissance-Zelda forum ruleset](https://forums.puissance-zelda.com/index.php/topic,9717.0.html) explicitly makes an attempted recruitment of the Elder consume the Wolf-Father's recruitment. It also reveals the Wolf-Father's identity to the Elder and changes several other Role rules, so it is evidence of one community solution, not canonical authority.
- An informal [Pokébip Role transcription](https://www.pokebip.com/espace-membre/blogs/408959?page=4) reproduces both the first-bite condition and the once-per-game infection wording, but adds no explicit resolution rule for resource consumption.

No first-party FAQ or broad prose consensus was found that explicitly answers whether the Elder's resistance is consumed. The open-source implementation provides concrete support for spending it, but the available written sources still agree more strongly that a fresh Elder is not infected than they do about bookkeeping after that failed infection.

## Source-quality notes

| Source | Authority | What it establishes | Limitation |
| --- | --- | --- | --- |
| Current Asmodee-hosted English and French *Pact* rulebooks | Primary, highest | First-bite condition; infection action; once-per-Game-Session use | No explicit failed-attempt bookkeeping sentence |
| 2017 *Best Of* publisher booklet hosted by a retailer | Primary text through a secondary host | Older blanket-immunity wording and genuine edition conflict | Older and less specific than current *Pact* text |
| MyGames and Règles 2 Jeux | Secondary summaries | Two interpretations currently circulated | No resource-state detail; provenance unclear |
| Werewolves Assistant API | Firsthand community implementation | A real digital resolution that spends both resources | Archived, unofficial, and inferred from several code paths |
| Puissance-Zelda ruleset | Firsthand community house rules | A real implementation where failed recruitment is spent | Intentionally changes other mechanics |
| Pokébip transcription | Informal secondary transcription | Repeats current first-bite wording | Not independent adjudication |

## Historical options evaluated before canonical adoption

These options are retained as decision evidence. The canonical game-rule clarifications adopted Option A; Options B and C are no longer open product alternatives.

### Option A — Current *Pact* interpretation: spend both resources (adopted)

When infection is confirmed against an Elder with unused resistance, the Elder remains alive and uninfected, the Elder's resistance is spent, and the Wolf-Father's infection is spent. If the Elder's resistance was already spent, infection succeeds.

This best fits the latest precise publisher wording, maintains one coherent bite history, and gives confirmed one-use actions a stable commit point.

### Option B — Spend only the Wolf-Father's infection (not adopted)

The Elder remains alive, uninfected, and fully resistant to the next physical Werewolf attack; the Wolf-Father's infection is spent.

This is a possible resolution of the official text's bookkeeping gap, and resembles the outcome of some community house rules. It is less natural under the current rulebook because the infection is expressly described as the Elder's first bite but would not count toward the Elder's bite history.

### Option C — Permanent Elder immunity (not adopted)

The Elder can never be infected, following the literal 2017 *Best Of* wording. The app would still need a separate policy for whether an attempted infection is allowed and, if allowed, whether it spends the Wolf-Father's use.

This is defensible only if the app intentionally chooses the older *Best Of* ruleset. It conflicts with the current *Pact* first-bite condition and creates more product decisions than it resolves.

## Canonical product ruling (adopted)

The [game-rule clarifications](../domain/game-rules-clarifications.md) now record Option A explicitly:

> When the Accursed Wolf-Father confirms infection against the Elder, the infection use is spent. If the Elder's resistance is unused, the Elder remains alive and uninfected and that resistance is spent. Otherwise, the infection succeeds.

The rule should be described as the app's explicit resolution of a small bookkeeping ambiguity in the current *Pact* rules, not as a verbatim official FAQ ruling.
