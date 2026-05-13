# Triage Labels

This file maps skill-canonical roles to the actual label strings used in this repo's issue tracker. Labels are organised into three groups: **category**, **state**, and **document**.

## State roles

| Label in mattpocock/skills | Label in our tracker | Meaning                                  |
| -------------------------- | -------------------- | ---------------------------------------- |
| `needs-triage`             | `needs-triage`       | Maintainer needs to evaluate this issue  |
| `needs-info`               | `needs-info`         | Waiting on reporter for more information |
| `ready-for-agent`          | `ready-for-agent`    | Fully specified, ready for an AFK agent  |
| `ready-for-human`          | `ready-for-human`    | Requires human implementation            |
| `blocked`                  | `blocked`            | Specified but waiting on prerequisite    |
| `wontfix`                  | `wontfix`            | Will not be actioned                     |

## Category roles

Every work-item issue gets exactly one category label. Skills that mention `enhancement` should map to `feature` or `architecture` depending on the nature of the work.

| Label in mattpocock/skills | Label in our tracker | Meaning                                              |
| -------------------------- | -------------------- | ---------------------------------------------------- |
| `bug`                      | `bug`                | Something is broken                                  |
| `enhancement`              | `feature`            | New feature or capability                            |
| —                          | `architecture`       | Structural improvement, refactoring, or codebase health |
| —                          | `spike`              | Time-boxed investigation or feasibility validation   |

**When to use `feature`**: New capabilities, enhancements to existing behaviour, or any work that delivers user-facing value.

**When to use `architecture`**: Issues raised by the `improve-codebase-architecture` skill, or any refactor / tech-debt / structural change that doesn't add user-facing functionality.

**When to use `spike`**: Time-boxed exploration to answer a specific question or validate feasibility before committing to implementation.

## Document roles

Not state or category roles — these replace category labels for non-work-item issues:

| Label in mattpocock/skills | Label in our tracker | Meaning                                  |
| -------------------------- | -------------------- | ---------------------------------------- |
| `prd`                      | `prd`                | Product requirements document            |

PRDs only use `needs-triage` or no state label. Never apply `ready-for-agent`, `ready-for-human`, or `blocked` to a PRD.

When a skill mentions a role (e.g. "apply the AFK-ready triage label"), use the corresponding label string from this table.

Edit the right-hand column to match whatever vocabulary you actually use.
