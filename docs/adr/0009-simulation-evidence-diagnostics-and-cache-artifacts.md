# Simulation evidence is stable, diagnostics are not cache artifacts

Simulation runs and batches produce stable Simulation Result Evidence. Diagnostic material such as full transcripts, final Player/Faction snapshots, exception details, instruction counts, timing, memory, raw engine traces, and driver limits can be useful during development, but it is not part of the stable evidence contract.

Stable per-run evidence is intentionally minimal: run identity, Run Seed Material, completion state, and, for Completed Simulation Runs, the Game Session Outcome, ending Turn, and ending Victory Check Window. Simulation execution first returns Simulation Batch Source Evidence containing execution identity, ordered per-attempt source records, and Completed and Incomplete Simulation Run counts. This source evidence is a precursor from which downstream terminal evaluation may compose complete batch-level Simulation Result Evidence with the information needed to derive screening and probability views, including Possible Faction and Possible Game Result inventories, completed outcomes by Game Result, completed outcomes by ending Victory Check Window, and ending Turn.

Terminal cache records are compact lobby evaluations derived from stable evidence. They do not carry per-run source records or replay material. Run Seed Material remains in evidence and replay/audit workflows; terminal records carry only the derived lobby result needed by the app.

This ADR originally deferred the distribution mechanism for cache records. ADR-0018 now decides that production packages no simulator cache: it accepts only exact-identity local terminal records and otherwise performs bounded on-device evaluation. Any future packaged or remote distribution mechanism requires a new decision.

## Considered options

- **Full trace as stable evidence**: make transcripts, final state snapshots, timings, and driver diagnostics part of the result contract. Rejected because it couples long-lived evidence to implementation internals and creates a brittle versioning surface.
- **Aggregate probabilities only**: store just the final percentage rows. Rejected because screening, replay, QA, and zero-frequency Possible Game Result handling require more source evidence than final display rows.
- **App-facing terminal records carry per-run records**: put replay evidence directly in local cache entries. Rejected because app lookup needs compact lobby evaluations, while replay and audit workflows can use separate evidence artifacts.
- **Stable minimal evidence plus derived terminal records**: keep enough source evidence for replayable interpretation, exclude diagnostics from the durable contract, and derive compact terminal records for app use. Accepted.
