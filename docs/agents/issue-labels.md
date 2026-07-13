# Issue Labels And Readiness

This repository uses labels to describe an issue's kind and to admit prepared
work to agent execution. Labels are not a triage state machine.

## Readiness

`ready-for-agent` is the only implementation-readiness label. It means all of
the following are currently true:

- the work-item issue is open;
- it has no open native blockers;
- its canonical issue-body Implementation Contract satisfies
  `docs/agents/implementation-contract.md`;
- the contract has been validated against landed code and has no relevant
  material drift;
- no product decision needed for implementation remains unresolved; and
- the issue has been deliberately admitted to the execution frontier.

The label is both a preparation result and an admission decision. It is not a
promise that a previously blocked draft will become ready automatically.

An open issue with only its category label is captured work in the refinement
backlog. Refine, split, unblock, or prepare it until every readiness condition is
true, then add `ready-for-agent` as the final tracker mutation. Do not use a
negative readiness-state label.

## Lifecycle

Create a work item with its category label, write its canonical body, and
establish parent and blocker relationships before considering readiness.
`create-issue.sh` refuses `ready-for-agent` because the body and relationship
graph must exist first.

Blocked contracts are provisional and do not carry readiness. `to-tickets` may
prepare and label the initial unblocked frontier only after all issues and edges
exist, applying `ready-for-agent` last. When an issue becomes unblocked,
`prepare-ticket` refreshes its contract against landed predecessor code,
resolves any product questions, and applies the label last only if every gate
passes.

Preparation records `Validated against: <default-branch commit SHA>` in the
body. A provisional blocked contract may use `Drafted against` until it is
refreshed. A newer default-branch tip alone is not material drift; the anchor
bounds the cheap comparison for contract-relevant changes.

## Invalidation Rules

- Adding a native blocker removes `ready-for-agent` after the relationship is
  created.
- A generic issue-body edit removes `ready-for-agent`, because the body is the
  canonical contract.
- Marking or unmarking an existing item in the canonical `### Acceptance
  criteria` section through the criterion-state wrappers is progress-only and
  does not remove readiness.
- Closing an issue removes readiness.
- Closing or removing the last blocker never adds readiness.
- Closed blocker edges remain as durable provenance. If a retained blocker is
  reopened, it naturally becomes an open blocker again and gates the issue.
- The invariant-audit workflow removes invalid readiness after material body
  edits, when a blocker closes, from closed issues, and from open issues with
  open blockers. It paginates retained relationships, preserves checkbox-only
  progress edits, retains every edge, and never adds readiness.

Comments are discussion or evidence only. Adding, editing, or superseding a
comment never changes the canonical Implementation Contract.

## Category Labels

Every work-item issue gets exactly one category label. Skills that mention
`enhancement` map it to `feature` or `architecture` depending on the nature of
the work.

| Skill vocabulary | Label in our tracker | Meaning |
| --- | --- | --- |
| `bug` | `bug` | Something is broken |
| `enhancement` | `feature` | New feature or capability |
| — | `architecture` | Structural improvement, refactoring, or codebase health |
| — | `spike` | Time-boxed investigation or feasibility validation |

Use `feature` for new capabilities, enhancements to existing behavior, or work
that delivers user-facing value.

Use `architecture` for issues raised by the
`improve-codebase-architecture` skill, refactors, technical debt, or structural
changes that do not add user-facing behavior.

Use `spike` for time-boxed exploration that answers a specific question or
validates feasibility before implementation is contracted.

## Document Labels

`prd` identifies a product requirements document. It replaces the category
label because a PRD is planning material, not an implementable work item. Every
PRD uses `prd` and never uses `ready-for-agent`; implementation belongs in child
or related work-item issues.
