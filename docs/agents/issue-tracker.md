# Issue tracker: GitHub

Issues and PRDs for this repo live as GitHub issues on `bicheichane/Werewolves`. Use the `gh` CLI for all operations. The `gh` CLI infers the repo from `git remote` automatically when run inside a clone.

Label and readiness conventions are defined in `docs/agents/issue-labels.md`. In particular, `ready-for-agent` is the sole positive readiness signal; formal blocker relationships determine whether ready work is currently executable.

## CRUD Operations

All CRUD operations use wrapper scripts in `docs/agents/scripts/`.

| Operation | Command |
|---|---|
| Create issue | `docs/agents/scripts/create-issue.sh <title> <body> [label...]` |
| Read issue | `docs/agents/scripts/read-issue.sh <number>` |
| List issues | `docs/agents/scripts/list-issues.sh [--label <name>] [--state <state>] [--milestone <name>] [--limit <n>]` |
| Edit issue body | `docs/agents/scripts/edit-issue-body.sh <number> <body>` |
| Add comment | `docs/agents/scripts/add-comment.sh <number> <body>` |
| Delete comment | `docs/agents/scripts/delete-comment.sh <comment_id>` |
| Add label | `docs/agents/scripts/add-label.sh <number> <label>` |
| Remove label | `docs/agents/scripts/remove-label.sh <number> <label>` |
| Close issue | `docs/agents/scripts/close-issue.sh <number> [comment]` |

**Notes:**

- `create-issue.sh` prints the new issue URL. Extract the number from the last path segment.
- `add-comment.sh` prints the numeric comment ID to stdout, for use with `delete-comment.sh`.
- `add-label.sh` and `remove-label.sh` pass the label string directly to GitHub. Use `docs/agents/issue-labels.md` for this repository's vocabulary.
- `list-issues.sh` forwards all arguments to `gh issue list`. Use `--limit 200` when you expect many results. Use `--search "no:blocked-by"` to exclude issues with open blocking relationships.
- `close-issue.sh` accepts an optional comment; omit it if no message is needed.

## Relationship Operations

These use wrapper scripts in `docs/agents/scripts/`. Each script accepts issue numbers (not node IDs) and resolves node IDs internally.

| Operation | Command |
|---|---|
| Set parent | `docs/agents/scripts/set-parent.sh <child> <parent>` |
| Remove parent | `docs/agents/scripts/remove-parent.sh <child> <parent>` |
| Add blocker | `docs/agents/scripts/add-blocked-by.sh <issue> <blocker>` |
| Remove blocker | `docs/agents/scripts/remove-blocked-by.sh <issue> <blocker>` |

## Relationship Queries

| Query | Command | Output |
|---|---|---|
| Get parent | `docs/agents/scripts/query-parent.sh <issue>` | Parent number, or empty |
| Get children | `docs/agents/scripts/query-children.sh <issue>` | Child numbers, one per line |
| Get blockers | `docs/agents/scripts/query-blockers.sh <issue>` | `<number> <state>` per line |
| Get blocking | `docs/agents/scripts/query-blocking.sh <issue>` | `<number> <state>` per line |

## Body-Text Operations

| Operation | Command |
|---|---|
| Mark criterion complete | `docs/agents/scripts/mark-criterion-complete.sh <issue> "<criterion_text>"` |
| Unmark criterion complete | `docs/agents/scripts/unmark-criterion-complete.sh <issue> "<criterion_text>"` |

Pass the criterion text without the `- [ ]` / `- [x]` checkbox marker. The
scripts match exactly one Markdown task-list item, support wrapped criteria by
normalizing whitespace, and fail instead of editing when the text is ambiguous.

## When a Skill Says...

| Skill phrase | Script |
|---|---|
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
