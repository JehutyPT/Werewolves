# Issue Tracker: GitHub

Issues and PRDs for this repository live as GitHub issues on
`bicheichane/Werewolves`. Run the wrappers in this clone; the `gh` CLI infers
the repository from `git remote`.

Read `docs/agents/issue-labels.md` for the readiness lifecycle and
`docs/agents/implementation-contract.md` for the canonical
`## Implementation Contract` section. Surrounding body text and comments are
discussion, metadata, or evidence only.

## Readiness Protocol

For a new work item:

1. Create the issue with its category label, never `ready-for-agent`.
2. Put the canonical `## Implementation Contract` section in its body.
3. Establish every parent and blocker relationship.
4. If any blocker is open, leave the contract provisional and do not add
   readiness.
5. For an unblocked issue, validate the body against landed code, record its
   `Validated against` default-branch commit, resolve product decisions, and
   add `ready-for-agent` last.

`to-tickets` may admit the initial unblocked frontier this way after publishing
the complete graph. `prepare-ticket` uses the same final preparation sequence
when a provisional issue becomes unblocked. A newer branch tip is not by itself
material drift; use the freshness anchor to inspect only relevant landed
changes.

## CRUD Operations

All CRUD operations use wrappers in `docs/agents/scripts/`.

| Operation | Command |
| --- | --- |
| Create issue | `docs/agents/scripts/create-issue.sh <title> <body> [label...]` |
| Read issue | `docs/agents/scripts/read-issue.sh <number>` |
| List issues | `docs/agents/scripts/list-issues.sh [--label <name>] [--state <state>] [--milestone <name>] [--limit <n>]` |
| Edit issue body | `docs/agents/scripts/edit-issue-body.sh <number> <body>` |
| Add comment | `docs/agents/scripts/add-comment.sh <number> <body>` |
| Delete comment | `docs/agents/scripts/delete-comment.sh <comment_id>` |
| Add label | `docs/agents/scripts/add-label.sh <number> <label>` |
| Remove label | `docs/agents/scripts/remove-label.sh <number> <label>` |
| Close issue | `docs/agents/scripts/close-issue.sh <number> [comment]` |

Behavioral notes:

- `create-issue.sh` prints the new issue URL; its number is the last path
  segment. It refuses `ready-for-agent` at creation so the issue body and
  relationships can be established first.
- `read-issue.sh` returns the canonical body, discussion comments, timestamps,
  and closing pull-request references so preparation can inspect what actually
  landed.
- `edit-issue-body.sh` replaces the canonical contract and invalidates
  `ready-for-agent` in the same edit. Prepare the new body before restoring the
  label.
- `add-comment.sh` prints the numeric comment ID for `delete-comment.sh`.
  Comments do not update or supersede the contract.
- `add-label.sh` passes other labels through unchanged. For `ready-for-agent`,
  it refuses closed issues and issues with any open blocker. The caller remains
  responsible for the semantic contract-validation and admission gates.
- `list-issues.sh` forwards its arguments to `gh issue list`. Use `--limit 200`
  when many results are expected. Use `--search "no:blocked-by"` to exclude
  issues with open blocking relationships.

## Relationship Operations

Relationships are native tracker state. They must not be duplicated as parent
or blocker lists in the issue body.

| Operation | Command |
| --- | --- |
| Set parent | `docs/agents/scripts/set-parent.sh <child> <parent>` |
| Remove parent | `docs/agents/scripts/remove-parent.sh <child> <parent>` |
| Add blocker | `docs/agents/scripts/add-blocked-by.sh <issue> <blocker>` |
| Remove blocker | `docs/agents/scripts/remove-blocked-by.sh <issue> <blocker>` |

`add-blocked-by.sh` creates the relationship before removing
`ready-for-agent`, if present. `remove-blocked-by.sh` never adds readiness and
directs the operator back through preparation.

Closed blocker relationships are durable provenance. Do not call
`remove-blocked-by.sh` merely because the blocker closed. Remove an edge only
when the dependency was recorded incorrectly or is no longer a real dependency.
If a retained blocker reopens, it naturally gates the dependent issue again.

## Relationship Queries

| Query | Command | Output |
| --- | --- | --- |
| Get parent | `docs/agents/scripts/query-parent.sh <issue>` | Parent number, or empty |
| Get children | `docs/agents/scripts/query-children.sh <issue>` | Child numbers, one per line |
| Get blockers | `docs/agents/scripts/query-blockers.sh <issue>` | `<number> <state>` per line, including closed blockers |
| Get blocking | `docs/agents/scripts/query-blocking.sh <issue>` | `<number> <state>` per line |

Only blockers whose state is `OPEN` gate readiness.

## Body Progress Operations

| Operation | Command |
| --- | --- |
| Mark criterion complete | `docs/agents/scripts/mark-criterion-complete.sh <issue> "<criterion_text>"` |
| Unmark criterion complete | `docs/agents/scripts/unmark-criterion-complete.sh <issue> "<criterion_text>"` |

Pass criterion text without the `- [ ]` or `- [x]` marker. The scripts match
exactly one Markdown task-list item, normalize whitespace for wrapped criteria,
and fail rather than editing an ambiguous match.

These two wrappers are progress-only exceptions to body-edit invalidation. They
toggle an existing checkbox without changing contract text, so they preserve
`ready-for-agent`. Use `edit-issue-body.sh` for every other body change.

## Invariant Audit

`.github/workflows/maintain-issue-relationships.yml` is a readiness invariant
audit. It removes `ready-for-agent` from closed issues and, on scheduled or
manual sweeps, from open ready issues that have an open blocker. It retains all
native relationships and never promotes an issue.

Ordinary GitHub Actions issue events do not cover native dependency changes.
The relationship wrappers enforce invariants immediately for normal repository
operations; the scheduled/manual sweep catches relationships changed outside
the wrappers and blockers that reopen.

Closing or removing the last blocker never adds readiness. Run preparation
against the landed predecessor result and add the label last only when the
contract still passes every gate.

## When A Skill Says

| Skill phrase | Script |
| --- | --- |
| "publish to the issue tracker" | `docs/agents/scripts/create-issue.sh` |
| "fetch the relevant ticket" | `docs/agents/scripts/read-issue.sh` |
| "query the issue tracker" / "list issues" | `docs/agents/scripts/list-issues.sh` |
| "update the issue body" | `docs/agents/scripts/edit-issue-body.sh` |
| "post a comment" | `docs/agents/scripts/add-comment.sh` |
| "delete a comment" | `docs/agents/scripts/delete-comment.sh` |
| "add/apply label" | `docs/agents/scripts/add-label.sh` |
| "remove label" | `docs/agents/scripts/remove-label.sh` |
| "close the issue" | `docs/agents/scripts/close-issue.sh` |
| "set parent" / "establish parent" | `docs/agents/scripts/set-parent.sh` |
| "add blocker" / "mark as blocked by" | `docs/agents/scripts/add-blocked-by.sh` |
| "remove blocker" / "unblock" | `docs/agents/scripts/remove-blocked-by.sh` |
| "remove parent" | `docs/agents/scripts/remove-parent.sh` |
| "get parent" / "find parent issue" | `docs/agents/scripts/query-parent.sh` |
| "get children" / "list sub-issues" | `docs/agents/scripts/query-children.sh` |
| "get blockers" / "check blockers" | `docs/agents/scripts/query-blockers.sh` |
| "get blocking" | `docs/agents/scripts/query-blocking.sh` |
| "check criterion" / "mark criterion complete" | `docs/agents/scripts/mark-criterion-complete.sh` |
| "uncheck criterion" / "unmark criterion" | `docs/agents/scripts/unmark-criterion-complete.sh` |
