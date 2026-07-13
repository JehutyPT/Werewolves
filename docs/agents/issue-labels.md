# Issue Labels and Readiness

This repository uses labels to describe an issue's kind and to opt implementation work into agent discovery. Labels are not a triage state machine.

## Readiness

`ready-for-agent` is the only implementation-readiness label. Apply it only when a work-item issue contains a complete implementation contract as described in `docs/agents/agent-brief.md`.

An open work-item issue with only a category label is captured work in the refinement backlog. It does not need a negative readiness label. Refine or split it until it is ready, then add `ready-for-agent`.

Readiness and blocking are independent:

- `ready-for-agent` means the issue is sufficiently specified for implementation.
- Formal `blockedBy` relationships determine whether that ready issue is currently executable.
- A ready issue is executable when it is open and has no open blockers.
- Adding or removing a blocker does not add or remove `ready-for-agent`.
- Closing an issue removes `ready-for-agent`; work that will not be actioned should be closed as not planned rather than moved to another label state.

## Category labels

Every work-item issue gets exactly one category label. Skills that mention `enhancement` should map to `feature` or `architecture` depending on the nature of the work.

| Skill vocabulary | Label in our tracker | Meaning                                              |
| ---------------- | -------------------- | ---------------------------------------------------- |
| `bug`                      | `bug`                | Something is broken                                  |
| `enhancement`              | `feature`            | New feature or capability                            |
| —                          | `architecture`       | Structural improvement, refactoring, or codebase health |
| —                          | `spike`              | Time-boxed investigation or feasibility validation   |

**When to use `feature`**: New capabilities, enhancements to existing behaviour, or any work that delivers user-facing value.

**When to use `architecture`**: Issues raised by the `improve-codebase-architecture` skill, or any refactor / tech-debt / structural change that doesn't add user-facing functionality.

**When to use `spike`**: Time-boxed exploration to answer a specific question or validate feasibility before committing to implementation.

## Document labels

`prd` identifies a product requirements document. It replaces the category label because a PRD is a planning document, not an implementable work item. Every PRD uses `prd` and never uses `ready-for-agent`; implementation work must be represented by child or related work-item issues.
