# Safety screening is a separate simulator capability

Role support v1 admits Roles to production lobby safety screening without admitting them to dormant full-probability evaluation. Safety screening and full probability evaluation therefore use the separately versioned capability identities `safety-screening@<version>` and `full-probability@<version>`, each with an explicit Role set, supported setup artifacts, and headless-response policy. `DegenerateScreeningOnly` and `FullProbability` remain request evaluation depths; they are not capability identities. Scenario support, cache lookup, and stale-record rejection are evaluated for the named capability. Because full probability evaluation passes through the same earlier safety gates, the Full-Probability Role Set is a subset of the Safety-Screening Role Set; the reverse is deliberately false.

Safety-screening support means deterministic Already-Decided classification, legal seeded Simulation Start State derivation, legal headless responses for every reachable instruction, and complete 1,000-run Degenerate Simulation Scenario evidence. Any incomplete run produces Could Not Evaluate. Already-Decided and Degenerate results may persist as compact local terminal records; a successful non-degenerate screening pass remains session-local. A terminal record is usable only when its complete identity exactly matches the requested current capability. The pre-release `core-simulator@1` producer/profile is unsupported: its records are stale, are not re-keyed or projected into another capability, and are treated as cache misses.

## Consequences

Role admission, setup defaults, Role or outcome semantics, and baseline response-policy changes update safety compatibility identity when they can change screening evidence. Decision Strategy identity changes with response policy; until that identity participates in cache keys, safety compatibility must change with it. Cache schema versions change only for their own wire semantics. Safety may persist only compact local Already-Decided or Degenerate terminal records and never requires a 10,000-run probability batch.

This decision amends ADR-0008's unqualified simulator-support gate and ADR-0013's broad probability-record projection. Those rules now operate through the exact named capability. ADR-0018 supersedes ADR-0012 and removes the packaged cache and pre-release compatibility bridge.

## Amendment: ADR-0017

[ADR-0017](./0017-thief-offer-is-committed-before-the-physical-deal.md) adds the committed Deal Pool/Thief Offer partition, legal choice branches, and exchange semantics to the safety-screening compatibility identity. Changes to those semantics require fresh current-capability evidence.
