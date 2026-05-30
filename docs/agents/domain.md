# Domain Docs

How the engineering skills should consume this repo's domain documentation when exploring the codebase.

## Before exploring, read these

- **`CONTEXT.md`** at the repo root.
- **`docs/adr/`** — read ADRs that touch the area you're about to work in.
- **`docs/domain/invariants.md`** — read when implementation or tests rely on stable domain facts.
- **`docs/game-rules-clarifications.md`** — read when role interactions, win timing, or rule disambiguation matter.
- **`docs/agents/qa-strategy.md`** — read before writing or evaluating tests.

If any of these files don't exist, **proceed silently**. Don't flag their absence; don't suggest creating them upfront. The producer skill (`/grill-with-docs`) creates them lazily when terms or decisions actually get resolved.

## File structure

Single-context repo:

```
/
├── CONTEXT.md
├── docs/domain/
│   └── invariants.md
├── docs/game-rules.md
├── docs/game-rules-clarifications.md
├── docs/adr/
│   ├── 0001-*.md
│   └── 0002-*.md
├── docs/agents/
│   ├── issue-tracker.md
│   ├── issue-lifecycle.md
│   ├── triage-labels.md
│   ├── qa-strategy.md
│   └── domain.md
├── Werewolves.Client/
├── Werewolves.Client.Shared/
├── Werewolves.Client.Tests/
└── Werewolves.Core/
```

## Use the glossary's vocabulary

When your output names a domain concept (in an issue title, a refactor proposal, a hypothesis, a test name), use the term as defined in `CONTEXT.md`. Don't drift to synonyms the glossary explicitly avoids.

If the concept you need isn't in the glossary yet, that's a signal — either you're inventing language the project doesn't use (reconsider) or there's a real gap (note it for `/grill-with-docs`).

## Flag ADR conflicts

If your output contradicts an existing ADR, surface it explicitly rather than silently overriding:

> _Contradicts ADR-0007 (event-sourced orders) — but worth reopening because…_
