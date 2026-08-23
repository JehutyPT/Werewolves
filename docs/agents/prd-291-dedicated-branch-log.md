# PRD #291 dedicated-branch execution log

This log records the user-directed lifecycle exception for executing PRD #291
on one dedicated branch and delivering it through one final PR.

## Branch policy

- PRD #291 and all child-issue work land only on `codex/prd-291` until the
  user reviews and merges the final pull request. These commits must not be
  described as merged to `main` before that merge occurs.
- For this PRD only, a child is considered landed for tracker-progression
  purposes after its exact commit is integrated, centrally verified, and
  reviewed on `codex/prd-291`. Closing a completed child therefore means
  "verified on the dedicated PRD branch," not "merged to main."
- Formal parent and blocker relationships are retained after child closure as
  dependency provenance. Closing a blocker changes its state but does not
  remove its relationship edges.
- Preparation, validation, and drift checks for subsequent PRD #291 children
  use the evolving `codex/prd-291` tip as the integrated predecessor baseline
  under this scoped exception. Canonical freshness anchors still name the
  actual default-branch commit required by repository policy; the exception
  must not pretend dedicated-branch commits are present on the default branch.
- This exception applies only to PRD #291 and does not change the repository's
  general issue-readiness, implementation-contract, or relationship policies.

## Integrated child work

### 2026-08-23 — issue #296

- Integrated, verified, and reviewed post-session Lobby continuity through
  commits `ab4690c9e8afab7c268429b56cf05d4bdb9115ba` and
  `75fccd518a2deccfe34c924cf536f276282e1046`.

### 2026-08-23 — issue #297

- Integrated, verified, and reviewed explicit Landing navigation through
  commit `b647de51491e9c76178720092fe4e5d476f908c0`.

### 2026-08-23 — issue #298

- Integrated, verified, and reviewed scoped Roster and Role Selection resets
  through commit `b32952b53823b1bf6cc314f01a952eb714883207`.

### 2026-08-23 — issue #299

- Integrated, verified, and reviewed recent-setup capture and reuse through
  commit `9692869756119069d28eb9b1fc615dcda46897c5`.
- Closed issue #299 as verified on `codex/prd-291` under this dedicated-branch
  exception, without representing the commit as merged to `main`.

## Final child set and pull request

- The completed child set is #296, #297, #298, and #299, at the exact commits
  recorded above. Each child is closed as integrated, verified, and reviewed
  on `codex/prd-291`; the retained formal relationships remain authoritative.
- The one final pull request from `codex/prd-291` into
  `codex/role-workflow-workstream` is pending user review. None of the
  dedicated-branch commits recorded here are claimed to be present on
  `main`.
