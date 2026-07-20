# Lobby evaluation uses layered support and classification gates

Lobby evaluation proceeds through explicit gates rather than one overloaded validity check: Rules-Valid Role Composition, App-Supported Role Composition, Capability-Supported Simulation Scenario for the requested capability, Already-Decided Role Composition, Degenerate Simulation Scenario, then—only for a Full-Probability-Supported request—probability simulation.

Each gate answers a different question and has different user-facing consequences. Rules validity is about the physical game rules. App support is about whether the Moderator helper can guide the game. Capability support is about whether the named simulator boundary can evaluate the scenario with its Role set, setup artifacts, and headless-response policy. Already-decided classification uses only Role Composition evidence available at Lobby Exit. Degenerate classification is screening evidence over completed simulation runs. Probability output belongs only to the dormant full-probability capability after the scenario passes the earlier gates.

This separation is especially important while the implemented role catalog, app UI, and simulator profile grow at different speeds. A Role may be described by the rules before it is implemented. A Role Composition may be app-supported before a simulator profile can evaluate it. A simulator failure must not be mislabeled as balanced, already-decided, or degenerate.

## Considered options

- **One overloaded valid/supported result**: return a single pass/fail state from lobby setup. Rejected because it hides whether the problem is physical-rules invalidity, unsupported app scope, unsupported simulator scope, already-decided outcome, degenerate screening evidence, or evaluation failure.
- **Simulate first and classify from outcomes**: run the simulator for any plausible setup and infer lobby result from observed outcomes. Rejected because Already-Decided Role Compositions are Lobby Exit facts derived from Role Composition evidence alone, not simulation results.
- **Treat app support and simulator support as the same boundary**: expose only Roles the simulator can evaluate. Rejected because app feature support and simulator profile support should be able to advance independently.
- **Layered gates with explicit evidence boundaries**: preserve separate domain terms and ordered evaluation responsibilities. Accepted.

## Amendment

ADR-0013 retains these ordered evidence boundaries but changes production capability selection to stop after safety screening. Full probability evaluation remains a dormant later stage.

ADR-0016 further parameterizes simulator support by an explicitly named capability. Production uses a Safety-Screening-Supported Simulation Scenario gate; dormant probability requests additionally require Full-Probability support. The Full-Probability Role Set is a subset of the Safety-Screening Role Set, but safety-only Roles do not enter probability evaluation or build-time cache enumeration.

ADR-0017 further amends the target pipeline. After App-Supported validation, Role Lock-In commits the complete Role Composition and any Deal Pool/Thief Offer partition. The Lobby then collects setup required by every Role reachable from either zone before capability validation, Already-Decided classification from the Deal Pool only, and branchwise Degenerate screening. Each semantically distinct offered-Role behavior is screened, along with Decline when legal; `Offer1` and `Offer2` remain separate response and physical-card identities, but same-printed-Role offers may share one behavioral branch. Any Degenerate branch blocks; otherwise incomplete/error/timeout branches aggregate to nonblocking Could Not Evaluate, and only all-complete non-degenerate branches pass. These are target semantics, not a claim that current lobby or simulator APIs implement them.
