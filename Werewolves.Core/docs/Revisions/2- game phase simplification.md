### **Architectural Proposal: Simplification of Game Phases via a Hybrid Consolidation Model**

### 1. Abstract

This proposal outlines a targeted change to the game's state machine architecture. The primary goal is to simplify the `GameFlowManager` by consolidating main game phases that are strictly sequential in nature. Following a detailed analysis, the **`Night`** phase is a prime candidate for consolidation into a single, action-focused sequence. All complex outcome calculation will be removed from the `Night` phase and concentrated into a new, unified **`Day_Dawn`** phase, which merges the responsibilities of the former `Day_ResolveNight` and `Day_Event` phases. This ensures calculation and subsequent moderator interactions occur within a single, cohesive logical block where the system has full context. Conversely, the remaining Day phases—specifically those involving voting—contain complex, interrupt-driven logic, making them unsuitable for a simple sub-phase model. Therefore, this proposal advocates for a hybrid approach to robustly handle the game's logical structure.

### 2. Motivation

The current architecture, while functional, defines multiple main game phases for sequences that are, in practice, a single, uninterrupted flow of events. The `Night` phase is the most prominent example; it is a deterministic loop of role calls that always follows the same order. Similarly, the process of resolving the night's victims and then processing their role reveals is a single, linear sequence. Representing these steps as distinct main phases adds unnecessary complexity to the `GameFlowManager`'s state transition map.

The guiding principle for this refactoring is that sub-phases should be used for simple, linear sequences. This proposal seeks to apply this principle to enhance architectural clarity by consolidating the `Night` phase's actions and merging the daybreak resolution steps into a single, comprehensive `Day_Dawn` main phase.

### 3. Proposed Architectural Changes

The proposal is divided into two key decisions: the consolidation of the `Night` phase and the consolidation of the daybreak resolution phases into a single `Day_Dawn` phase.

#### 3.1 Consolidation of the `Night` Phase

The collection of main phases related to the night sequence will be collapsed into a single main phase: `GamePhase.Night`. The `GameFlowManager`'s `HandleNightPhase` method will be enhanced to manage an internal sequence of operations using a dedicated `NightSubPhase` enum and the `GamePhaseStateCache`.

**New `GamePhase` Enum:**
The `GamePhase` enum will be simplified. For example:
*   `Night_Start` is removed.
*   `Night_RoleAction` is removed.
*   `Night_RoleSleep` is removed.
*   A single `Night` phase is introduced/retained.

**Internal `Night` Phase Logic (`HandleNightPhase`):**
This handler will manage the following internal sub-phases sequentially:

1.  **`NightSubPhase.Start`:**
    *   Triggered on entry to the `Night` phase.
    *   Increments the `TurnNumber` (if transitioning from a Day phase).
    *   Issues the "Village goes to sleep" instruction.
    *   Transitions internally to `RoleIdentification`.

2.  **`NightSubPhase.RoleIdentification` (Conditional):**
    *   Executes only if `session.TurnNumber == 1`.
    *   Iterates through the roles that require first-night identification (Thief, Cupid, etc.).
    *   Uses the hook-based system to prompt the moderator for identification for each required role.
    *   Upon completion, transitions internally to `ActionLoop`.

3.  **`NightSubPhase.ActionLoop`:**
    *   The core of the `Night` phase.
    *   Iterates through the complete, ordered list of night roles.
    *   For each role, it fires the appropriate `GameHook`, logging all actions to the `GameHistoryLog`.
    *   The `GamePhaseStateCache` will track the currently active listener. If a listener requires input, the `Night` phase will pause, storing its progress. When input is received, it will resume from the exact same point in the loop.
    *   Once all roles have acted, the `HandleNightPhase` method signals a transition to the `Day_Dawn` main phase.

#### 3.2 Consolidation of Daybreak Resolution Phases

A key component of this proposal is the creation of a new, consolidated `Day_Dawn` phase, which merges the former `Day_ResolveNight` and `Day_Event` phases. This serves as the central point for all night outcome calculations and the subsequent processing of those outcomes. This design concentrates the entire resolution logic at the moment of execution, where knowledge of all influencing factors—including night actions and daybreak events—is complete.

**New `GamePhase` Enum:**
*   `Day_ResolveNight` is removed.
*   `Day_Event` is removed.
*   `Day_ResolveVote` becomes `Day_Dusk`.
*   A single `Day_Dawn` phase is introduced.

**Internal `Day_Dawn` Phase Logic (`HandleDayDawnPhase`):**
This handler will manage a sequence of internal sub-phases to process all outcomes, including cascading eliminations (e.g., Hunter, Lovers) and role reveals, as a single atomic operation.

1.  **`DawnSubPhase.CalculateVictims`:**
    *   Queries the `GameHistoryLog` for all night actions.
    *   Calculates the final list of all players eliminated during the night, processing a queue of effects to handle cascading eliminations. If a hook listener (e.g., Hunter) requires moderator input for a target, the phase will pause and resume within this sub-phase until the elimination queue is empty.

2.  **`DawnSubPhase.AnnounceVictims`:**
    *   Issues a single `ModeratorInstruction` to announce all victims. Awaits moderator confirmation to proceed.

3.  **`DawnSubPhase.ProcessRoleReveals`:**
    *   Iterates through the list of eliminated players. For each player, it issues a `ModeratorInstruction` to provide the revealed role.
    *   The phase will pause and resume as it receives input for each required reveal.

4.  **`DawnSubPhase.Finalize`:**
    *   Once all reveals are processed, signals a transition to the `Day_Debate` main phase.

The proposal explicitly recommends *against* consolidating the other Day phases (`Day_Debate`, `Day_Vote`, `Day_Dusk`). These phases remain subject to interruptions (Stuttering Judge, Devoted Servant) that represent complex branching behavior unsuitable for a linear sub-phase model.

### 4. Impact Analysis

#### 4.1 Benefits

*   **Improved Cohesion:** The logic for the `Night` phase is streamlined to focus purely on executing role actions. The entire process of resolving night outcomes, from calculation to role reveals, is encapsulated within the single, logical `Day_Dawn` phase.
*   **Centralized Calculation Logic:** All logic for determining the final outcome of a night is located in a single handler. This ensures that calculation only occurs once full information is available.
*   **Reduced State Machine Complexity:** The number of top-level `GamePhase` enum members and the number of defined transitions between them will be reduced, simplifying the state machine graph.
*   **Robustness:** By intentionally not consolidating the voting-related Day phases, the architecture retains the flexibility to handle complex, interrupt-driven role mechanics in a clear and explicit manner.

#### 4.2 Considerations & Mitigations

*   **Consideration: Increased Complexity in `HandleDayDawnPhase`**
    *   The `HandleDayDawnPhase` method's responsibilities will expand significantly. It must contain the entire logic for interpreting raw actions from the `GameHistoryLog`, managing a queue of cascading eliminations, and handling a multi-step moderator input loop for announcing victims and revealing their roles.
    *   **Mitigation:** This complexity will be managed by structuring the handler with a re-entrant `switch` statement based on a `DawnSubPhase` enum stored in the `GamePhaseStateCache`. Each case in the switch will delegate to a clear, single-purpose private helper method (e.g., `CalculateProvisionalNightVictimsFromLog`, `ProcessNextRoleReveal`). This ensures the code remains organized and testable, despite the handler's expanded scope.

*   **Consideration: State Tracking within the `ActionLoop`**
    *   The `GamePhaseStateCache` must correctly track which role is currently acting in the `Night` phase's `ActionLoop` to support the pause/resume functionality.
    *   **Mitigation:** The `GamePhaseStateCache` is already designed for this purpose. Its `SetCurrentListenerState` and `GetCurrentListenerState` methods will be used to store and retrieve the active listener in the loop, ensuring the state is managed correctly with no new architectural components required.

*   **Consideration: First Night vs. Subsequent Nights Logic**
    *   The single night handler must correctly differentiate between the first night (which includes role identification) and all other nights.
    *   **Mitigation:** This will be handled by a simple conditional check on `session.TurnNumber == 1` at the beginning of the `HandleNightPhase` logic, which will route the flow through the `RoleIdentification` sub-phase. This is a trivial and low-risk implementation detail.