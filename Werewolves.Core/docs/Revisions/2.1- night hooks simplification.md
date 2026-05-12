### **Architectural Proposal: Unification of First Night and Regular Night Action Loops**

### 1. Abstract

This proposal outlines a change to the `Werewolves.Core` architecture to simplify the `Night` phase game flow. The current design, which implies separate sub-phases for `RoleIdentification` and `ActionLoop` on the first night, introduces unnecessary complexity and maintenance risks. The proposed change is to consolidate these into a single, unified `NightActionLoop` sub-phase that executes every night of the game. The responsibility for handling first-night-only behavior will be delegated to the individual role implementations (`IGameHookListener`), which will conditionally execute logic based on the `GameSession.TurnNumber`. This refactoring will simplify the `GameFlowManager`, align the system more closely with its core architectural principles, and eliminate the risk of configuration errors.

### 2. Motivation

The primary motivation for this change is to address two significant issues with a bifurcated first-night logic:

*   **Inaccurate Game Flow Modeling:** In the physical game of Werewolves, the first night consists of a single, sequential series of "wake-up" calls. A design with separate identification and action phases could lead to a flawed user experience where the helper application instructs the moderator to wake the same player twice (e.g., Cupid to identify lovers, then later as part of the village to sleep) or implies a more complex flow than what actually occurs. This proposal aims to model the real-world game flow more accurately.

*   **Architectural Fragility and Maintenance Overhead:** The current architecture uses a static `_masterHookListeners` dictionary to define the deterministic order of night actions. A two-phase system for the first night would necessitate two separate, ordered lists of listeners—one for `RoleIdentification` and one for the `ActionLoop`. Because the first-night sequence is a superset of subsequent night sequences, these lists would contain overlapping roles in a specific order. This creates a significant maintenance burden and a high risk of desynchronization errors whenever a new role is added or the night order is adjusted.

*   **Violation of Architectural Principles:** The principle of "Self-Contained State Machines" dictates that listeners should encapsulate their own logic. Forcing the `GameFlowManager` (GFM) to manage two different types of night loops makes it more of an orchestrator than a dispatcher. The decision of whether a role should act on a specific night is part of that role's internal logic, not a responsibility of the central flow controller.

### 3. Proposed Architectural and Documentation Changes

To implement this proposal, the following changes will be made:

1.  **`GameFlowManager` Simplification:**
    *   The `HandleNightPhase` method within the `GameFlowManager` will be streamlined.
    *   The `NightSubPhases` enum will be simplified from `Start`, `RoleIdentification`, `ActionLoop` to just `Start`, `ActionLoop`.
    *   The GFM's logic will no longer check if `TurnNumber == 1` to decide which loop to run. It will execute the `ActionLoop` sub-phase on every turn, including the first.

2.  **Hook and Listener Registration Consolidation:**
    *   Any `GameHook` enum values specific to a `RoleIdentification` phase will be removed.
    *   The `_masterHookListeners` static dictionary will be configured with only **one** master list for the unified `NightActionLoop` hook. This list will contain *all* roles with night actions, in their correct, deterministic call order for the entire game.

3.  **Delegation of Logic to `IGameHookListener` Implementations:**
    *   Role classes that have actions exclusive to the first night (e.g., `CupidRole`, `WolfHoundRole`) **must** now include a check at the beginning of their `AdvanceStateMachine` method.
    *   This check will query the game state for the current turn number. If the turn number is greater than one, the listener will immediately return `HookListenerActionResult.Complete()` without performing any logic or requesting moderator input.

    **Example Implementation (Conceptual):**
    ```csharp
    // Inside a role like CupidRole.cs
    public HookListenerActionResult AdvanceStateMachine(GameSession session, ModeratorInput input)
    {
        // On any night after the first, Cupid does nothing.
        if (session.TurnNumber > 1)
        {
            return HookListenerActionResult.Complete();
        }

        // ... existing Night 1 logic to choose lovers follows ...
    }
    ```

4.  **Documentation Updates:**
    *   The `architecture.md` document will be updated to reflect these changes.
    *   The **Game Loop Outline** section for the `Night Phase` will be rewritten to describe the single, unified `ActionLoop`.
    *   The **Enums** section will be updated to show the simplified `NightSubPhases` enum.
    *   The description of the `GameFlowManager`'s responsibilities will be clarified to emphasize its role as an orchestrator of *phase transitions*, not intra-phase conditional logic.
    *   A note will be added to the `IGameHookListener` Interface section establishing the pattern of checking `session.TurnNumber` as a requirement for roles with turn-specific actions.

### 4. Impact Analysis

#### 4.1. Benefits

*   **Architectural Simplicity:** The logic within the `GameFlowManager` becomes significantly cleaner and more stable. The concept of "night" is unified, removing special-case code paths.
*   **Reduced Maintenance Overhead:** By consolidating to a single master list for night action order, the risk of configuration errors is drastically reduced. Adding, removing, or re-ordering night-acting roles becomes a single, atomic change.
*   **Improved Architectural Consistency:** This change strongly reinforces the "Self-Contained State Machines" principle. Each role becomes fully responsible for its own lifecycle, including on which turns it is active. The GFM's role as a high-level phase navigator is clarified.
*   **Increased Robustness:** Eliminating parallel, manually synchronized lists of listeners removes a potential source of critical, hard-to-debug runtime errors.

#### 4.2. Considerations & Mitigations

*   **Consideration:** First-night logic, which was previously implied by the phase, is now explicitly distributed across multiple role classes.
    *   **Mitigation:** This is an intended and positive consequence of adhering to the "Self-Contained" principle. The logic now resides with the component to which it belongs. This pattern will be formally documented as the standard for implementing roles with turn-specific behavior, and adherence will be enforced through code reviews.

*   **Consideration:** A minor, theoretical performance overhead is introduced by invoking `AdvanceStateMachine` on listeners that will do no work on subsequent nights.
    *   **Mitigation:** This overhead is negligible and will have no measurable impact on application performance. The cost of a single integer comparison (`if (session.TurnNumber > 1)`) and an immediate method return is computationally trivial. The significant gains in maintainability and architectural integrity far outweigh this theoretical cost.