## Project structure

- **Werewolves.Core** — Game engine (C#). Architecture: `Werewolves.Core/docs/architecture.md`, vision: `Werewolves.Core/docs/product-vision.md`
- **Werewolves.Client** — Mobile client (.NET MAUI). Architecture: `Werewolves.Client/docs/architecture.md`, vision: `Werewolves.Client/docs/product-vision.md`

## Agent skills

### Issue tracker

Issues are tracked in GitHub Issues on bicheichane/Werewolves. See `docs/agents/issue-tracker.md`.

### Triage labels

Default label vocabulary (needs-triage, needs-info, ready-for-agent, ready-for-human, wontfix). See `docs/agents/triage-labels.md`.

### Agent briefs

Template and conventions for issues moving to `ready-for-agent`. See `docs/agents/agent-brief.md`.

### QA strategy

Claim-first QA evidence rules and the source-test allowlist live in `docs/agents/qa-strategy.md`. Read it before writing or evaluating tests.

### Domain docs

Single-context layout — one CONTEXT.md + docs/adr/ at the repo root. See `docs/agents/domain.md`.
