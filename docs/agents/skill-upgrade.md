# Skill Upgrade: Tracker-Agnostic Operations + Formal Relationships

## Design Principle

Global skills (`~/.claude/skills/`) speak in **abstract operations** — "set parent", "add blocker", "list issues by role". Repo-level docs (`docs/agents/`) provide **concrete recipes** — the actual `gh` CLI / GraphQL commands. This mirrors how `triage-labels.md` already works for label strings.

**Milestones never appear in global skills.** They're a tracker-specific concept handled entirely at the repo level via lifecycle hooks.

---

## 1. Abstract Operation Vocabulary

These are the tracker-agnostic verbs that global skills reference. Every verb has its recipe defined in `docs/agents/issue-tracker.md`.

| Verb | Purpose | Used by |
|---|---|---|
| `create-issue(title, body, labels)` | Create an issue | to-issues, to-prd |
| `read-issue(id)` | Fetch body, comments, labels | all skills |
| `list-issues(filters)` | List issues by role/state filters | triage, fanout, fanout-triage |
| `edit-issue-body(id, body)` | Update issue body | fanout-review |
| `add-comment(id, body) → comment_id` | Post a comment | triage, fanout, fanout-triage |
| `delete-comment(comment_id)` | Delete a comment | fanout-triage |
| `apply-role(id, role)` | Add a label/role | triage, to-issues, fanout-triage |
| `remove-role(id, role)` | Remove a label/role | triage, fanout-triage |
| `close-issue(id, comment?)` | Close an issue | triage, fanout |
| `set-parent(child_id, parent_id)` | Establish parent/child | to-issues |
| `add-blocked-by(id, blocker_id)` | Mark as blocked by another | to-issues, triage, fanout-triage |
| `remove-blocked-by(id, blocker_id)` | Remove blocking relationship | triage |
| `query-parent(id) → parent_id?` | Get parent issue | triage, fanout |
| `query-children(id) → [id]` | Get child issues | future use |
| `query-blockers(id) → [{id, state}]` | Get blocking issues with state | triage, fanout-triage, promote-unblocked |
| `mark-criterion-complete(id, text)` | Check a checkbox in issue body | fanout-review |
| `resolve-node-id(number) → node_id` | Get tracker-internal ID from number | helper for mutations |

**Not in the vocabulary:** milestone creation, milestone assignment, milestone-scoped queries. These are repo-level lifecycle hooks.

---

## 2. Repo-Level Infrastructure

### 2A. Expanded `docs/agents/issue-tracker.md`

Grows from 23 lines to a full recipe book. Adds:

- **Relationship operations section** — GraphQL mutations for `set-parent` (`addSubIssue`), `add-blocked-by` (`addBlockedBy`), `remove-blocked-by` (`removeBlockedBy`), queries for `query-parent`, `query-children`, `query-blockers`
- **Node ID resolution** — helper recipe since all GraphQL mutations need node IDs
- **Updated "When a skill says..." section** — covering the new verbs

### 2B. New file: `docs/agents/issue-lifecycle.md`

Defines **named lifecycle moments** that global skills trigger, and what tracker-specific actions to perform. Global skills reference the hook name, never the action.

| Hook | Triggered by | This repo's action |
|---|---|---|
| `on:prd-published` | /to-prd after creating PRD | Create milestone with PRD title, assign PRD to it |
| `on:child-issue-created` | /to-issues after creating each child | Inherit parent's milestone |
| `on:batch-discover` | /fanout when gathering issues | Apply user-supplied scope filter (e.g. `--milestone`) |
| `on:issue-triaged-orphan` | /triage when triaging an issue with no parent | Assign to user-chosen or default milestone |
| `on:issue-landed` | /fanout after merging and closing | No extra action (milestone stays for tracking) |

If a repo doesn't have this file, hooks are no-ops.

**Scope for fanout:** The global fanout skill accepts an optional freeform `<scope>` argument and passes it to the `on:batch-discover` hook, which interprets it tracker-specifically. The skill never mentions "milestone."

### 2C. Updated `docs/agents/agent-brief.md`

Remove `## Parent` and `## Blocked by` body sections from the template. Document that relationships are formal tracker relationships managed via `set-parent` and `add-blocked-by` verbs.

---

## 3. Per-Skill Changes

### to-issues/SKILL.md

- **Remove** `## Parent` and `## Blocked by` from the issue body template
- **Add** after creating each issue: call `set-parent(new_issue, source_issue)` if source was an existing issue
- **Add** after creating each issue: for each blocker, call `add-blocked-by(new_issue, blocker_issue)`
- **Add** lifecycle hook: execute `on:child-issue-created` from `docs/agents/issue-lifecycle.md` if it exists
- Issue body template simplifies to: `## What to build` and `## Acceptance criteria`

### to-prd/SKILL.md

- **Add** lifecycle hook: after publishing the PRD, execute `on:prd-published` from `docs/agents/issue-lifecycle.md` if it exists

### triage/SKILL.md

- **Replace** "Query the issue tracker" with "use `list-issues` from `docs/agents/issue-tracker.md`"
- **Replace** "blocked with all blockers closed" bucket: use `query-blockers(id)` instead of parsing `## Blocked by` from body
- **Replace** "note which issues it's waiting on" with "call `add-blocked-by(id, blocker_id)`"
- **Add** "use `query-parent(id)` to understand context" in gather-context step
- **Add** lifecycle hook: for orphan issues, execute `on:issue-triaged-orphan` if it exists

### fanout/SKILL.md

- **Remove** inline `gh issue list --label ...` → "use `list-issues` with the `ready-for-agent` role filter"
- **Remove** inline `gh issue view <N> --comments` → "use `read-issue(id)`"
- **Keep** `gh repo view --json defaultBranchRef` (git operation, not tracker operation)
- **Add** scope filter: "if the user provides a scope filter, pass it to the `on:batch-discover` hook in `docs/agents/issue-lifecycle.md`"
- **Replace** inline close/comment commands → "use `close-issue` and `add-comment`"

### fanout-triage/SKILL.md

- **Remove** inline `gh issue list --label ...` → "use `list-issues`"
- **Remove** inline `gh issue view <N> --comments` → "use `read-issue(id)`"
- **Remove** body mutation to inject `## Blocked by` → call `add-blocked-by(dependent_id, blocker_id)`
- **Remove** inline `gh api -X DELETE` → "use `delete-comment(comment_id)`"
- Veto step simplifies from body-parse-and-mutate to three verb calls

### fanout-review/SKILL.md + AGENT-PROMPT.md

- **Remove** inline `gh issue view <N> --json ...` → "use `read-issue(id)`"
- **Remove** inline checkpoint protocol → "use `mark-criterion-complete(id, criterion_text)`"
- **Add** instruction for sub-agents: "read `docs/agents/issue-tracker.md` for tracker operation recipes"

### triage/AGENT-BRIEF.md (global)

- **Remove** the inline `gh issue list --label needs-triage` example → tracker-agnostic phrasing

---

## 4. Sub-Agent Prompt Composition

Sub-agents run in git worktrees that include all committed repo files, so they can read `docs/agents/issue-tracker.md` directly.

- **fanout-implementer:** No tracker interaction — writes code only. No change needed.
- **fanout-triage sub-agents:** Add to spawn prompt: "Read `docs/agents/issue-tracker.md` for tracker operation recipes."
- **fanout-review sub-agents:** Same — reference the doc instead of inlining commands.
- **Orchestrators** must commit any `docs/agents/` changes before spawning sub-agents.

---

## 5. Migration Strategy

### PR-1: Foundation + Backfill

1. Expand `docs/agents/issue-tracker.md` with relationship recipes
2. Create `docs/agents/issue-lifecycle.md` with hooks
3. Update `docs/agents/agent-brief.md` template
4. Run backfill script: parse existing issues' `## Parent` / `## Blocked by` sections, create formal relationships via GraphQL. **Keep body text intact.**

### PR-2: Cutover

1. Update `.github/workflows/promote-unblocked.yml` to use `query-blockers` (GraphQL) instead of awk-parsing body text
2. Strip `## Parent` / `## Blocked by` from existing issue bodies
3. Update global skills (all 6)

This ordering prevents the window where the workflow sees no blockers before formal relationships exist.

---

## 6. Edge Cases

| Edge case | Approach |
|---|---|
| **Orphan issues** (bugs with no PRD) | `set-parent` is optional. Milestone handled by `on:issue-triaged-orphan` lifecycle hook. |
| **Cross-PRD blocking** | `add-blocked-by` is independent of parent hierarchy — works across milestones. |
| **Single-parent constraint** | Each issue has one parent. Pick the direct parent, mention others in body text. |
| **User-supplied scope for fanout** | Global skill accepts freeform `<scope>`, passes to `on:batch-discover` hook. Repo docs interpret it as `--milestone` for GitHub. |
| **Existing issues without milestones** | Enforced going forward. Triage handles orphans via lifecycle hook. |

---

## 7. Risks

| Risk | Mitigation |
|---|---|
| GraphQL API not available on all plans | Verified working on this repo (bicheichane/Werewolves). Other repos can fall back to body-text recipes. |
| Agent skips recipe lookup | Same pattern as label indirection — proven with `triage-labels.md`. |
| Removing body-text loses at-a-glance visibility | GitHub sub-issue and blocking UI surfaces these in sidebar. |
| `mark-criterion-complete` keys on text | Same fragility as today. Deferrable. |

---

## 8. Implementation Sequence

1. **Repo-level docs** — issue-tracker.md, issue-lifecycle.md, agent-brief.md
2. **Backfill** — formal relationships for existing issues (keep body text)
3. **Workflow update** — promote-unblocked.yml → GraphQL
4. **Body strip** — remove `## Parent` / `## Blocked by` from existing issues
5. **Global skills** — all 6 skills updated to use abstract verbs
6. **Seed template** — update `~/.claude/skills/` files for future repos
