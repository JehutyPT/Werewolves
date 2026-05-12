### **Architectural Proposal: A Hook-Based, Role-Centric State Machine Model (Revised)** 
 
#### **1. Abstract** 
 
This proposal outlines a fundamental architectural refactoring of the `Werewolves.Core` application. The current model, which utilizes a centralized `GameFlowManager` to orchestrate role-specific actions, will be evolved into a **Hook-Based, Delegated State Machine Model**. In this new architecture, the `GameFlowManager` (GFM) is refactored into a high-level phase controller that fires declarative **Game Hooks** at specific, event-driven moments. It delegates all role-specific logic to any registered `IGameHookListener` (which includes roles and event cards), which are registered to listen for these hooks. 
 
Each listener operates as a self-contained state machine. To manage resumable, multi-step actions that must pause for moderator input, the system will use a new, unified `IntraPhaseStateCache` on the `GameSession`. This cache acts as a transient "program counter," becoming the single source of truth for the game's current execution point, tracking the active phase, sub-phase, hook, and any single listener that is paused awaiting input. This design enforces true encapsulation, enhances testability and scalability, and makes the system's behavior more predictable and robust by formalizing the contracts between the dispatcher and its listeners. 
 
--- 
 
#### **2. Motivation** 
 
The existing architecture concentrates game flow logic within the `GameFlowManager`. While effective for simple, single-action roles, this approach presents several long-term challenges: 
 
*   **Growing Complexity:** As roles with more intricate, multi-step behaviors are added, the `GameFlowManager` will accumulate significant conditional complexity, making it difficult to maintain, test, and reason about. 
*   **Tight Coupling:** The `GameFlowManager` is tightly coupled to the implementation details of various roles (e.g., special handling for Night 1 identification or Hunter elimination). This violates the principle of encapsulation, as the manager must be aware of how different roles operate. 
*   **Violation of Open/Closed Principle:** Adding a new, complex role may require modifying the existing, validated logic within `GameFlowManager`, increasing the risk of introducing regressions. 
 
The proposed transition addresses these issues by decoupling the "what" (the listener's specific action) from the "when" (the specific game event that triggers it) and the "how" (the game's overall phase progression). 
 
--- 
 
#### **3. Proposed Architectural Changes** 
 
The core of this proposal involves redesigning the contract between the `GameFlowManager` and all reactive game entities, formalizing a robust, strictly-scoped state mechanism, and implementing a declarative, event-driven dispatch model. 
 
##### **3.1. `IGameHookListener` Interface** 
 
The `IRole` interface will be superseded by a more generic `IGameHookListener` interface, which both roles and event cards can implement. Existing purpose-specific methods will be deprecated and replaced with a single, universal method. 
 
*   **New Method Signature:** 
    ```csharp 
    RoleActionResult AdvanceStateMachine(GameSession session, ModeratorInput input); 
    ``` 
 
*   **Interaction Contract:** The interaction between the GFM and a listener's state machine is strictly defined: 
    1.  **Dispatch:** When a `GameHook` is fired, the GFM iterates through *all* listeners registered for that hook and calls `AdvanceStateMachine` for each, providing the current `ModeratorInput`. 
    2.  **State Determination:** The `IGameHookListener` implementation is responsible for its entire lifecycle, including determining if it needs to act and what its current state is. It does this by querying the `GameSession` (e.g., to see if the player with the role is alive) and the `IntraPhaseStateCache` (to resume a paused action). Logic such as Night 1 identification is now an internal part of the listener's own state machine. 
 
*   **Return Value Semantics:** The `RoleActionResult` return type is non-nullable and communicates a precise, unambiguous outcome to the GFM dispatcher: 
    *   `RoleActionResult.NeedInput(instruction)`: The listener is active and requires further input; it has not completed its turn. The GFM will halt all processing and await the next moderator input, which will be routed back to this same listener. 
    *   `RoleActionResult.Complete(optional_instruction)`: The listener has successfully completed all its actions for this hook invocation (or chose not to act). This signals the GFM to immediately proceed to the next listener in the hook's sequence (if any). 
    *   `RoleActionResult.Error(error)`: An error occurred during processing. The GFM will halt and report the error. 
 
##### **3.2. Unified Intra-Phase State Management** 
 
To enable idempotent re-entrancy, the system will use a single, centralized `IntraPhaseStateCache`. This cache becomes the single source of truth for the game's current execution point, subsuming the `GamePhase` tracking from `GameSession`. It acts as a bookmark for resuming paused operations and does not store action outcomes, which remain the responsibility of the `GameSession.GameHistoryLog`. 
 
*   **State Cache Abstraction:** The `GameSession` will contain a single instance: 
    *   `public IntraPhaseStateCache IntraPhaseStateCache { get; }` 
 
    ```csharp 
    public class IntraPhaseStateCache 
    { 
        // Tracks the GFM's current execution point. 
        private GamePhase _currentPhase; 
        private string? _currentGfmSubPhase; 
 
        // Tracks the currently executing hook sequence. 
        private GameHook? _activeHook; 
         
        // Tracks the single listener that is currently paused awaiting input. 
        private ListenerIdentifier? _pausedListener; 
        private string? _pausedListenerState; 
 
        // --- GFM State Accessors --- 
        public GamePhase GetCurrentPhase() => _currentPhase; 
         
        public void SetGfmState<T>(GamePhase phase, T? subPhase = null) where T : struct, Enum 
        { 
            _currentPhase = phase; 
            _currentGfmSubPhase = subPhase?.ToString(); 
        } 
 
        public T? GetGfmSubPhase<T>() where T : struct, Enum 
        { 
            if (_currentGfmSubPhase != null) 
            { 
                return Enum.Parse<T>(_currentGfmSubPhase); 
            } 
            return null; 
        } 
 
        // --- Hook & Listener State Accessors --- 
        public void SetActiveHook(GameHook hook) => _activeHook = hook; 
        public GameHook? GetActiveHook() => _activeHook; 
        public void CompleteHook() => _activeHook = null; 
 
        public void SetPausedListenerState<T>(ListenerIdentifier listener, T enumState) where T : struct, Enum 
        { 
            _pausedListener = listener; 
            _pausedListenerState = enumState.ToString(); 
        } 
 
        public T? GetPausedListenerState<T>(ListenerIdentifier listener) where T : struct, Enum 
        { 
            if (_pausedListener.HasValue && _pausedListener.Value.Equals(listener) && _pausedListenerState != null) 
            { 
                return Enum.Parse<T>(_pausedListenerState); 
            } 
            return null; 
        } 
         
        public void CompletePausedListener() 
        { 
            _pausedListener = null; 
            _pausedListenerState = null; 
        } 
 
        // --- Central Cleanup (Mandatory) --- 
        public void ClearTransientState() 
        { 
            _currentGfmSubPhase = null; 
            _activeHook = null; 
            _pausedListener = null; 
            _pausedListenerState = null; 
        } 
    } 
    ``` 
 
*   **State Lifecycle (Mandatory & Automated):** The `GameFlowManager` **must** call `IntraPhaseStateCache.ClearTransientState()` as the final step of *every* transition between main `GamePhase`s (e.g., from `Day_ResolveVote` to `Night_RoleAction`). This guarantees transient state is never leaked across phases. 
 
##### **3.3. `GameFlowManager` Simplification: A Hook-Based Dispatcher** 
 
The GFM is refactored into a **Phase Manager** and a **Reactive Hook Dispatcher**. It will rely on a central mapping to resolve listener identifiers to their concrete implementations. 
 
*   **Listener Identification:** To accommodate different types of listeners (Roles, Events), a unified identifier will be used. 
    ```csharp 
    public readonly struct ListenerIdentifier 
    { 
        public GameHookListenerType ListenerType { get; } // Enum: Role, Event 
        public int ListenerId { get; } // Stores the RoleType or EventCardType enum value 
    } 
    ``` 
 
*   **Master Hook Configuration:** A single, declarative dictionary will map all potential listeners to the hooks that trigger them. Hooks will be granular, representing specific moments where game state can be modified. 
    ```csharp 
    // Inside GFM configuration 
    private readonly Dictionary<GameHook, List<ListenerIdentifier>> _masterHookListeners = new() 
    { 
        [GameHook.NightSequenceStart] = new List<ListenerIdentifier> { new(RoleType.SimpleWerewolf), new(RoleType.Seer) }, 
        [GameHook.OnPlayerEliminationFinalized] = new List<ListenerIdentifier> { new(RoleType.Hunter), new(EventCardType.Retribution) } 
    }; 
     
    // The GFM will also hold a master lookup for implementations. 
    private readonly Dictionary<ListenerIdentifier, IGameHookListener> _listenerImplementations; 
    ``` 
 
*   **Dispatcher Logic:** When firing a hook, the GFM iterates through the registered listeners. It does **not** perform an "activation check." 
    *   It dispatches to **every** listener in the sequence. 
    *   It is the listener's responsibility to inspect the `GameSession` and decide whether its conditions for acting have been met. 
    *   It reacts to the `RoleActionResult`: 
        *   On `NeedInput`, it suspends all processing to await moderator input. 
        *   On `Complete`, it immediately calls the next listener in the sequence. 
 
##### **3.4. GFM Phase Handler Implementation Pattern** 
 
The optimal pattern for defining the GFM's internal phase step sequences is a **re-entrant `switch` statement**. This provides the best balance of readability, explicit control flow, and maintainability. 
 
*   **Structure:** Each phase handler in the GFM will be a state machine that reads its current step from the `IntraPhaseStateCache` (e.g., `GetGfmSubPhase`) and uses a `switch` to jump to the correct logic block. 
*   **Best Practice:** To maintain readability, any `case` block containing significant logic will be refactored into a private helper method. This keeps the `switch` statement clean and allows it to serve as a high-level summary of the phase's process. 
 
--- 
 
#### **4. Detailed Interaction Flow Examples** 
 
##### **4.1 Example: Hunter's Shot (Event-Driven Hook)** 
 
1.  **Phase & Step 1:** The GFM is in `Day_ResolveVote`. Its handler enters, reads its state from the cache, and executes **Step 1**: process vote outcome, set `hunterPlayer.Status = PlayerStatus.Dead`. It then updates its own state: `cache.SetGfmState(GamePhase.Day_ResolveVote, DayResolveVoteStep.FireEliminationHook)`. 
2.  **Hook Firing (Step 2):** In the next step, the GFM calls `FireHook(GameHook.OnPlayerEliminationFinalized)`. It sets `cache.SetActiveHook(...)`. 
3.  **Dispatch:** The GFM looks up the hook, finds `ListenerIdentifier(RoleType.Hunter)`, and calls `hunterRole.AdvanceStateMachine(...)`. 
4.  **Role Logic (Initial State):** 
    *   Inside `AdvanceStateMachine`, the `HunterRole` checks the `GameSession` and confirms it is attached to a dead player. It then checks the cache for a paused state via `cache.GetPausedListenerState(...)` and finds `null`. 
    *   **Action:** It sets its paused state: `cache.SetPausedListenerState(hunterIdentifier, HunterState.AwaitingTarget)`. It returns `RoleActionResult.NeedInput` with an instruction to choose a victim. 
5.  **Await Input & Resumption:** The GFM receives `NeedInput` and halts. Upon receiving the next input, it re-enters the `Day_ResolveVote` handler. It checks its state, sees it's on `DayResolveVoteStep.FireEliminationHook`, and re-fires the hook. The hook logic checks `cache.GetPausedListener()` and knows to resume dispatching directly to the `HunterRole`. 
6.  **Role Logic (Subsequent State):** The `HunterRole` retrieves its state (`AwaitingTarget`), processes the input, logs the outcome to `GameHistoryLog`, and returns `RoleActionResult.Complete`. 
7.  **Sequence & Phase Completion:** The GFM receives `Complete`. It cleans up the listener's state (`cache.CompletePausedListener()`). As there are no more listeners for this hook, the hook is finished (`cache.CompleteHook()`). The GFM updates its execution state (`cache.SetGfmState(..., DayResolveVoteStep.ProceedToEndPhase)`) and proceeds to the final step of its logic. 
 
##### **4.2 Example: Werewolf Night 1 (Sequence-Driven Hook)** 
 
1.  **Hook Firing:** GFM enters `Night_RoleAction` and its first step is to call `FireHook(GameHook.NightSequenceStart)`. It sets `cache.SetActiveHook(...)`. 
2.  **Dispatch (Listener 1):** The GFM gets the first listener, `SimpleWerewolf`, and calls `werewolfRole.AdvanceStateMachine(...)`. 
3.  **Role Logic (Internal State 1: Identification):** The `SimpleWerewolfRole`'s state machine is entered. It checks `session.TurnNumber == 1` and sees that its players have not been identified. It retrieves its state from the cache, finds `null`, and enters its initial state. 
    *   **Action:** It sets its internal state: `cache.SetPausedListenerState(wwIdentifier, WerewolfState.AwaitingIdentification)`. It returns `RoleActionResult.NeedInput` with an instruction for the moderator to identify the werewolf players. 
4.  **Await Input & Resumption:** The GFM halts. The moderator provides the player IDs. The GFM re-enters its loop, sees the paused listener in the cache, and calls `AdvanceStateMachine` on the `SimpleWerewolfRole` again. 
5.  **Role Logic (Internal State 2: Victim Selection):** The role retrieves its state (`AwaitingIdentification`), processes the moderator's input to assign the roles, logs the assignments, and transitions its internal state. 
    *   **Action:** It now sets its state: `cache.SetPausedListenerState(wwIdentifier, WerewolfState.AwaitingVictimSelection)`. It returns `RoleActionResult.NeedInput` with the instruction to choose a victim. 
6.  **Advance Sequence:** After another input cycle, the role processes the victim selection, logs the action, and returns `RoleActionResult.Complete`. 
7.  **Dispatch (Listener 2):** The GFM receives `Complete` and clears the paused listener state. It sees more listeners are registered for the `NightSequenceStart` hook. It immediately calls `AdvanceStateMachine` on the next listener (`SeerRole`), all within the same `HandleInput` execution. This cycle repeats until a listener returns `NeedInput` or the entire sequence completes. 
 
--- 
 
#### **5. Impact Analysis** 
 
*   **Benefits:** 
    *   **True Encapsulation:** All logic for a role's or event's behavior resides entirely within its own class. The GFM has zero knowledge of how any specific listener operates. 
    *   **Explicit State Logic:** The use of explicit, intra-phase state machines makes listener logic clear and easy to follow. 
    *   **Simplified `GameFlowManager`:** The manager becomes a stable, generic dispatcher that processes a declarative configuration map. Its complexity no longer grows as new listeners are added. 
    *   **Enhanced Testability:** Each listener's state machine can be unit-tested in complete isolation. 
    *   **Improved Robustness:** Mandating GFM-driven cleanup of all transient state prevents state leakage bugs by design. 
    *   **Scalability & Maintainability:** Adding new, complex listeners requires **no modifications** to the core `GameFlowManager` phase handlers. 
 
*   **Considerations & Mitigations:** 
    *   **Traceability:** The distribution of game flow across multiple classes makes direct code tracing more difficult. 
        *   **Mitigation:** This is a **critical architectural requirement**. Robust, structured logging must be implemented. Each dispatch to `AdvanceStateMachine` must log the hook, the listener, the input type, the retrieved state string, and the result. GFM step transitions must also be logged. 
    *   **State Signaling Discipline:** The correctness of this pattern relies on listeners correctly signaling their completion with `RoleActionResult.Complete` versus `RoleActionResult.NeedInput`. 
        *   **Mitigation:** This contractual obligation must be strictly enforced through code reviews and documentation. 
    *   **Listener Responsibility:** The GFM dispatches to every listener registered for a hook. The listener is solely responsible for efficiently checking game state to determine if it should act. 
        *   **Mitigation:** Listeners must be coded defensively and perform their activation checks as their first operation. A listener that performs a costly operation unconditionally could degrade performance. This must be a point of emphasis during code reviews. 
    *   **State Deserialization Errors:** An error parsing a state string from the cache is a symptom of a critical logic bug. 
        *   **Mitigation:** These errors will not be handled gracefully at runtime. They should propagate as exceptions during development to ensure state management logic within every listener is implemented correctly.