# Lobby evaluation uses layered support and classification gates

Lobby evaluation proceeds through explicit gates rather than one overloaded validity check: Rules-Valid Role Composition, App-Supported Role Composition, Simulator-Supported Simulation Scenario, Already-Decided Role Composition, Degenerate Simulation Scenario, then probability simulation.

Each gate answers a different question and has different user-facing consequences. Rules validity is about the physical game rules. App support is about whether the moderator helper can guide the game. Simulator support is about whether the active simulator profile can evaluate the scenario. Already-decided classification uses only Role Composition evidence available at Lobby Exit. Degenerate classification is screening evidence over completed simulation runs. Probability output is shown only after the scenario passes the earlier gates.

This separation is especially important while the implemented role catalog, app UI, and simulator profile grow at different speeds. A Role may be described by the rules before it is implemented. A Role Composition may be app-supported before a simulator profile can evaluate it. A simulator failure must not be mislabeled as balanced, already-decided, or degenerate.

## Considered options

- **One overloaded valid/supported result**: return a single pass/fail state from lobby setup. Rejected because it hides whether the problem is physical-rules invalidity, unsupported app scope, unsupported simulator scope, already-decided outcome, degenerate screening evidence, or evaluation failure.
- **Simulate first and classify from outcomes**: run the simulator for any plausible setup and infer lobby result from observed outcomes. Rejected because Already-Decided Role Compositions are Lobby Exit facts derived from Role Composition evidence alone, not simulation results.
- **Treat app support and simulator support as the same boundary**: expose only Roles the simulator can evaluate. Rejected because app feature support and simulator profile support should be able to advance independently.
- **Layered gates with explicit evidence boundaries**: preserve separate domain terms and ordered evaluation responsibilities. Accepted.

## Amendment

ADR-0013 retains these ordered evidence boundaries but changes production capability selection to stop after safety screening. Full probability evaluation remains a dormant later stage.
