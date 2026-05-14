# Agent Briefs

When an issue moves to `ready-for-agent`, its body must be a complete brief — a durable spec an AFK agent can work from days or weeks later, after the codebase has shifted around it.

This document defines the template, the philosophy, and the conventions used in this repository.

## Philosophy

### Spec vs. workflow

An issue body has two layers:

1. **The brief itself** — _what_ the agent must build, in behavioral terms. This is the spec.
2. **Workflow metadata** — _where this work fits_ in the project: parent slice, prerequisite issues. This is not the spec; it's coordination state.

Keep these layers visible but distinct. The brief survives on its own; the workflow sections wrap it.

### Durability over precision

The issue may sit in `ready-for-agent` for days or weeks. The codebase will change.

- **Do** describe interfaces, types, and behavioral contracts.
- **Do** name specific types, function signatures, or config shapes the agent should look for or modify.
- **Don't** reference file paths.
- **Don't** reference line numbers.
- **Don't** assume current implementation structure will remain the same.

### Behavioral, not procedural

Describe _what_ the system should do, not _how_ to implement it. The agent will explore the codebase fresh and make its own implementation decisions.

- **Good:** "When `GameClientManager.ProcessInput` returns a successful result, the event log is serialised and written to `FileSystem.AppDataDirectory`."
- **Bad:** "Open `GameClientManager.cs` and add a save call after line 87."

### Complete acceptance criteria

Every criterion must be independently verifiable. The agent needs to know when it's done.

- **Good:** "Mute state persists across instruction changes."
- **Bad:** "Audio should work correctly."

### Explicit scope boundaries

State what is **out of scope**. Prevents the agent from gold-plating or making assumptions about adjacent features.

## Template

```markdown
## Parent

#<parent-issue-or-prd>

## Blocked by

- #<prerequisite-issue> *(omit section entirely if no blockers)*

---

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

**Out of scope:**
- Adjacent thing that should NOT be addressed in this issue
- Future enhancement that might seem related but is separate
```

### Conventions specific to this repo

- **All user-facing UI text must be in Portuguese.** Mention this in `Desired behavior` when a slice adds visible UI surface area.
- **Cite ADRs.** If the work touches an area covered by an ADR (`docs/adr/`), reference it in `Key interfaces` or `Desired behavior` so the agent honours the decision.
- **Use the domain glossary.** Phrase the brief in `CONTEXT.md` terminology (Faction, not Team; Moderator, not GM; Phase, not Round).
- **Categories.** This repo uses `feature` / `bug` / `architecture` / `spike` — see `docs/agents/triage-labels.md`. Skill docs sometimes say "enhancement"; that maps to `feature` here.

### Workflow sections (Parent / Blocked by)

- **Parent** points to the PRD or umbrella slice this issue is part of. Used for traceability — agents reading the brief can follow the link for context.
- **Blocked by** lists prerequisite issues. The auto-promotion workflow watches these references: when all listed blockers close, the issue auto-promotes from `blocked` to `ready-for-agent`. Maintainers can override.

Both sections live **outside** the `## Agent Brief` block. Do not embed prerequisites inside `Key interfaces` or `Acceptance criteria` — they are project state, not specification.

If an issue has no parent (standalone) or no blockers (already actionable), omit the relevant section.

## Examples

### Good brief (feature)

```markdown
## Parent

#2

## Blocked by

- #19

---

## Agent Brief

**Category:** feature
**Summary:** Auto-save and resume the active game so progress survives app interruptions.

**Current behavior:**
`GameClientManager` keeps the active session in memory. If the app process is
killed or the device restarts, the in-progress game is lost. On launch, the
app always returns the moderator to the Lobby.

**Desired behavior:**
After every successful `ProcessInput`, the event log is serialised and written
to `FileSystem.AppDataDirectory` as a single overwriting save file. On app
launch, if a save file exists, `GameClientManager` rehydrates the session via
`GameService` and returns the moderator to the Dashboard at the start of the
current main phase (per ADR-0002). The save file is cleared when a game ends
(victory) or a new game starts. A corrupted save file must not crash the app.

**Key interfaces:**
- `GameClientManager` — gains save/load lifecycle; existing `ProcessInput` and
  `StateChanged` semantics unchanged.
- `GameService` — used for rehydration; expected to accept a serialised event
  log and return a fully-restored `IGameSession`.
- `FileSystem.AppDataDirectory` — single canonical save location.

**Acceptance criteria:**
- [ ] Successful `ProcessInput` writes the event log to disk.
- [ ] Only the event log is serialised (per ADR-0002).
- [ ] On app launch, an existing save file triggers session rehydration.
- [ ] After rehydration, the moderator continues from the start of the
      current main phase.
- [ ] Save file is deleted on victory and on new-game start.
- [ ] Corrupt save file is detected and surfaced as a recoverable error
      (app does not crash; moderator returns to Lobby).
- [ ] All user-facing strings are in Portuguese.

**Out of scope:**
- Multiple save slots / game list management.
- Cloud sync or cross-device resume.
- Mid-phase resume (deferred per ADR-0002).
- Schema versioning / migration of older save files.
```

### Bad brief

```markdown
## Agent Brief

**Summary:** Fix the save thing.

**What to do:**
The save isn't working right. Look in `GameClientManager.cs` around line 120
and make sure it writes the file properly.

**Files to change:**
- `Werewolves.Client/Services/GameClientManager.cs` (line 120)
```

This is bad because:
- No category, no parent, no scope boundaries.
- Vague description ("the save isn't working right").
- References file paths and line numbers that will go stale.
- Procedural ("look in X and make sure Y") instead of behavioral.
- No acceptance criteria.
- No mention of Portuguese, ADR-0002, or other repo-specific conventions.

## When the brief isn't ready

If you cannot write a specific, behavioral brief with testable acceptance
criteria, the issue is not `ready-for-agent`. Common signs:

- You catch yourself writing "or" / "either" / "depending on" in acceptance
  criteria.
- A criterion is shaped like "X works correctly" rather than "X produces Y
  given input Z".
- The brief bundles unrelated concerns (different layers, different risk
  profiles) that a single PR cannot land cleanly.

In those cases, move the issue back to `needs-info` or `needs-triage` and
sharpen the brief — or split the issue.
