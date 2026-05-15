# Issue Lifecycle Hooks

Lifecycle hooks are named moments that global skills trigger after specific actions. The skill references the hook name; this file defines what tracker-specific actions to perform. If a repo has no `docs/agents/issue-lifecycle.md`, all hooks are no-ops.

Milestone operations use wrapper scripts in `docs/agents/scripts/`.

## Hooks

### `on:prd-published`

**Triggered by:** `/to-prd` after creating the PRD issue.

**Actions:**
1. Create a milestone named after the PRD title: `docs/agents/scripts/create-milestone.sh "<PRD title>"` (prints the milestone number).
2. Assign the PRD issue to that milestone: `docs/agents/scripts/assign-milestone.sh <prd_number> "<PRD title>"`.

### `on:child-issue-created`

**Triggered by:** `/to-issues` after creating each child issue.

**Actions:**
1. Read the parent issue's milestone: `docs/agents/scripts/read-issue.sh <parent_number>` and extract the milestone title from the JSON output.
2. If the parent has a milestone, assign the same milestone to the child: `docs/agents/scripts/assign-milestone.sh <child_number> "<milestone_title>"`. Skip silently if the parent has no milestone.

### `on:batch-discover`

**Triggered by:** `/fanout-implement` when gathering issues to process.

**Actions:**
1. Apply the user-supplied scope filter to narrow the issue set.

The global fanout-implement skill accepts an optional freeform `<scope>` argument. In this repo, scope is interpreted as a milestone name and passed as `--milestone` to the list command.

- Without scope: `docs/agents/scripts/list-issues.sh --label "ready-for-agent" --state open`
- With scope: `docs/agents/scripts/list-issues.sh --label "ready-for-agent" --state open --milestone "<scope>"`

If the user provides no scope, no filtering is applied beyond the role label.

### `on:issue-triaged-orphan`

**Triggered by:** `/triage` when triaging an issue that has no parent (standalone bug, ad-hoc task).

**Actions:**
1. Assign the issue to the user-chosen milestone, or to the default milestone if none is specified: `docs/agents/scripts/assign-milestone.sh <number> "<milestone_name>"`.

If no milestone preference is available, skip silently. Do not block triage on milestone assignment.

### `on:issue-landed`

**Triggered by:** The user (or a downstream skill) after merging a fanout branch and closing an issue.

**Actions:** None. The milestone stays on the issue for historical tracking. No extra action required.
