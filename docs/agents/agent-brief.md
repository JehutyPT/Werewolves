# Agent Briefs

This document defines Werewolves-specific additions to the global triage skill's agent brief format. Use it together with the global `AGENT-BRIEF.md`.

In this repository, an agent brief is a structured comment on a GitHub issue. It is the authoritative specification for a `ready-for-agent` issue; the issue body and discussion remain context. The issue body is product-level (what to build, acceptance criteria); the agent brief comment is implementation-level (how to approach it, key interfaces, scope boundaries).

## Template

```markdown
## Agent Brief

**Category:** bug / feature / architecture / spike
**Summary:** one-line description of what needs to happen.

**Current behavior:**
What happens now. For bugs, this is the broken behavior.
For features, this is the status quo the feature builds on.

**Desired behavior:**
What should happen after the agent's work is complete.
Be specific about edge cases and error conditions.

**Key interfaces:**
- `TypeName` — what needs to change and why
- `MethodName()` — current contract vs. desired contract
- Config / DTO shape — any new fields or shapes the agent should introduce

**Acceptance criteria:**
- [ ] Specific, testable criterion 1
- [ ] Specific, testable criterion 2
- [ ] Specific, testable criterion 3

**QA evidence:**
- **Claim:** observable behavior the tests or checks must prove.
- **Preferred evidence:** test surface and artifact from `docs/agents/qa-strategy.md`.
- **Forbidden evidence:** evidence that would couple to implementation shape.
- **Source-test allowlist needed:** yes/no; if yes, cite or add the allowlist row.

**Out of scope:**
- Adjacent thing that should NOT be addressed in this issue
- Future enhancement that might seem related but is separate
```

## Repo Conventions

- **All user-facing UI text must be in Portuguese.** Mention this in `Desired behavior` when a slice adds visible UI surface area.
- **Cite ADRs.** If the work touches an area covered by an ADR (`docs/adr/`), reference it in `Key interfaces` or `Desired behavior` so the agent honours the decision.
- **Use the domain glossary.** Phrase the brief in `CONTEXT.md` terminology: Faction, not Team; Moderator, not GM; Phase, not Round.
- **Categories.** This repo uses `feature` / `bug` / `architecture` / `spike`; see `docs/agents/triage-labels.md`. Skill docs sometimes say `enhancement`; that maps to `feature` here.

## Relationships

Parent and blocker relationships are **formal tracker relationships**, not body-text sections. They are managed via the `set-parent` and `add-blocked-by` verbs defined in `docs/agents/issue-tracker.md`.

- **Parent** — set via `set-parent(child_id, parent_id)`. Points to the PRD or umbrella slice this issue is part of. Agents can query it with `query-parent(id)` for context.
- **Blocked by** — set via `add-blocked-by(id, blocker_id)`. The auto-promotion workflow watches these formal relationships: when all blockers close, the issue auto-promotes from `blocked` to `needs-triage`. Query with `query-blockers(id)`.

Do not embed parent or blocker references inside the agent brief comment or issue body. They are project state managed at the tracker level, not specification text.

## Example

```markdown
## Agent Brief

**Category:** feature
**Summary:** Auto-save and resume the active game so progress survives app interruptions.

**Current behavior:**
`GameClientManager` keeps the active session in memory. If the app process is
killed or the device restarts, the in-progress game is lost. On launch, the
app always returns the Moderator to the Lobby.

**Desired behavior:**
After every successful `ProcessInput`, the Core's stable recovery snapshot is
serialised and written to `FileSystem.AppDataDirectory` as a single overwriting
save file. On app launch, if a save file exists, `GameClientManager` rehydrates
the session via `GameService` and returns the Moderator to the Dashboard at the
latest stable Main Phase recovery boundary (per ADR-0002). The save file is
cleared when a game ends (victory) or a new game starts. A corrupted save file
must not crash the app.

**Key interfaces:**
- `GameClientManager` — gains save/load lifecycle; existing `ProcessInput` and
  `StateChanged` semantics unchanged.
- `GameService` — used for rehydration; expected to accept a serialised stable
  recovery snapshot and return a fully-restored `IGameSession`.
- `FileSystem.AppDataDirectory` — single canonical save location.

**Acceptance criteria:**
- [ ] Successful `ProcessInput` writes the Core stable recovery snapshot to disk.
- [ ] Current-phase tail work remains transient until Core captures a stable boundary (per ADR-0002).
- [ ] On app launch, an existing save file triggers session rehydration.
- [ ] After rehydration, the Moderator continues from the latest stable Main
      Phase recovery boundary.
- [ ] Save file is deleted on victory and on new-game start.
- [ ] Corrupt save file is detected and surfaced as a recoverable error
      (app does not crash; Moderator returns to Lobby).
- [ ] All user-facing strings are in Portuguese.

**QA evidence:**
- **Claim:** save/recovery behavior follows Core stable-boundary rules.
  **Preferred evidence:** service tests through `GameClientManager` with fake
  persistence and observable session state.
  **Forbidden evidence:** assertions over private fields or serialized
  implementation details not owned by the recovery contract.
  **Source-test allowlist needed:** no.
- **Claim:** recoverable corrupt-save errors return the Moderator to Lobby.
  **Preferred evidence:** service or rendered UI evidence through public app
  flow and resource-backed Portuguese copy.
  **Forbidden evidence:** raw Razor/source scans for specific component names,
  CSS classes, or resource keys.
  **Source-test allowlist needed:** no for this issue; any new source scan must
  cite or add the matching `docs/agents/qa-strategy.md` allowlist row.

**Out of scope:**
- Multiple save slots / game list management.
- Cloud sync or cross-device resume.
- Mid-Phase resume (deferred per ADR-0002).
- Schema versioning / migration of older save files.
```

## When The Brief Isn't Ready

If you cannot write a specific, behavioral brief with testable acceptance
criteria and QA evidence choices, the issue is not `ready-for-agent`. Common signs:

- You catch yourself writing "or" / "either" / "depending on" in acceptance criteria.
- A criterion is shaped like "X works correctly" rather than "X produces Y given input Z".
- The brief bundles unrelated concerns that a single PR cannot land cleanly.

In those cases, move the issue back to `needs-info` or `needs-triage` and sharpen the brief, or split the issue.
