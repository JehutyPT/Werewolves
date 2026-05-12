Of course. I have updated the architectural proposal to reflect the principles and clarifications from our discussion.

The changes have been integrated to minimize rewording while ensuring every nuance of our conversation is detailed, particularly the strict definition of a loggable event and the precise flow of control between the `StateModel` and `GameLogic` layers.

Here is the revised proposal.

***

### Architectural Proposal: Encapsulated, Log-Driven State Refactoring of GameSession

#### 1. Abstract

This proposal outlines a plan to refactor the `GameSession` class to an **encapsulated, log-driven state architecture**. The core of this change is to establish the `GameHistoryLog` as the single, canonical source of truth for all **persistent, non-deterministic** game events. This will be achieved by splitting the core library into two distinct assemblies: a `StateModel` library containing the game state and its mutation mechanisms, and a `GameLogic` library for the stateless rules engine. All in-memory representations of persistent state will be treated as private, read-optimized caches that are mutated exclusively by applying events from the log. `GameSession` will become the sole gatekeeper for all state queries and modifications via a strictly controlled public API.

This change eliminates the possibility of data synchronization issues ("dirty caches") for persistent state. For persistence, the entire `GameSession` object, including its derived cached values and its transient execution state, will be serialized directly. This creates a more robust, testable, and maintainable foundation for the application.

#### 2. Motivation

The current architecture defines a philosophical separation between the `GameHistoryLog` (the history of *how* state evolved) and various properties within `GameSession` and its components that hold the *current* state (e.g., `TurnNumber`, `Player.State`). This creates two potential sources of truth.

This duality introduces a significant risk of **state inconsistency**. A developer could inadvertently update a player's state without creating a corresponding log entry, or vice-versa. Such "dirty cache" bugs are difficult to trace and undermine the system's reliability. By transitioning to a model where the `GameHistoryLog` is the *only* driver of runtime state changes, we can guarantee that the in-memory game state is always a correct and deterministic reflection of the game's non-deterministic history.

#### 3. Proposed Architectural Changes

The architecture will be refactored into two separate library projects to achieve compiler-enforced encapsulation.

1.  **Establish a Two-Assembly Architecture:**
    *   **`Werewolves.Core.StateModel`:** This new library will contain the complete state representation of the game. This includes `GameSession`, `Player`, `PlayerState`, all `GameLogEntry` derived classes, and all shared `enums`. This project will contain no game-specific rules logic (e.g., `GameFlowManager`, roles). Its purpose is to define the state and its internal mutation mechanics.
    *   **`Werewolves.Core.GameLogic`:** This library will contain the stateless "rules engine," including the `GameFlowManager`, `GameService`, and all `IGameHookListener` implementations (roles and events). This project will have a one-way reference to `Werewolves.Core.StateModel` and will only be able to interact with its `public` API.

2.  **Establish the `GameHistoryLog` as the Sole Driver of State Changes:**
    *   The `private readonly List<GameLogEntryBase> _gameHistoryLog` in `GameSession` will be the definitive record of all non-deterministic game events. It will be treated as an append-only event store.
    *   **Guiding Principle:** A log entry is created **if and only if** it represents a non-deterministic event, such as a player's choice, a moderator's input, or a random event card draw. Any deterministic outcome that can be calculated purely from the existing log history need not be entered into the logs, though it may be cached in memory for performance.
    *   The `GameSession` class in `StateModel` will expose a curated public API for querying state (e.g., `GetPlayerState(Guid)`) but will not expose mutable state properties or the raw log itself.

3.  **Formalize Player State as an Encapsulated, Log-Driven Cache:**
    *   The `Player` and `PlayerState` classes will reside in `StateModel`. All property setters within `PlayerState` will be marked as `internal`, making them inaccessible to the `GameLogic` assembly.
    *   `GameSession` will be the sole owner of the collection of `Player` objects. The assembly boundary prevents any external component, including the rules engine, from modifying state directly.

4.  **Implement a Secure, Polymorphic `Apply` API using the State Mutator Pattern:**
    *   To ensure cache mutations only occur as a direct result of a logged event, the **State Mutator Pattern** will be used. This pattern is simplified by the two-assembly architecture.
    *   `GameLogEntryBase` (in `StateModel`) will define an `internal abstract` method accepting an `IStateMutator` interface.
    *   `GameSession` will define the `internal IStateMutator` interface and a `private nested` class that implements it. Because this implementation is inside `StateModel`, it has privileged access to the `internal` setters of other classes in the same assembly, like `PlayerState`.

    *   **Example: The Simplified State Mutator Pattern**
        ```csharp
        // In Werewolves.Core.StateModel project...

        // In GameSession.cs
        public class GameSession
        {
            private readonly Dictionary<Guid, Player> _players = new();
            
            // 1. The public command method orchestrates the change.
            public void RecordElimination(Guid playerId, EliminationReason reason)
            {
                var entry = new PlayerEliminatedLogEntry { /* ... */ };
                _gameHistoryLog.Add(entry);
                // 2. A mutator is created and passed to Apply. The GameLogic assembly
                // cannot create this object or access the internal Apply method.
                entry.Apply(new StateMutator(this));
            }

            // 3. The mutator interface is internal.
            internal interface IStateMutator { /* ... */ }

            // 4. The concrete implementation is a private nested class.
            private class StateMutator : IStateMutator
            {
                private readonly GameSession _session;
                public StateMutator(GameSession session) => _session = session;

                public void SetPlayerStatus(Guid playerId, PlayerStatus newStatus)
                {
                    // It has privileged access to internal members of its own assembly.
                    _session._players[playerId].Status = newStatus;
                }
            }
        }

        // In Player.cs
        public class Player {
            // 5. The setter is internal, visible to StateMutator but not to GameLogic.
            public PlayerStatus Status { get; internal set; }
        }

        // In PlayerEliminatedLogEntry.cs
        internal override void Apply(GameSession.IStateMutator mutator) {
            mutator.SetPlayerStatus(this.PlayerId, PlayerStatus.Dead);
        }
        ```
    *   **Event Processing and Logic Decoupling:** The `Apply` method must remain free of game logic. Its sole responsibility is to mutate the derived state cache. The full, decoupled event flow is as follows:
        1.  The `GameLogic` layer (e.g., `GameFlowManager`) determines a non-deterministic event has occurred and invokes a public command on `GameSession` (e.g., `RecordElimination`).
        2.  The `GameSession` (`StateModel`) creates the log entry, adds it to the history, and calls the entry's `Apply` method to update the internal cache.
        3.  Control returns to the `GameLogic` layer.
        4.  The `GameFlowManager` then fires the appropriate hook to announce the state change (e.g., `OnPlayerEliminationFinalized`).
        5.  Hook listeners in `GameLogic` (e.g., `HunterRole`) react to this hook. If a listener's rules require a new non-deterministic input (e.g., the Hunter's final shot), it will pause the game and request that input.
        6.  This new input from the moderator begins a new, separate cycle, resulting in a distinct log entry (e.g., a second `PlayerEliminatedLogEntry` with `Reason: HunterShot`).

5.  **Enforce Stateless `IGameHookListener` Implementations:**
    *   This rule remains critical. All roles and events in the `GameLogic` library must be **stateless**.
    *   **Enforcement Strategy:** This will be enforced architecturally by registering all listener implementations as **singletons** within the dependency injection container.
    *   **Clarification:** The *execution state* of a listener's logical state machine will be tracked centrally within `GameSession`'s encapsulated `GamePhaseStateCache`.

6.  **Encapsulate and Delineate State Categories:**
    *   To manage complexity, all state will be classified into one of four distinct categories, creating clear boundaries of responsibility.
        *   **Persistent Canonical State:** The `_gameHistoryLog`. This is the absolute, immutable source of truth for all non-deterministic events.
        *   **Persistent Derived State:** Cached, read-optimized values derived from the log history (e.g., properties in `Player.State`). This state must persist across game phases and turns. A state like `PendingKnightCurseTarget` falls into this category, as it must be remembered from one night to the next. This state is mutated *only* by the `Apply` methods of log entries.
        *   **Runtime Transient State:** Short-lived state calculated on-the-fly by the `GameLogic` layer to manage a complex, multi-step process *within a single phase*. A property like `PendingEliminations` is a prime example; it would be calculated at the start of the `Day_Dawn` phase, processed completely, and then discarded. Such state should exist only as local variables within `GameFlowManager` methods, not as properties on `GameSession`.
        *   **Transient Execution State:** The `GamePhaseStateCache` instance. This is the runtime "program counter" for the `GameLogic` engine, tracking the current phase, hook, and paused listeners. It is part of `GameSession` to be persisted for save/load functionality but is not part of the canonical log.

#### 4. Impact Analysis

##### Benefits

*   **Compiler-Enforced Encapsulation:** The physical separation into two assemblies provides the strongest possible guarantee of encapsulation, enforced by the compiler. It is not possible for game logic components to circumvent the public API of the state model.
*   **Guaranteed Runtime State Consistency:** It will be architecturally impossible for the persistent in-memory state to be out of sync with the game's recorded history.
*   **Enhanced Robustness and Testability:** A specific sequence of log entries will always result in the exact same derived game state, making unit testing reliable and deterministic.
*   **Simplified and Performant Save/Restore Functionality:** Saving involves serializing the `GameSession` object. Restoring involves deserializing it, capturing both persistent and transient state for seamless resumption of play.
*   **Superior Separation of Concerns:** The architecture physically separates the "what" (the state model) from the "how" (the game logic), making the system easier to reason about and maintain.

##### Considerations & Mitigations

*   **Performance of State Calculation:**
    *   **Consideration:** Calculating state from a long history can be computationally expensive.
    *   **Mitigation:** The hybrid model avoids this on load by serializing the cache. During runtime, the `Apply` pattern ensures high-performance, surgical updates to the live cache.

*   **Increased Solution-Level Complexity:**
    *   **Consideration:** The architecture now involves two core library projects instead of one.
    *   **Mitigation:** This is a deliberate trade-off. By centralizing state concerns into a dedicated assembly, we simplify the logic and boundaries for the rest of the application. Strict, one-way dependency (`GameLogic` -> `StateModel`) must be maintained.

*   **State Corruption Risk via Logic Bugs:**
    *   **Consideration:** The chosen persistence strategy means that a bug in a `GameLogEntry.Apply` method could create a corrupted cache state. If this state is saved, the corruption becomes permanent.
    *   **Mitigation:** This is an accepted and explicit design trade-off where performance is prioritized over recoverability. The game session is considered **irrevocably lost** if a bug leads to a corrupted save file. The mitigation is a "fail-fast" design philosophy and a strict development process mandate: **every `GameLogEntry.Apply` implementation must undergo rigorous and exhaustive unit testing.**

*   **Testing and Debugging Strategy:**
    *   **Consideration:** The `internal` state within the `StateModel` assembly must be accessible to tests for setting up preconditions and asserting outcomes.
    *   **Mitigation:** The idiomatic .NET attribute `InternalsVisibleTo` will be used.
        *   The `Werewolves.Core.StateModel` project will specify its dedicated test project (e.g., `Werewolves.Core.StateModel.Tests`) as a "friend" assembly.
        *   This grants the test project access to all `internal` members of `StateModel` for comprehensive testing.
        *   Crucially, this access is **not** granted to any other assembly, including `Werewolves.Core.GameLogic`, thereby maintaining perfect encapsulation in the production code. This provides the ideal developer experience for testing without compromising architectural integrity.