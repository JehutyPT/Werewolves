# Production lobby evaluation stops after safety screening

Production lobby evaluation requests the `DegenerateScreeningOnly` evaluation depth from the Safety-Screening capability. Evaluation depth controls how far one request runs; it is not the versioned capability identity. Production retains the deterministic Already-Decided Role Composition gate and the 1,000-run Degenerate Simulation Scenario gate, then stops before the 10,000-run probability batch.

The Moderator sees actionable already-decided and degenerate warnings only. A successful screening pass, evaluation failure, or simulator-unavailable state does not expose Game Result Frequency, Ended-By-Turn Frequency, retry, or an evaluation panel. Pending screening still closes the Lobby Exit gate; failure and unsupported scenarios release it.

The full probability evaluator, evidence and presentation code, tests, build-time generator, and reviewed cache payloads remain available as a dormant capability rather than being deleted. A trusted existing probability cache record may establish that its scenario previously passed the earlier screening gate, but production does not project its probability payload.

## Considered options

- **Delete probability evaluation and cached payloads**: rejected because the implementation remains useful technical groundwork and the product decision is a reversible shelving, not a domain-model rollback.
- **Hide probability UI only**: rejected because an on-device miss could still execute the 10,000-run batch and probability data could remain reachable through service state.
- **Select an explicit evaluation depth at composition and orchestration boundaries**: accepted because production can prove it never requests the probability phase while dormant full evaluation remains testable.

## Consequences

Production composition supplies one safety-only setting to lobby orchestration, and orchestration passes that depth to the evaluator for every fallback request. Cached probability records project only to a nonblocking screening-passed state. Newly computed screening passes are session-local and are not persisted as fabricated probability records; already-decided and degenerate terminal records retain their existing cache behavior.

The packaged current-profile cache remains unchanged under ADR-0012, so this decision requires no artifact migration or regeneration. Reintroducing Moderator-facing simulator guidance requires a new product decision based on the evidence layers parked in PRD #94, followed by an explicit production capability change.

## Amendment

ADR-0016 gives safety screening and dormant probability the separately versioned `safety-screening@<version>` and `full-probability@<version>` capability identities and Role sets. A legacy `core-simulator@1` probability record may project into the current safety consumer only through the restricted compatibility bridge for a scenario in the intersection of that legacy profile and the current Safety-Screening capability; a newly admitted safety-only Role or changed semantic boundary always requires current safety evidence.
