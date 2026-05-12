# Architectural Proposal: Atomic Game Initialization via Orchestration

## 1. Abstract
This proposal addresses a critical flaw in the current `GameService` startup workflow where a `GameSession` can be instantiated in an invalid "stuck" state (missing a `PendingModeratorInstruction`). The proposed solution involves relocating concrete `ModeratorInstruction` implementations to the `Werewolves.StateModels` project and shifting the initialization strategy to **Constructor Injection via Orchestration**. This ensures that a `GameSession` cannot be instantiated without a valid initial instruction, enforcing atomic validity while maintaining strict separation of concerns between the Rules Engine (`GameFlowManager`), State Container (`GameSession`), and Orchestrator (`GameService`).

## 2. Motivation

### 2.1 The Problem
Currently, `GameService.StartNewGame` performs two discrete actions:
1.  It instantiates a `GameSession`.
2.  It (implicitly) expects the session to be ready for input.

However, the `GameSessionKernel` initializes with `PendingModeratorInstruction` as null (or a hardcoded placeholder). The `GameFlowManager`—which owns the logic for determining the first instruction—is not invoked until the first input is processed. `GameService.ProcessInstruction` throws an exception if `PendingModeratorInstruction` is null. Consequently, a newly started game enters a deadlock: it waits for input to generate the first instruction, but cannot validate input because no instruction exists.

### 2.2 Rejected Alternatives
During analysis, three alternative solutions were considered and rejected:
*   **Service Mutation (Rejected):** Granting `GameService` write access to `SetPendingInstruction` violates the "Mutation Gatekeeper" pattern. It leaks business logic (knowing *what* the first instruction is) into the Service layer.
*   **Manager-as-Factory (Rejected):** Having `GameFlowManager` create and return the `GameSession` violates the Single Responsibility Principle. The Rules Engine should not act as a resource factory or manage object lifecycles; it should only determine state transitions.
*   **Instruction Self-Generates ID (Rejected):** Having `StartGameConfirmationInstruction` generate its own `Guid` was considered, but rejected because ID ownership should remain with `GameSession`/`GameSessionKernel`. The orchestration pattern (Option C2) was chosen instead, where `GameService` generates the ID and passes it to both `GetInitialInstruction` and the `GameSession` constructor.

### 2.3 The Goal
To achieve a startup flow where:
1.  **Atomic Validity:** A `GameSession` is guaranteed to be in a valid, playable state immediately upon construction.
2.  **Dependency Sanctity:** `GameSession` remains a "dumb" container, and `GameFlowManager` remains a pure logic engine.
3.  **Orchestration:** `GameService` acts as the glue, retrieving requirements from the Logic layer to satisfy the dependencies of the State layer.

## 3. Proposed Architectural & Documentation Changes

### 3.1 Project Structure Refactoring
**Move `ModeratorInstruction` Implementations:**
The concrete implementations of `ModeratorInstruction` (currently in `Werewolves.GameLogic`) will be moved to `Werewolves.StateModels`.
*   **Source:** `Werewolves.GameLogic.Models.Instructions` (e.g., `ConfirmationInstruction`, `SelectPlayersInstruction`, `AssignRolesInstruction`).
*   **Destination:** `Werewolves.StateModels.Models.Instructions`.
*   **Rationale:** These classes function as Data Transfer Objects (DTOs) defining the *shape* of the state/UI contract. They do not contain business logic. Moving them resolves circular dependencies, allowing `GameSession` to accept them as constructor arguments.

### 3.2 `GameSessionKernel` State Ownership
**Move `Id` Property to Kernel:**
The `Id` property will be relocated from `GameSession` to `GameSessionKernel`, consistent with the "Kernel is Fort Knox" principle where all canonical state lives.
*   **`GameSessionKernel`:** Add `internal Guid Id { get; }` initialized via constructor parameter.
*   **`GameSession`:** Change `public Guid Id { get; }` to a pass-through getter: `public Guid Id => _gameSessionKernel.Id;`
*   **Rationale:** Ensures single source of truth for all state, including identity.

### 3.3 `GameFlowManager` (Logic Layer)
Introduce a pure function to generate the bootstrap instruction without creating a session.

*   **Add:** `public static StartGameConfirmationInstruction GetInitialInstruction(List<MainRoleType> rolesInPlay, Guid gameId)`
*   **Logic:** This method performs any necessary setup validation (e.g., checking for required roles) and returns the specific `StartGameConfirmationInstruction` with the provided game ID.
*   **Rationale:** The `Guid gameId` parameter is required because `StartGameConfirmationInstruction` contains the game ID for UI consumers. `GameService` generates this ID before calling the method.

### 3.4 `GameSession` & `GameSessionKernel` (State Layer)
Enforce the dependency at the constructor level.

*   **Modify `GameSessionKernel` Constructor:**
    ```csharp
    internal GameSessionKernel(
        Guid id,
        ModeratorInstruction initialInstruction,
        List<string> playerNamesInOrder,
        List<MainRoleType> rolesInPlay,
        List<string>? eventCardIdsInDeck = null)
    ```
    *   Accept `id` and assign to `Id` property.
    *   Accept `initialInstruction` and assign immediately to `_pendingModeratorInstruction`.
    *   Add validation: `ArgumentNullException.ThrowIfNull(initialInstruction);`
*   **Modify `GameSession` Constructor:**
    ```csharp
    internal GameSession(
        Guid id,
        ModeratorInstruction initialInstruction,
        List<string> playerNamesInOrder,
        List<MainRoleType> rolesInPlay,
        List<string>? eventCardIdsInDeck = null)
    ```
    *   Pass `id` and `initialInstruction` through to the Kernel constructor.
*   **Remove:** The hardcoded initialization of `_pendingModeratorInstruction` inside the Kernel. The Kernel no longer needs to know English strings like "Confirm Night Started".

### 3.5 `GameService` (Orchestration Layer)
Update the `StartNewGame` workflow to orchestrate the hand-off.

*   **Update `StartNewGame`:**
    ```csharp
    public StartGameConfirmationInstruction StartNewGame(
        List<string> playerNamesInOrder,
        List<MainRoleType> rolesInPlay,
        List<string>? eventCardIdsInDeck = null)
    {
        // 1. Generate the game ID
        var gameId = Guid.NewGuid();
        
        // 2. Get the initial instruction from GameFlowManager (pure function)
        var initialInstruction = GameFlowManager.GetInitialInstruction(rolesInPlay, gameId);
        
        // 3. Create the session with both the ID and instruction
        var session = new GameSession(gameId, initialInstruction, playerNamesInOrder, rolesInPlay, eventCardIdsInDeck);
        
        // 4. Store the session
        _sessions.TryAdd(session.Id, session);
        
        // 5. Return the same instruction that was passed to the session
        return initialInstruction;
    }
    ```
*   **Key Decision:** `StartNewGame` continues to return `StartGameConfirmationInstruction` (not just `Guid`) for convenience to UI consumers, who need both the game ID and the initial instruction.

### 3.6 Documentation Updates (`architecture.md`)
*   **Update `ModeratorInstruction` Class Hierarchy:** Reflect that these are now part of the `Werewolves.StateModels` assembly.
*   **Update Game Loop Outline:** Explicitly define the "Bootstrap" step where `GameService` retrieves the initial instruction before the Setup Phase technically begins.
*   **Update Component Definitions:** Refine `GameFlowManager` description to include its role in providing "Bootstrap State" via pure functions.

## 4. Impact Analysis

### 4.1 Benefits
*   **Compile-Time Safety:** It becomes structurally impossible to compile code that instantiates a `GameSession` without a valid starting state.
*   **Testability:** The "Start Game" logic in `GameFlowManager` becomes a pure function, allowing unit tests to verify the initial instruction without mocking a complex `GameSession` or `Kernel`.
*   **Decoupling:** `GameSession` is decoupled from localization/text resources. `GameFlowManager` is decoupled from object lifecycle management.
*   **Stability:** Eliminates the race condition/null reference exception currently blocking game progress.
*   **State Consistency:** Moving `Id` to the Kernel ensures all canonical state lives in one place.

### 4.2 Considerations & Mitigations
*   **Refactoring Effort:** Moving the Instruction classes will require namespace updates across the solution.
    *   *Mitigation:* Use IDE refactoring tools to handle namespace adjustments safely.
*   **Instruction Logic:** If `ModeratorInstruction` classes currently contain logic (e.g., `CreateResponse` validation), moving them to `StateModels` might feel like leaking logic to the State project.
    *   *Mitigation:* This is acceptable. Input validation logic *belongs* near the data contract. It ensures the "shape" of the input matches the "shape" of the request, which is a data concern, not a game rule concern.
*   **Architecture Violation Risk:** Future developers might try to pass the `GameSession` into `GetInitialInstruction` if initialization becomes complex (e.g., depending on player count).
    *   *Mitigation:* Keep `GetInitialInstruction` signature restricted to configuration data (`List<MainRoleType>`, `Guid`) only. Do not allow `GameSession` as a parameter.
*   **Constructor Bypasses Key Mechanism:** The `GameSessionKernel` constructor directly sets `_pendingModeratorInstruction` without using the `IGameFlowManagerKey` that `SetPendingModeratorInstruction` requires.
    *   *Mitigation:* This is an acceptable compromise. The constructor is setting the *initial* state of a private field for an object being constructed—this is fundamentally different from mutation of an existing object. The key mechanism protects against unauthorized *post-construction* mutation.