# Production lobby evaluation stops after safety screening

Production lobby evaluation requests the `DegenerateScreeningOnly` evaluation depth from the Safety-Screening capability. Evaluation depth controls how far one request runs; it is not the versioned capability identity. Production retains the deterministic Already-Decided Role Composition gate and the 1,000-run Degenerate Simulation Scenario gate, then stops before the 10,000-run probability batch.

The Moderator sees actionable already-decided and degenerate warnings only. A successful screening pass, evaluation failure, or simulator-unavailable state does not expose Game Result Frequency, Ended-By-Turn Frequency, retry, or an evaluation panel. Pending screening still closes the Lobby Exit gate; failure and unsupported scenarios release it.

The full probability evaluator, evidence and presentation code, and tests remain available as a dormant capability rather than being deleted. The pre-release build-time generator and packaged cache payload are not retained. Production accepts a terminal cache record only when its complete identity exactly matches the requested current capability; a Full-Probability or `core-simulator@1` record never establishes Safety Screening.

## Considered options

- **Delete dormant probability evaluation**: rejected because the implementation remains useful technical groundwork and the product decision is a reversible shelving, not a domain-model rollback.
- **Hide probability UI only**: rejected because an on-device miss could still execute the 10,000-run batch and probability data could remain reachable through service state.
- **Project pre-release probability records into safety screening**: rejected because pre-release compatibility is not a product requirement and cross-capability projection would make stale evidence appear current.
- **Select an explicit evaluation depth at composition and orchestration boundaries**: accepted because production can prove it never requests the probability phase while dormant full evaluation remains testable.

## Consequences

Production composition supplies one safety-only setting to lobby orchestration, and orchestration passes that depth to the evaluator for every fallback request. Only exact-identity current local records may satisfy cache lookup. Newly computed screening passes are session-local and are not persisted as fabricated probability records; already-decided and degenerate terminal records retain their local cache behavior.

Production packages no simulator cache and performs no cache migration or regeneration. Reintroducing Moderator-facing simulator guidance requires a new product decision based on the evidence layers parked in PRD #94, followed by an explicit production capability change.

## Amendment

ADR-0016 gives safety screening and dormant probability the separately versioned `safety-screening@<version>` and `full-probability@<version>` capability identities and Role sets. ADR-0018 requires exact current capability identity for every local cache hit and removes the packaged `core-simulator@1` artifact and compatibility bridge. A newly admitted Role or changed semantic boundary always requires fresh current-capability evidence.

ADR-0017 changes the target Thief scenario identity and screening request. Once implemented, one production request screens Offer 1, Offer 2, and every legal Decline branch after the partition and conditional setup are committed. Any Degenerate branch blocks. Otherwise, incomplete/error/timeout branches produce nonblocking Could Not Evaluate and only all-complete non-degenerate branches pass. Thief records require fresh current-capability evidence for these changed semantics.
