# Architectural Proposal: The State Engine Pattern & Zero-Leakage Mutation

## 1. Abstract

This proposal mandates a structural refactoring of the `Werewolves.StateModels` assembly to enforce a strict "Zero-Leakage" policy regarding state mutation.

Currently, the `GameSession` class acts as a "God Object," simultaneously functioning as the public API, the state container, and the rules enforcer. Due to C# scoping rules regarding nested classes, `GameSession` currently possesses privileged access to modify `PlayerState` directly, bypassing the architectural requirement that all changes must flow through the `IStateMutator` via the `GameHistoryLog`.

We propose decoupling these concerns by introducing a dedicated, internal **`GameSessionKernel`**. This engine will act as the hermetically sealed kernel of the application, encapsulating all concrete state classes (`Player`, `PlayerState`) as **private nested types**. `GameSession` will be reduced to a pure **Facade**, interacting with the state solely through read-only interfaces (`IPlayer`, `IPlayerState`) and dispatching commands via `GameLogEntry` objects. This architecture renders direct state mutation by the Session physically impossible at compile time.

## 2. Motivation

### 2.1. The "Privileged Scope" Leak
The current architecture relies on `PlayerState` being a `protected nested class` of `GameSession`. While this prevents external assemblies from modifying state, C# language specifications grant an outer class full access to the private members of its nested classes. This loophole allows `GameSession` logic to imperatively modify properties (e.g., `player.State.Health = Dead`) without generating a log entry, creating a divergence between the *Game History* and the *Memory Heap*.

### 2.2. Integrity of Consequential State Logging
With the adoption of "Consequential State Logging" (Revisions/6), the system relies on the `GameHistoryLog` to be the single source of truth for deterministic outcomes (e.g., Infection, Protection usage). If `GameSession` retains the ability to bypass the log, the integrity of the replay system is compromised. We require a system where the compiler enforces that **creation of a Log Entry is the only mechanism** to trigger a state change.

### 2.3. Separation of Intent vs. Mechanism
The current design conflates **Intent** (The API call `EliminatePlayer`) with **Mechanism** (The heap manipulation `player.Health = Dead`). By separating these into a Facade (Session) and a Kernel (Engine), we isolate the messy implementation details of mutation, storage, and caching from the clean, high-level logic of the Session.

## 3. Proposed Architectural Changes

### 3.1. Component: `GameSessionKernel` (The Kernel)
A new `internal sealed class` in `Werewolves.StateModels.Core` that acts as the sole owner of the game's mutable memory.

*   **Data Ownership:** The Kernel owns the `GameHistoryLog` (via a private `GameLogManager`), the `GamePhaseStateCache`, the `Players` dictionary, and configuration lists (`SeatingOrder`, `RolesInPlay`).
*   **Private Concrete Types:** The concrete classes `Player` and `PlayerState` are moved inside the Kernel as **private nested classes**.
    *   They implement `IPlayer` and `IPlayerState`.
    *   Their properties are auto-implemented (`public get; private set;`).
    *   **Key-Based Access:** They expose a `GetMutableState(IStateMutatorKey key)` method, ensuring only the authorized mutator can access writeable properties.
*   **The Proxy Mutator:** A `private nested class SessionMutator : ISessionMutator`.
    *   This class holds the singleton `StateMutatorKey`.
    *   It is the *only* component capable of unlocking the mutable state of players and the kernel itself.
*   **Transactional Apply Flow:**
    *   `GameLogEntryBase` defines `protected abstract GameLogEntryBase InnerApply(ISessionMutator mutator)`.
    *   The public `Apply` method is sealed and handles the actual insertion into the log *after* `InnerApply` returns. This guarantees that no state change can occur without the corresponding log entry being recorded.

### 3.2. Component: `GameSession` (The Facade)
`GameSession` is an `internal class` implementing the public `IGameSession` interface. It is stripped of all state storage fields, retaining only a reference to the Kernel.

*   **Public Contract (`IGameSession`):** Defines the read-only API available to the `GameLogic` assembly (e.g., `GetPlayer(Guid)`, `TurnNumber`).
*   **Internal Implementation:** `GameSession` delegates all queries to `_gameSessionKernel`.
*   **Command Dispatch:** To change state, `GameSession` constructs a `GameLogEntry` and passes it to `_gameSessionKernel.AddEntryAndUpdateState()`. It has no direct access to the `SessionMutator` or the `StateMutatorKey`.

### 3.3. Migration of Shared State
To ensure the Kernel is the single source of truth, `PlayerSeatingOrder` and `RolesInPlay` are moved from `GameSession` to `GameSessionKernel`. This allows future flexibility for `GameLogEntry` types that might need to mutate these lists (e.g., swapping seats or adding roles mid-game), which would be impossible if they remained immutable fields on the Session.

## 4. Documentation Changes

### 4.1. `architecture.md` Updates

*   **State Management Philosophy:** Update to define the **Facade/Kernel** relationship. Explicitly state that `GameSession` is a read-only projection of the `GameSessionKernel`.
*   **Core Components:**
    *   **`GameSession`:** Redefine as a stateless API wrapper.
    *   **`GameSessionKernel`:** Add section detailing its role as the "Fort Knox" of state, holding the Log, Cache, and concrete Player objects.
    *   **`ISessionMutator`:** Clarify that this interface is implemented privately within the Kernel to prevent casting attacks.
*   **State Models:** Remove references to "Protected Nested Classes" in `GameSession` and replace with "Private Nested Classes" in `GameSessionKernel`.

## 5. Impact Analysis

### 5.1. Benefits
*   **Absolute Encapsulation:** It is compile-time impossible for `GameSession` or `GameLogic` to mutate state directly. The `GameSessionKernel` acts as a perfect firewall.
*   **Key-Based Security:** The `IStateMutatorKey` pattern adds a second layer of defense, preventing even internal Kernel methods from accidentally mutating state without going through the `SessionMutator`.
*   **Self-Enforcing Logs:** The Transactional Apply flow ensures that the act of applying a change *is* the act of logging it, removing any possibility of "forgetting to log" a mutation.
*   **Interface Segregation:** Consumers (Session/Logic) deal only with clean contracts (`IPlayer`), never implementation details.
*   **Testability:** The `GameSessionKernel` can be unit-tested in isolation to verify log application logic without instantiating the full Session overhead.
*   **Simplified "Undo/Fork":** Because the Kernel owns 100% of the mutable state, creating a "Simulated Future" (e.g., for AI decision making) requires only cloning the Kernel, not the entire Session wrapper.

### 5.2. Considerations
*   **Delegation Boilerplate:** `GameSession` requires pass-through methods for every piece of data it exposes (e.g., `public IReadOnlyList<Guid> SeatingOrder => _gameSessionKernel.SeatingOrder;`).
*   **Serialization Constraints:** Standard JSON serializers cannot serialize the `GameSessionKernel.Players` dictionary directly because the concrete types are private.
    *   *Mitigation:* This reinforces the architecture's philosophy that the **Log** is the save file. We do not save the "Heap"; we save the "History." Loading a game requires replaying the log through the Kernel.

### 5.3. Conclusion
This architecture provides the strongest possible guarantee of data integrity in C#. It eliminates the risk of "Developer Bypass" (modifying state without logging) and ensures that the `GameHistoryLog` remains the undisputed, reproducible source of truth for the game's execution.