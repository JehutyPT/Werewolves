# Production cache uses current local records only

Production cache lookup consults only Local Fallback Cache Records stored on the Moderator's device. A record is usable only when its complete identity exactly matches the requested current Simulator Capability, including the scenario and every versioned semantic identity that can change its evidence. A missing, stale, or foreign record is a cache miss and starts bounded on-device evaluation when the capability supports the scenario.

The pre-release `core-simulator@1` producer/profile is obsolete. Production packages no simulator cache and retains no loader, dedicated build-time generator, scenario catalog, generation diagnostics, compatibility projection, alias, migration, or re-keying path for its records. Incompatible pre-release local records are unusable and may be discarded.

The dormant Full-Probability evaluator, evidence, presentation, and tests remain available under the current `full-probability@<version>` capability, including its exact-current Probability records. Those records do not satisfy `safety-screening@<version>`. Safety Screening persists only terminal Already-Decided and Degenerate results; a successful non-degenerate Safety Screening pass remains session-local.

## Considered options

- **Retain the packaged artifact and compatibility bridge**: rejected because backward compatibility is not a product requirement before the first full release, and projected pre-release evidence would make obsolete semantics look current.
- **Regenerate or re-key the artifact for Safety Screening**: rejected because production does not need a distribution artifact and Safety Screening does not persist successful screening passes.
- **Use exact current local records with bounded on-device fallback**: accepted because one identity rule governs lookup and stale-record rejection without a second distribution or migration surface.

## Consequences

Removing the obsolete distribution path does not itself require a terminal-cache schema bump; capability and semantic identities invalidate incompatible records. Any future packaged or remote cache, cross-capability projection, or pre-release migration policy requires a new decision. ADR-0012 is superseded.
