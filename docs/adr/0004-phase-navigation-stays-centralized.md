# Phase navigation stays centralized

Main phase and sub-phase navigation is defined in `GameFlowManager`, even when the behavior executed at a phase step lives in a per-phase handler module. Phase handlers may calculate game outcomes, mutate the session, and produce moderator instructions, but they do not choose the next phase or sub-phase.

The navigation graph is a correctness and auditability property of the game rules. A moderator helper must make it easy to see the canonical path through Night, Dawn, and Day, including branch points such as Dawn victim announcement, tie votes, vote repeats, death-chain processing, and main-phase exits. Keeping those transitions in one place makes the game flow readable against the rulebook without opening every phase implementation file.

This deliberately accepts a larger `GameFlowManager`. The value of extracting phase-specific behavior is to keep prompts, role assignment, victim calculation, and vote resolution close to their domain code. The value of keeping navigation centralized is different: it preserves one visible map of the game loop. When these values conflict, navigation visibility wins.

`EliminationCascadeStage` therefore never selects a sub-phase or Main Phase destination. It drains the full scoped batch/reaction chain and reports completion; the following Dawn or Day navigation stage owns the transition. Victory and consecutive-vote routing cannot run while a cascade remains active.

## Considered options

- **Per-phase modules own their full state machines**: each phase file defines its sub-phases, branch points, and main-phase exits. Rejected because the game's full navigation path becomes distributed across files, making it harder to audit where phases can go.
- **GameFlowManager owns only main-phase transitions**: `Night -> Dawn -> Day -> Night` stays central, but sub-phase exits stay inside phase modules. Rejected because the important exit points out of each main phase are still buried in the phase files.
- **Central navigation with extracted handlers**: `GameFlowManager` defines phase and sub-phase transitions, while per-phase handler modules hold the work performed at those steps. Accepted because it keeps the flow visible without reintroducing every phase-specific behavior into `GameFlowManager`.
