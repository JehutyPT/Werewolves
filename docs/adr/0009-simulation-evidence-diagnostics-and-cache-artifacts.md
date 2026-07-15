# Simulation evidence is stable, diagnostics are not cache artifacts

Simulation runs and batches produce stable Simulation Result Evidence. Diagnostic material such as full transcripts, final Player/Faction snapshots, exception details, instruction counts, timing, memory, raw engine traces, and driver limits can be useful during development, but it is not part of the stable evidence contract.

Stable per-run evidence is intentionally minimal: run identity, Run Seed Material, completion state, and, for Completed Simulation Runs, the Game Session Outcome, ending Turn, and ending Victory Check Window. Simulation execution first returns Simulation Batch Source Evidence containing execution identity, ordered per-attempt source records, and Completed and Incomplete Simulation Run counts. This source evidence is a precursor from which downstream terminal evaluation may compose complete batch-level Simulation Result Evidence with the information needed to derive screening and probability views, including Possible Faction and Possible Game Result inventories, completed outcomes by Game Result, completed outcomes by ending Victory Check Window, and ending Turn.

Bundled Simulator Cache entries are compressed lobby evaluations derived from stable evidence. They do not carry per-run source records or replay material. Run Seed Material remains in evidence and replay/audit workflows; cache entries carry only the derived lobby result needed by the app.

This ADR does not decide the distribution mechanism for cache artifacts. Whether cache entries ship inside the app package or are fetched from a static remote source remains deferred until full-role cache size is measured.

## Considered options

- **Full trace as stable evidence**: make transcripts, final state snapshots, timings, and driver diagnostics part of the result contract. Rejected because it couples long-lived evidence to implementation internals and creates a brittle versioning surface.
- **Aggregate probabilities only**: store just the final percentage rows. Rejected because screening, replay, QA, and zero-frequency Possible Game Result handling require more source evidence than final display rows.
- **Bundled cache carries per-run records**: put replay evidence directly in app-facing cache entries. Rejected because app lookup needs compact lobby evaluations, while replay and audit workflows can use separate evidence artifacts.
- **Stable minimal evidence plus derived cache entries**: keep enough source evidence for replayable interpretation, exclude diagnostics from the durable contract, and derive compact cache records for app use. Accepted.
