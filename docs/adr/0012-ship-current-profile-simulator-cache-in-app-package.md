# Ship the current-profile simulator cache in the app package

The measured `core-simulator@1` Bundled Simulator Cache contains 1,664 terminal lobby evaluations and is 2,337,001 canonical bytes. Ship this current-profile artifact as one root MAUI asset inside the app package so cache-first lobby evaluation is deterministic and available without a network dependency; do not add a remote manifest, downloader, or update protocol for this delivery.

This resolves ADR-0009's distribution deferral only for the current simulator profile. A future expanded or full-role profile may choose a different distribution mechanism after its realistic artifact size, update cadence, and operating constraints are measured.

## Considered options

- **Bundled current-profile artifact**: accepted because the measured package cost is bounded, the app already requires offline Moderator operation, and artifact bytes can be generated, reviewed, and versioned with the simulator/cache schema.
- **Static remote artifact**: rejected for the current profile because it would add availability, version-negotiation, update, and fallback surfaces without improving the required cache-first flow.

## Consequences

The app package carries the reviewed 2,337,001-byte artifact and ordinary builds package it without regenerating it. Changing those bytes requires explicit Build-Time Cache Generation and integrity review. On-device generation remains a bounded fallback for a missing or unusable compatible record, not a replacement distribution channel.
