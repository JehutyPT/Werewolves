# Bugs

## Issue #67 follow-up: source scaffold still freezes constructor parameter shape

- **Failed criterion:** No tests remain that only freeze child component names, handler names, parameter declarations, exact CSS classes, resource key names, or implementation methods unless #52 explicitly promotes that source shape to policy.
- **Evidence:** `Werewolves.Client.Tests/Services/LobbySetupStateTests.cs` verifies that `LobbySetupState` exposes exactly one public constructor parameter of type `LobbySetupMetadata`.
- **Why this matters:** The test appears to protect constructor parameter declaration shape rather than a documented source-policy contract, and the current Source-Test Allowlist does not explicitly promote this shape as policy.
- **Suggested fix direction:** Either convert the check into behavior/service evidence for lobby setup state construction, or remove the source-shape assertion if equivalent behavior is already covered. If the constructor shape is intentionally a public policy contract, add it to `docs/agents/qa-strategy.md` with a permanent-policy rationale.
- **Resolved:** Replaced the constructor reflection assertion with behavior evidence that constructed lobby setup state projects supplied setup metadata into observable state.
