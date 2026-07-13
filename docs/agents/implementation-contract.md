# Implementation Contracts

The canonical Implementation Contract for a work-item issue is its
`## Implementation Contract` section in the issue body. Surrounding body text,
comments, and evidence are context only: none of them, including a newer
structured comment, supersedes that section.

An issue is ready for implementation only when its body states the intended
outcome, behavioral acceptance criteria, scope boundaries, dependency
assumptions, and verification evidence precisely enough that an implementation
agent does not need to choose product behavior.

## Template

```markdown
## Implementation Contract

**Validated against:** <default-branch commit SHA>

### Outcome

Describe the observable behavior that must exist after this issue lands. Include
the current or broken behavior only when it is needed to understand the change.
Name relevant edge cases and error behavior.

### Acceptance criteria

- [ ] Specific, testable behavioral criterion 1.
- [ ] Specific, testable behavioral criterion 2.
- [ ] Specific, testable behavioral criterion 3.

### Scope boundaries

In scope:

- Interface, component, or behavior this issue owns.

Out of scope:

- Adjacent concern that must not be implemented in this issue.
- Deferred enhancement that should land separately.

### Dependency assumptions

- Landed interface, product decision, or predecessor behavior this contract
  assumes. Cite an ADR when one governs the assumption.
- State explicitly when there are no additional dependency assumptions.

### Verification

- **Claim:** Observable behavior the tests or checks must prove.
  **Preferred evidence:** Test surface and artifact from
  `docs/agents/qa-strategy.md`.
  **Forbidden evidence:** Evidence that would couple the check to incidental
  implementation shape.
  **Source-test allowlist needed:** yes/no; if yes, cite or add the matching
  allowlist row.
```

Repeat the four Verification fields for each distinct evidence surface. The
repository's claim-first QA gate remains authoritative; a contract must not
replace those fields with a generic "tests pass" criterion.

Describe stable behavior, interfaces, and decisions. Omit file paths and line
numbers unless the location itself is part of the contract.

## Freshness Anchor

A prepared contract records `**Validated against:** <default-branch commit
SHA>`.
The anchor identifies the landed-code baseline used to validate the contract,
its interfaces, its assumptions, and its QA choices. Use the actual default
branch commit, not a feature-branch tip.

A blocked contract is provisional because predecessor work or product decisions
can change its assumptions. It may record
`**Drafted against:** <default-branch commit SHA>` until `prepare-ticket`
refreshes the body and replaces that line with `Validated against` after the
open blockers are resolved.

A newer default-branch tip alone is not material drift and does not make every
prepared contract stale. The anchor is a cheap guard: inspect landed changes
since that commit for behavior, interfaces, decisions, or evidence constraints
that matter to this contract. Refresh the body and anchor only when relevant
drift exists; then revalidate before admitting the issue to execution.

## Preparation And Admission

Apply `ready-for-agent` only after all of the following are true:

- The issue is open and has no open native `blockedBy` relationships.
- The body follows this contract and has been validated against landed code.
- Acceptance criteria are behavioral and testable.
- Dependency assumptions match the landed predecessors and current ADRs.
- No product decision required for implementation remains unresolved.
- Verification records Claim, Preferred evidence, Forbidden evidence, and
  Source-test allowlist needed for every test surface.
- The issue has been deliberately admitted to the execution frontier.

`to-tickets` creates the issues, bodies, and formal relationships first. It may
prepare the initial unblocked frontier and apply `ready-for-agent` to those
issues as its final mutation. Blocked descendants keep provisional contracts
without readiness. `prepare-ticket` refreshes a newly unblocked contract against
landed code and, when it passes every gate, applies `ready-for-agent` last.

Adding the label is admission, not an automatic consequence of becoming
unblocked. Closing or removing the last blocker never adds it.

## Repository Conventions

- **All user-facing UI text must be in Portuguese.** State this explicitly in
  Outcome or Acceptance criteria when a slice adds visible UI.
- **Cite ADRs.** If `docs/adr/` covers an affected area, cite the decision in
  Dependency assumptions or Scope boundaries.
- **Use the domain glossary.** Phrase contracts in `CONTEXT.md` terminology:
  Faction, not Team; Moderator, not GM; Phase, not Round.
- **Use repository categories.** Work items use `feature`, `bug`,
  `architecture`, or `spike`; a skill's `enhancement` category maps to
  `feature` here.

## Relationships

Parent and blocker relationships are native tracker state, not body-text lists.
Manage them with the relationship wrappers in `docs/agents/issue-tracker.md`.
Dependency assumptions explain what the contract relies on and should cite the
upstream issue or merged PR when that provenance helps preparation. They do not
replace formal edges or mirror whether an edge is currently open or closed.

Open blockers gate readiness. Closed blocker relationships remain attached as
durable provenance: closing a blocker must not delete the edge. Remove a native
relationship only when the dependency itself was recorded incorrectly or is no
longer real. If a retained blocker reopens, it naturally becomes an open gate
again.

Adding a blocker removes `ready-for-agent`. Closing or explicitly removing the
last blocker does not restore it; preparation must validate the body against the
landed result and add the label last. The repository workflow audits these
invariants, removes invalid readiness, preserves every relationship, and never
promotes an issue.

## Body Changes And Progress

Any generic issue-body edit invalidates `ready-for-agent`, because the canonical
contract may have changed. Prepare and validate the edited contract before
adding the label again.

The criterion checkbox wrappers are the narrow exception. Marking or unmarking
an existing Acceptance criterion records implementation progress only; it does
not rewrite the contract and therefore does not invalidate readiness. Do not use
those wrappers to alter criterion text or any other body content.

## When A Contract Is Not Ready

Leave the issue open with its category label and without `ready-for-agent` when:

- a native blocker is open;
- the contract is provisional or materially stale against landed code;
- a product or interface decision remains unresolved;
- an acceptance criterion says only that something "works correctly";
- the body offers mutually exclusive implementation outcomes; or
- the issue bundles concerns that cannot land as one coherent change.

Refine or split the work, preserve its formal relationships, and run preparation
again. Do not apply a negative readiness-state label.
