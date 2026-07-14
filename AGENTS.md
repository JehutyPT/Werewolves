## Project structure

- **Werewolves.Core** — Game engine (C#). Architecture: `Werewolves.Core/docs/architecture.md`, vision: `Werewolves.Core/docs/product-vision.md`
- **Werewolves.Client** — Mobile client (.NET MAUI). Architecture: `Werewolves.Client/docs/architecture.md`, vision: `Werewolves.Client/docs/product-vision.md`

## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues on bicheichane/Werewolves. External issues
and pull requests are not external triage surfaces. See
`docs/agents/issue-tracker.md`.

### Issue labels and readiness

Internal issue categories, the `ready-for-agent` readiness signal, blocker semantics, and PRD labeling are defined in `docs/agents/issue-labels.md`.

### Implementation contracts

The canonical issue-body contract, freshness anchor, and `/prepare-ticket` gate for issues carrying `ready-for-agent` are defined in `docs/agents/implementation-contract.md`.

### QA strategy

Claim-first QA evidence rules and the source-test allowlist live in `docs/agents/qa-strategy.md`. Read it before writing or evaluating tests.

### Domain docs

Single-context layout — one CONTEXT.md + docs/adr/ at the repo root. See `docs/agents/domain.md`.
