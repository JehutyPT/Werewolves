# Project

Two-assembly architecture consisting of:
* `Werewolves.Core.StateModels` (.NET Class Library) - State representation and data models
* `Werewolves.Core.GameLogic` (.NET Class Library) - Game logic, rules engine, and flow management

# Goal

To provide the core game logic and state management for a Werewolves of Miller's Hollow moderator helper application, handling rules and interactions based on the provided rulebook pages (excluding Buildings/Village expansion, but including specified New Moon events). The app **tracks the game state based on moderator input**. It assumes moderator input is accurate and provides deterministic state tracking and guidance based on that input. 

# String Management Principle

To ensure maintainability, localization capabilities, and type safety: 
*   All user-facing strings (e.g., moderator instructions, log entry descriptions, error messages displayed to the user) **must** be defined in the `Resources/GameStrings.resx` file and accessed via the generated `GameStrings` class. 
*   Internal identifiers or constants used purely for logic (e.g., specific action types for conditional checks) should strongly prefer the use of dedicated `enum` types over raw string literals to avoid weakly-typed comparisons and improve code clarity. 

# State Management Philosophy

This architecture employs a **Kernel-Facade Pattern** with **Event Sourcing** and a **Two-Speed State Model** to ensure strict separation between the mutable core, the read-only public API, and the transient execution logic.

## Two-Speed State Architecture

The system distinguishes between two types of state:

1.  **Persistent Domain State (Event Sourced):**
    *   **Scope:** `TurnNumber`, `Players` (Health/Roles), `WinningTeam`, `GameHistoryLog`.
    *   **Mechanism:** Changes occur **exclusively** via `GameLogEntryBase` (e.g., `AssignRoleLogEntry`, `VictoryConditionMetLogEntry`).
    *   **Purpose:** Represents the permanent historical record of the game. If the application restarts, the *consequences* of previous actions are preserved.
    *   **Replayability:** The `GameHistoryLog` is sufficient to reconstruct the **Game Status**, but not the **Execution Pointer**. Replaying the log restores the game to the start of the current Main Phase.
    *   **Initial State:** Games begin in the Night phase. There is no Setup phase; the `StartGameConfirmationInstruction` directly triggers Night phase execution when confirmed.

2.  **Transient Execution State (In-Memory):**
    *   **Scope:** The current Main Phase/Sub-Phase cursor, completed and active stage, active listener and listener state, Pending Instruction, and semantic recovery cursors owned by `GameSessionKernel`.
    *   **Mechanism:** GameLogic reads one immutable `GameSession.Execution` (`ExecutionView`) snapshot and requests changes through the keyed, reducer-backed `GameSession` facade. The Kernel validates the expected snapshot and complete candidate before applying the change.
    *   **Purpose:** Represents the fleeting "program counter" of the logic engine. Logging every state machine tick (e.g., transitioning between stages within `Night.Start`) would bloat the history log with technical noise.

## Key Pattern (Controlled Mutation Access)

Most transient execution updates do not go through `GameLogEntryBase`, because recording every state-machine tick would pollute domain history. `GameSession` therefore exposes a narrow reducer facade rather than direct cache setters. Each request requires the key for the component that owns it; the key authorizes the caller, while the Kernel reducer remains the mutation authority and validates the requested transition.


*   **`IStateMutatorKey`:** Remains the separate capability used by the private `SessionMutator` to reach persistent mutable state while applying log entries.
*   **`IGameFlowManagerKey`:** Authorizes correlated Pending Instruction publication, optional stable-recovery-boundary advancement, and authenticated transient-continuation restoration. Its only production owner is `GameFlowManager`.
*   **`IPhaseManagerKey`:** Authorizes Sub-Phase movement and stage completion. Its only production owner is `PhaseManager<TSubPhaseEnum>`.
*   **`ISubPhaseManagerKey`:** Authorizes atomic stage entry. Its only production owner is `SubPhaseManager<TSubPhase>`.
*   **`IHookSubPhaseKey`:** Authorizes listener-state movement and clearing. Its only production owner is `HookSubPhaseStage`.
*   **`IRoleStageExecutionKey`:** Remains a separate capability for the existing Role-stage workflow; it is not an alternative execution-state mutation path.

## Core Principles

*   **Canonical State Source:** The `GameSessionKernel.GameHistoryLog` is the **single, canonical source of truth** for all **state-altering** game events. This includes both **non-deterministic inputs** (moderator choices) and **deterministic consequences** (rule resolutions like infection). This append-only event store drives all persistent state mutations.
*   **The Kernel (Core):** The `GameSessionKernel` is the **sole owner of mutable memory**. It encapsulates the `GameHistoryLog`, its private execution-cursor storage, `Players`, `SeatingOrder` and `RolesInPlay`. It is an internal, hermetically sealed component.
*   **The Facade (Read-Only Projection & Mutation Gatekeeper):** The `GameSession` class acts as a **read-only projection** of the Kernel for the public API (via `IGameSession`). GameLogic receives a coherent immutable `ExecutionView` for internal execution reads and uses keyed facade methods to submit reducer requests; it never receives the mutable phase cache.
*   **Transactional Mutation:** Persistent state mutation follows a strict transactional flow:
    1.  **Command Dispatch:** The Facade constructs a `GameLogEntryBase` directly or invokes a caller-supplied typed entry factory, then passes the resulting command to the Kernel.
    2.  **Proxy Mutator:** The Kernel creates a temporary `SessionMutator` (a private nested class implementing `ISessionMutator`) that has privileged access to the internal mutable state.
    3.  **Apply:** The `GameLogEntryBase.Apply(ISessionMutator)` method is called, allowing the entry to modify the state via the proxy.
    4.  **Commit:** If successful, the entry is appended to the `GameHistoryLog`. 
*   **Validated Execution Mutation:** The Kernel constructs or receives one complete candidate execution view, rejects stale or structurally invalid movement, and—when requested—builds and validates the candidate stable recovery snapshot before changing live state. Only after validation succeeds does it replace the execution cursor and any Pending Instruction/recovery boundary, then emit the notification owned by that transition.

# Hook System Architecture

The architecture uses a declarative hook-based system where the `GameFlowManager` acts as a **Pure Dispatcher** rather than an orchestrator: 

*   **Game Hooks:** Declarative events fired at specific moments in the game flow (e.g., `NightMainActionLoop`, `OnVoteConcluded`).
*   **Hook Listeners:** Components (roles and events) that register to respond to specific hooks.
*   **Self-Contained State Machines:** Each listener manages its own state and logic, encapsulating all behavior.
*   **Capability-Based Logic:** The dispatcher does not check *who* a player is (e.g., "Is this the Village Idiot?"), but *what* they can do (e.g., "Is this player immune to lynching?"). Logic relies on `IPlayerState` computed properties.
*   **Unified Execution Boundary:** `ExecutionView` provides one coherent read, and the Kernel reducer owns all cursor updates needed to pause and resume operations.

# Two-Assembly Architecture

The architecture is split into two separate library projects to achieve compiler-enforced encapsulation: 

*   **`Werewolves.Core.StateModels`:** This library contains the complete state representation of the game. This includes `GameSession`, `Player`, `PlayerState`, all `GameLogEntryBase` derived classes, all `ModeratorInstruction` implementations (in `Models.Instructions`), and all shared `enums`. This project contains no game-specific rules logic (e.g., `GameFlowManager`, roles). Its purpose is to define the state, its mutation mechanisms, and the UI communication contract (instructions). 
*   **`Werewolves.Core.GameLogic`:** This library contains the stateless "rules engine," including the `GameFlowManager`, `GameService`, and all `IGameHookListener` implementations (roles and events). This project has a one-way reference to `Werewolves.Core.StateModels`. **Crucially, `Werewolves.Core.StateModels` grants `[InternalsVisibleTo("Werewolves.Core.GameLogic")]`.** This allows the Rules Engine to access the `internal` concrete `GameSession` and its mutation methods, while external consumers (UI) are restricted to the `public` read-only interfaces. 

# Core Components

## `GameSession` Class (Facade) & `GameSessionKernel` (Core)

The architecture separates the public API (`GameSession`) from the internal state container (`GameSessionKernel`) to enforce zero-leakage mutation and strict encapsulation.

### `GameSessionKernel` (Internal Core):
The hermetically sealed kernel that owns the game's mutable memory. It is not visible to the public API.

*   **Sole Owner of Mutable State:** The Kernel holds the master references to:
    *   `Id` (Guid): The unique game session identifier, injected at construction
    *   `GameHistoryLog` (The event source)
    *   A private `GamePhaseStateCache` value used only for transient execution-cursor storage and stable DTO conversion
    *   The Pending Instruction, semantic recovery cursors, and latest validated stable recovery boundary
    *   `Players` (Dictionary of concrete `Player` objects)
    *   `SeatingOrder` (List of Guids)
    *   `RolesInPlay` (List of roles)
    *   `ListenerInstanceCache` (Dictionary of session-scoped listener instances)
*   **Private Nested Classes:**
    *   **`Player` & `PlayerState`:** Concrete implementations of `IPlayer` and `IPlayerState` are defined as **private nested classes** within the Kernel. This ensures their setters are physically inaccessible to any code outside the Kernel file.
    *   **`SessionMutator`:** A **private nested class** implementing `ISessionMutator`. This is the "Proxy Mutator" that bridges the gap between the log entry and the private state.
*   **Transactional Apply Flow:** The Kernel exposes a single entry point for persistent mutation: `AddEntryAndUpdateState(GameLogEntryBase entry)`.
*   **Execution Reducer:** All structural execution changes pass through the Kernel's closed `ExecutionTransition` reducer. It compares the request's expected `ExecutionView` with current state, validates the candidate and cleanup hierarchy, prepares any requested stable boundary, applies the complete transition, and only then notifies observers.
*   **Deserialization:** `Deserialize(string json)` (static): Restores a `GameSessionKernel` from a stable recovery snapshot JSON payload. Rehydration restores derived player state, the boundary `GameHistoryLog`, the committed boundary `PendingInstruction`, and the minimal phase cursor directly; it does not replay log entries or restore the live in-memory execution tail.

### `GameSession` (Facade):
A lightweight, stateless wrapper that implements `IGameSession` and delegates all operations to an internal `_gameSessionKernel` instance.

*   **Constructors:**
    *   `GameSession(Guid id, ModeratorInstruction initialInstruction, GameSessionConfig config, ...)`: Standard constructor for new games.
    *   `GameSession(string json)`: Internal Rehydration constructor that deserializes a previously saved stable recovery snapshot into a new session facade.

*   **Public API (IGameSession):** Read-only projection for UI consumers.
    *   `Id` (Guid): Unique identifier (pass-through to `GameSessionKernel.Id`).
    *   `TurnNumber` (int): The current turn number.
    *   `GetCurrentPhase()` (GamePhase): Returns the current main game phase.
    *   `GetPlayers()` (IEnumerable<IPlayer>): Returns all players.
    *   `GetPlayer(Guid id)` (IPlayer): Retrieves a player by ID.
    *   `GetPlayerState(Guid id)` (IPlayerState): Retrieves a player's state by ID.
    *   `GameHistoryLog` (IEnumerable<GameLogEntryBase>): The event log.
    *   `RoleInPlayCount(MainRoleType type)` (int): Returns count of a specific role in play.
    *   `Serialize()` (string): Serializes the latest stable recovery snapshot to JSON for persistence. The payload advances only when a validated execution commit advances the recovery boundary.
*   **Internal API (GameLogic):** Mutation gatekeeper for the rules engine.
    *   **State Mutation Methods** (create and dispatch log entries):
        *   `EliminatePlayer(Guid playerId, EliminationReason reason)`: Eliminates a player by creating a `PlayerEliminatedLogEntry`.
        *   `AssignRole(Guid playerId, MainRoleType role)`: Assigns a role to a single player by creating an `AssignRoleLogEntry`.
        *   `AssignRole(List<Guid> playerIds, MainRoleType role)`: Assigns the same role to multiple players by creating an `AssignRoleLogEntry`.
        *   `ApplyStatusEffect(StatusEffectTypes effectType, Guid playerId)`: Applies a status effect by creating a `StatusEffectLogEntry` with `IsActive = true`.
        *   `RemoveStatusEffect(StatusEffectTypes effectType, Guid playerId)`: Removes a status effect through the same log-backed path with `IsActive = false`.
        *   `TransitionMainPhase(GamePhase newPhase)`: Transitions main phase by creating a `PhaseTransitionLogEntry`.
        *   `PerformNightActionNoTarget(NightActionType type)`: Records a night action with no target.
        *   `PerformNightAction(NightActionType type, Guid targetId)`: Records a night action targeting a single player.
        *   `PerformNightAction(NightActionType type, List<Guid> targetIds)`: Records a night action targeting multiple players.
        *   `PerformDayVote(Guid? reportedOutcomePlayerId)`: Records a vote outcome by creating a `VoteOutcomeReportedLogEntry`. Pass `null` for a tie.
        *   `VictoryConditionMet(Team winningTeam, string description)`: Records victory by creating a `VictoryConditionMetLogEntry`.
    *   **Execution Read:** `Execution` returns one immutable `ExecutionView` containing the coherent Main Phase/Sub-Phase, stage, listener, Pending Instruction, and recovery-cursor projection used by GameLogic. There are no separate execution-field getters or exposed phase-cache interface.
    *   **Keyed Execution Requests** (all delegate to the Kernel reducer):
        *   `CommitExecution(IGameFlowManagerKey key, ExecutionCommit commit)`: Publishes one correlated next Pending Instruction and either retains or advances the validated stable recovery boundary.
        *   `RestoreTransientContinuation(IGameFlowManagerKey key, ExecutionView expected, ...)`: Restores only an authenticated continuation against an explicit expected view.
        *   `TransitionSubPhase(IPhaseManagerKey key, Enum subPhase)`: Requests Sub-Phase movement.
        *   `TryEnterSubPhaseStage(ISubPhaseManagerKey key, string subPhaseStageId)` (bool): Attempts to enter a sub-phase stage atomically. Returns `false` if already in a different stage or if the stage has already been completed.
        *   `CompleteSubPhaseStage(IPhaseManagerKey key)`: Marks the current sub-phase stage as completed.
        *   `TransitionListenerState(IHookSubPhaseKey key, ListenerIdentifier listener, string state)`: Requests listener-state movement.
        *   `ClearCurrentListener(IHookSubPhaseKey key)`: Clears the current listener and its state.
    *   **Listener Instance Management** (session-scoped listener caching):
        *   `GetOrCreateListener<T>(ListenerIdentifier id, Func<T> factory)` (T): Gets or creates a listener instance for this session. Listeners are cached per-session to ensure state machine isolation between games while maintaining consistency within a game.

## `GameSessionQueries` (Rules-Layer Log Queries)

Rule-specific questions over the event log live in `Werewolves.Core.GameLogic.Queries.GameSessionQueries`, not on the `GameSession` state facade. The module operates over `IGameSession` and `GameHistoryLog`, keeping `GameSession` focused on structural state access and mutation commands.

*   **Purpose:** Centralize rules-layer log queries such as current-night targets, dawn eliminations, vote outcomes, unassigned role choices, Stuttering Judge repeat-vote checks, and the session-wide Villager Role Power Suppression commitment and announcement acknowledgment.
*   **Boundary:** `GameSession` retains structural queries (`GetPlayers`, `GetPlayer`, `GetPlayerState`, `GameHistoryLog`, `RoleInPlayCount`) plus mutation methods. Rule concepts such as "last night", "this dawn", and "current vote target" belong in `GameSessionQueries`.
*   **Consumers:** Phase handlers and resolvers call `GameSessionQueries` instead of duplicating log scans or adding new rule-specific methods to `GameSession`.

## `RoleFactionKnowledge` (Rules-Layer Role/Faction Knowledge)

`Werewolves.Core.GameLogic.RoleFactionKnowledge` is the internal concrete static owner of Role Identification orchestration and the closed `MainRoleType` profile shared by Role-Identification Werewolf Faction Agent entailment and initial-agency rules consumers. It exposes `EstablishesInitialWerewolfAgency(MainRoleType)` for those consumers and `CommitRoleIdentification(GameSession, IReadOnlySet<Guid>, MainRoleType)` for caller-validated complete holder sets. It also owns the diagnostic Role-knowledge reads: established Role precedence (`PhysicalCharacterCardRole`, then `ModeratorKnownRole`, then `CurrentRole`), duplicate-preserving possible-Role and unclaimed-Role multiplicity with provenance-sensitive Werewolf-agency narrowing, and earliest Werewolf-agency fact provenance. The public `GameService.GetPossibleRoles(...)` and `GameService.GetEarliestWerewolfAgencyFact(...)` methods remain thin session-lookup adapters to that owner; `GameSessionQueries` retains unrelated raw log queries.

The command constructs the standalone private Role Identification record and, when the profile entails a still-unknown Werewolf Faction Agent fact, appends the separate Faction fact batch immediately afterward with the stable Role-Identification source and the identification's effective boundary. It does not replace an existing Agent fact. For the initial Werewolf Agent-group observation, `RoleFactionKnowledge` derives the opportunity, validates its exact candidates, cardinality, and current boundary, commits the exhaustive living partition, and supplies the stable source and returned boundary. `SimpleWerewolfRole` retains the Moderator interaction workflow, correlation, privacy, and closure handoff.

`GameSession` retains the low-level `CommitRoleIdentificationEntry(...)` and `CommitFactionFactBatch(...)` append gates needed by GameLogic. At those gates it invokes the caller-supplied typed entry factory and validates the resulting record; it does not define the Role Identification payload or own Role-to-agency rules. `Werewolves.Core.StateModels` therefore keeps its one-way assembly boundary and does not reference GameLogic. ADR-0010 and the domain invariants remain normative for Role entailment, while the Moderator interaction contract remains normative for the exchange.

## `NightInteractionResolver` (Rule Engine)

A static helper class that serves as the "Rule Engine" for the Dawn phase, resolving complex interactions between conflicting night actions.

*   **Purpose:** Decouples the `GameFlowManager` from specific role logic (e.g., Witch vs. Defender vs. Infection).
*   **Process:**
    1.  **Input:** Accepts the `GameSession` state.
    2.  **Just-in-time Elder identification:** Before resolution, Dawn requests one private exact-Role identification only when the living Elder holder set is still unknown, Villager Role Power Suppression is inactive, and the current Night contains a qualifying attack that survives the Defender precheck. An infection always passes that precheck; a Night whose qualifying physical attacks are all blocked does not identify the Elder.
    3.  **Resolution:** Reads the ordered current-night committed attempts and resolves their target outcomes in canonical global slot order rather than grouping them by Player or Seating Order.
    4.  **Output:** Records resolution-scoped Dawn victim candidates and applies settled Status Effects. `EliminationCascadeStage` consumes those candidates: pre-reveal interceptions run first, each required generic public reveal commits next, the entire distinct batch is eliminated atomically, and every resulting reaction batch drains before navigation.

*   **Resolution Priority & Special Rules:**
    1.  **Collective Slot:** A committed Accursed Wolf-Father infection globally replaces the collective physical Werewolf attempt; otherwise the collective physical attempt resolves first.
    2.  **White Werewolf Attack:** Resolves after the collective slot.
    3.  **Big Bad Wolf Attack:** Resolves after the White Werewolf attack.
    4.  **Defender Protection:** Blocks each applicable physical Werewolf attack for the whole Night, but never blocks Accursed Wolf-Father infection.
        *   **Exception - Little Girl:** Cannot be protected by the Defender. Protection fails silently.
    5.  **Elder Extra Life:** If an Elder with their extra life remaining is targeted by a qualifying physical Werewolf attack or infection, the extra life is consumed instead of applying that attempt. Defender may have already blocked a physical attack; it never blocks infection. A resisted infection leaves the Elder uninfected, while the confirmed one-use infection remains spent.
    6.  **Witch Save:** Resolves after every physical attack slot. It removes the newly applied physical loss, but does not cure infection or undo Elder protection consumed before a later lethal physical hit.
    7.  **Independent Lethal Actions:** The following actions ignore Defender protection and Witch healing:
        *   **Witch Kill (Death Potion):** Cannot be blocked or prevented.
        *   **Rusty Sword:** The Knight's posthumous revenge attack cannot be blocked.

The authoritative interaction semantics remain in the [game-rule clarifications](../../docs/domain/game-rules-clarifications.md#night-action-resolution); this document records only their architectural placement.

## Role Power Availability

`RolePowerAvailabilityGateway` is the single execution boundary for Role Power attempts. It validates that the attempt, concrete power instance, and optional One-Use Resource share the same source identity, then delegates to a decorated `IRolePowerAvailabilityPolicy` chain.

*   `VillagerRolePowerSuppressionPolicy` decorates the next policy and reads the session-wide suppression fact through `GameSessionQueries.IsVillagerRolePowerSuppressionActive(...)`.
*   Suppression is decided from `RolePowerAttempt.SourceRole.GetRoleGroup()`, so it follows the Role supplying the power rather than the acting Player's Faction or the power category. The policy denies a new Villager Role Power attempt and otherwise delegates unchanged.
*   The policy remains derived from the append-only session log; Roles do not own independent suppression flags. Detailed scope and already-committed-effect semantics live in the [Role Powers and New Moon Assignments clarification](../../docs/domain/game-rules-clarifications.md#role-powers-and-new-moon-assignments).

The chosen architecture utilizes a dedicated `PlayerState` wrapper class. This class contains individual properties (e.g., `HasVotingRight`, `DurableVotingPower`) for all dynamic boolean and data-carrying states, typically using `internal set` for controlled modification. The `Player` class then holds a single instance of `PlayerState`. This approach provides a balance of organization (grouping all volatile states together), strong typing, clear separation of concerns (keeping `Player` focused on identity/role), and strict encapsulation.

## `IPlayer` Interface & `Player` Class

Represents a participant and their core identity information. 

*   **Interface-Based Architecture:** The system uses a `public IPlayer` interface (which extends `IEquatable<IPlayer>` for identity comparison) with a `private nested Player` implementation within `GameSessionKernel`. The `GameSession` exposes these instances as `IPlayer` to the UI (read-only). The `Werewolves.Core.GameLogic` assembly cannot interact with `internal` members if necessary as it lacks access to the `private nested PlayerState` class.
*   **Enhanced Encapsulation through Nesting:** The `Player` class is implemented as a `private class` (not sealed) nested within `GameSessionKernel`, ensuring that only `GameSessionKernel` and its `SessionMutator` can directly access and modify player instances.
*   **`Player` Class Properties:
    *   `Id` (Guid): Unique identifier. 
    *   `Name` (string): Player's name. 
    *   `State` (IPlayerState): Encapsulates all dynamic, *persistent* states affecting the player. This approach keeps the core Player class focused on identity, while grouping volatile states for better organization. **This reflects the player's current, ongoing condition.** 
*   **Design Philosophy:** The `Player` class maintains only identity information, while all game-related dynamic state is managed through the `State` property, ensuring clear separation of concerns. State mutations are controlled exclusively through the `StateMutator` pattern to maintain architectural integrity.

## `IGameHookListener` Interface

Defines the contract for components that respond to game hooks (represents the *rules* of roles and events). 

*   **Interface Definition:** 
    ```csharp 
    internal interface IGameHookListener
    {
        ListenerIdentifier Id { get; }
        HookListenerActionResult Execute(GameSession session, ModeratorResponse input);
    }
    ``` 
*   **Accessibility:** The interface is marked as `internal` to hide implementation details from UI clients and ensure these components are only used within the game logic assembly.
*   **Interaction Contract:**  
    *   The `GameFlowManager` dispatches to all listeners registered for a fired hook by calling `Execute`.
    *   Each listener is responsible for determining if it should act based on game state and cached execution state 
    *   Listeners manage their own state machines by reading the current `ExecutionView`; `HookSubPhaseStage` owns the keyed listener-state requests that pause, resume, and clear dispatch state.
    *   **Return Value Semantics:** The `HookListenerActionResult` communicates the outcome to the dispatcher: 
        *   `HookListenerActionResult.NeedInput(instruction, nextPhase)`: Listener requires input, processing pauses.
        *   `HookListenerActionResult.Complete(nextPhase)`: Listener has finished processing a given game hook, after performing some work.
        *   `HookListenerActionResult.Skip()`: Listener has not done any work, as it detected it has nothing to do for a given game.
*   **Advanced State Machine Features:** The `RoleHookListener<TRoleStateEnum>` stage engine provides:
    *   Declarative state machine definition with runtime validation
    *   Comprehensive error checking and state transition validation
    *   Support for open-ended stages with unknown valid end states at runtime in state flows
    *   Support for end stages that prevent further state changes in state flows
    *   Generic `HookListenerActionResult<T>` for precise state tracking with `NextListenerPhase`
    *   Built-in protection against invalid state transitions and handler overwrites
*   **Declared Role Workflow Seam:** Every interactive Role expresses its moderator exchange as a *declared workflow* rather than as hand-written continuation matching. This is the single continuation authority for interactive Roles: there is no second matcher, and no listener can reclaim a Pending Instruction outside it.
    *   **`IDeclaredRoleWorkflow`**: The seam a Role opts into. It exposes a `WorkflowRuntime` and a `GetWorkflowRuntime(GameHook)` lookup, so one Role can own declared steps on more than one hook (for example a Night cadence plus a Day-hook continuation).
    *   **`RoleWorkflowRuntime`**: Owns one listener's ordered steps for one hook. `Execute` resolves **exactly one** step whose start state matches the current listener state and whose guard passes — the *one-listener-step rule*. Zero or multiple matches is a programming error and throws rather than guessing.
    *   **`IRoleWorkflowStep`**: The step contract. Alongside the waits below, `RoleWorkflowDecisionStep<TState>` performs a silent branch and `RoleWorkflowCompletionStep<TState>` ends the Role's turn on the hook.
    *   **`IRecoverableWait` / `RecoverableWait<TState, TInstruction>`**: A declared pause. A wait knows the instruction it issues, the response type it accepts, the continuation state it resumes into, and — critically — how to recognize *its own* Pending Instruction after Rehydration. Static factories select the durability the exchange needs (`RecoverableWaitDurability`): `Replayable`, `ReplayableWithAcceptedObservationHandoff`, `Durable`, and the domain-durable family `DomainDurable`, `RecurringDomainDurable`, `OneUseDomainDurable`, `ActorSetupCardSpendDomainDurable`, `LoversPairDomainDurable`, and `ActorBorrowedLoversDomainDurable`.
    *   **Recovery classification:** `ClassifyRecoveryCandidate` returns `Unrelated` (this wait did not issue the Pending Instruction), `Authenticated` (it did, and here is the continuation state), or `ClaimedButInvalid` (it claims the instruction but its context does not check out). All three are terminal — a Role never falls back to another matcher — and `ClaimedButInvalid` **fails closed** by throwing. Two listeners resolving the same Pending Instruction is likewise a hard error.
    *   **Committed recovery boundaries:** `TryValidateCommittedRecoveryBoundary` has six typed overloads on both `RoleWorkflowRuntime` and `IRecoverableWait`, one per committed-boundary shape (target-private Role Power, recurring Role Power, one-use Role Power, Actor setup-card spend, Lovers pair, and Actor-borrowed Cupid Lovers). A wait authenticates the boundary it actually declared; the others are not its business.
    *   **`DelegatedRecoverableWait<TState>`**: A wait whose issue and recovery behavior is supplied by the Role itself, for exchanges that do not fit the standard factories. It stubs all six committed-boundary overloads.
    *   **Transience:** None of this is serialized. Consistent with ADR-0002, the active listener, its state, and the active sub-phase stage are transient execution details; Rehydration re-derives the continuation by asking the declared waits which one owns the committed `PendingInstruction`.
*   **Polymorphic Listener Hierarchy:** The architecture provides a small set of abstract base classes that implement `IGameHookListener`:
    *   **`RoleHookListener`**: Universal base for all role listeners, providing core logic and stateless implementation support. Most interactive Roles derive from it directly and add `IDeclaredRoleWorkflow`.
    *   **`RoleHookListener<TRoleStateEnum>`**: Base for stateful roles driven by the older declarative stage engine with runtime validation. Retained for `KnightWithTheRustySwordRole`.
    *   **`CardinalityRoleHolderNightHookListener`**: Declared-workflow base for the shared cadence of Roles whose complete holder set recognizes one another on Night 1 and whose surviving quorum communicates on later Nights (`TwoSistersRole`, `ThreeBrothersRole`).
    *   **`DeclaredRoleIdentificationOnlyHookListener`**: Declared-workflow base for Roles that only need Night 1 identification and no subsequent Night power (`LittleGirlRole`, `BearTamerRole`, `PrejudicedManipulatorRole`).
*   **Concrete Implementations:** Each Role owns its own declared steps. There is deliberately no shared night-cadence builder: a Role's wake, act, and sleep exchanges are spelled out in its own workflow, which is what makes its recovery behavior readable in one place.
*   **TurnNumber Pattern for First-Night-Only Roles:** Roles with actions exclusive to the first night (e.g., Cupid, Thief, WolfHound, WildChild) gate their own declared steps on `session.TurnNumber == 1`. The guard lives on the step, so a first-Night-only Role simply has no executable step on later Nights.

## `IPlayerState` Interface & `PlayerState` Class

Wrapper class holding all dynamic state information for a `Player`. **Implemented with an `IPlayerState` interface and `private nested PlayerState` class within `GameSessionKernel` to provide clean abstraction, support testing, and strict encapsulation.** This improves organization and separation of concerns. Properties use `internal set` to ensure they are managed exclusively through the `StateMutator` pattern as part of the derived cached state pattern, maintaining state integrity. These properties represent the *persistent* or *longer-term* aspects of a player's current state (e.g., holding the Sheriff title, being in love, being infected, having used a specific potion). They reflect the player's ongoing status unless changed by a game event.

*   **Enhanced Encapsulation through Nesting:** The `PlayerState` class is implemented as a `private nested class` within `GameSessionKernel`, ensuring that only `SessionMutator` can directly access and modify player state instances.
*   **StateMutator Pattern Integration:** All state mutations are controlled exclusively through the `ISessionMutator` interface and its `private SessionMutator` implementation. This ensures that only log entries (through their `Apply` methods) can modify player state, maintaining architectural integrity.
*   **Restricted access to PlayerState**: The Player class exposes an IPlayerState property publicly but the PlayerState mutable property is only accessible through a `GetMutableState(IStateMutatorKey)`, ensuring that only `SessionMutator` can access it and its internal setters.
*   **Core Properties:**
    *   `MainRole` (MainRoleType?): The player's main character role type.
    *   `Health` (PlayerHealth): Current health status (Alive, Dead, etc.).
    *   `HasVotingRight` (bool): Whether the player is currently eligible to cast a vote; temporary restrictions may change this without changing the player's durable voting weight.
    *   `DurableVotingPower` (int): The player's nonnegative persistent vote weight. Ordinary voters start at `1`; permanent effects may reduce it to `0`, while future roles can use larger weights without changing the voting-right model.
*   **Unified Status Effects API:**
    *   `HasStatusEffect(StatusEffectTypes effect)` (bool): Checks if a specific status effect is currently active. For standard effects, performs a bitwise `HasFlag` check on the internal `ActiveEffects` field. **Special case for `None`:** When called with `StatusEffectTypes.None`, returns `true` if the player has **zero** active effects (i.e., `ActiveEffects == StatusEffectTypes.None`), and `false` if the player has **any** active effect. This semantic allows querying "does this player have no status effects?" directly.
    *   `GetActiveStatusEffects()` (List<StatusEffectTypes>): Returns all currently active status effects as a list (excluding `None`). Intended for UI consumption to display status effect icons.
*   **Internal Status Effect Storage:**
    *   `ActiveEffects` (StatusEffectTypes, internal): Internal flags field storing all active status effects. Not exposed on the `IPlayerState` interface. Mutations are performed via `AddEffect()`/`RemoveEffect()` internal methods, accessible only through `SessionMutator.SetStatusEffect()`.
*   **Computed Capability Properties (Logic Decoupling):**
    *   `Team` (Team): The player's current allegiance, derived from MainRole and status effects.

**PRD #93 identity model:** The landed state separates Physical Character Card Ownership and zones, current Role, known-or-unknown Faction Beneficiary and Agent facts, Moderator-known Role, and public-reveal state. Unknown is a valid persisted value and never means Simple Villager or non-Agent. Role Identification, Faction Agent Group Observation, Role Reveal, and Permanent Role Swap each commit only their own typed fact.

**Devoted Servant specialization:** The centralized Elimination Cascade pre-reveal reaction owns the public Continue-or-self-reveal window before any target-dependent consequence. An accepted self-reveal durably commits the concrete one-use spend, public Devoted Servant reveal, and correlated private printed-Role instruction. The accepted private transaction moves the public Devoted Servant card to Discarded, moves the target's hidden card to the Servant, creates fresh acquired-Role power state, clears only Charmed, Sheriff, and Town Crier, retains Player-bound infection and any in-force Scapegoat restriction, and returns the unchanged target to the same Vote for Elimination without generic target Role Reveal or transferred-Role behavior. The acquired Role remains Moderator-private and cannot act until its ordinary boundary from the next Night.

## `EventCard` Abstract Class (NOT YET IMPLEMENTED)

Base for New Moon event cards (represents the *rules* of the event). Implements `IGameHookListener`. 

*   `Id` (string): Unique identifier (e.g., "FullMoonRising"). 
*   `Name` (string): Event card name. 
*   `Description` (string): Text description of the event. 
*   `Timing` (EventTiming Enum): Primary trigger time (e.g., `NextNight`, `Immediate`, `PermanentAssignment`). 
*   `Duration` (EventDuration Enum): How long the effect lasts (e.g., `OneNight`, `Permanent`, `UntilNextVote`). 
*   `Execute(GameSession session, ModeratorResponse input)` (HookListenerActionResult): Implements the hook listener interface.

## `ActiveEventState` Class (NOT YET IMPLEMENTED)

Stores runtime state for an active event in `GameSession.ActiveEvents`.

*   `EventId` (string): Matches the `EventCard.Id`. 
*   `CardReference` (EventCard): Reference to the static definition of the card. 
*   `TurnsRemaining` (int?): Countdown for temporary events. 
*   `StateData` (Dictionary<string, object>): Event-specific runtime data.

## Transient Execution Read and Mutation Boundary

`GameSessionKernel` is the sole owner of the mutable execution point. Its private `GamePhaseStateCache` value is an implementation detail for cursor storage and stable DTO conversion; there is no exposed phase-cache interface or Kernel cache accessor.

*   **Coherent Read:** `GameSession.Execution` returns one immutable `ExecutionView` containing the current Main Phase, Sub-Phase, active and completed stages, active listener and listener state, Pending Instruction, and semantic recovery cursors. GameLogic takes one view for a decision instead of assembling state from field-at-a-time getters.
*   **Authorized Requests:** The keyed facade preserves one production owner per request family: `GameFlowManager` publishes Pending Instructions/recovery boundaries and restores authenticated continuations; `PhaseManager<TSubPhaseEnum>` moves Sub-Phases and completes stages; `SubPhaseManager<TSubPhase>` enters stages; and `HookSubPhaseStage` moves or clears listener state.
*   **Reducer Validation:** The closed `ExecutionTransition` family carries an expected view and one complete candidate. The Kernel rejects stale expectations, invalid cursor structure, illegal cleanup, mismatched instruction correlation, and invalid recovery publication before mutating any execution field.
*   **Automatic Cleanup:** A Main Phase change clears Sub-Phase, stage, and listener state; a Sub-Phase change clears stage and listener state; stage completion clears the active stage and listener; and listener transitions cannot escape the active stage.
*   **Persistent Main Phase Path:** `GameFlowManager` chooses every Main Phase and Sub-Phase destination under ADR-0004. A Main Phase destination is persisted only through `PhaseTransitionLogEntry`; applying that entry asks the reducer to install the corresponding transient cursor. There is no direct transient Main Phase setter.
*   **Notifications:** The Kernel emits execution notifications only after the complete transition has passed validation and live state has been updated. Applying `PhaseTransitionLogEntry` emits the Main Phase callback after its cursor consequence; `PhaseManager<TSubPhaseEnum>` requests emit the corresponding Sub-Phase or stage callback; `SubPhaseManager<TSubPhase>` stage entry emits the stage callback; `HookSubPhaseStage` requests emit the listener callback; and a `GameFlowManager` commit emits the Pending Instruction callback after any requested stable-boundary validation and publication. Authenticated continuation restoration installs the stage and listener atomically before emitting those two callbacks in that order.

## `GameFlowManager` Class

Acts as a high-level phase controller and reactive hook dispatcher. It contains the complete, declarative definition of the game's state machine.

*   **Core Components:**
    *   `HookListeners` (static Dictionary<GameHook, List<ListenerIdentifier>>): Declarative mapping of hooks to the ordered list of listeners that respond to them.
    *   `EliminationCascadeReactionRegistrations`: The single ordered registration list for elimination reactions. Each entry binds a stable reaction ID and execution boundary to a listener; reaction implementations do not declare priorities or sort themselves (see ADR 0003). Forced reactions, including Wild Child transformation, drain before interactive reactions such as the Hunter's final shot.
    *   `ListenerFactories` (internal `IReadOnlyDictionary<ListenerIdentifier, Func<IGameHookListener>>` property): Forwarding accessor sourced from `SupportedRoleCatalog`'s admission catalog. That catalog owns every supported Role's active/passive admission and derives listener factories only for active admissions, so adding a supported Role touches one place. Each game session gets its own fresh listener instances via `GameSession.GetOrCreateListener`, ensuring listener state machine isolation between games. Listener *ordering* still lives in `HookListeners` above (see ADR 0003).
    *   `PhaseDefinitions` (static Dictionary<GamePhase, IPhaseDefinition>): Declarative mapping of each main `GamePhase` to its corresponding `PhaseManager`.
*   **Primary Methods:**
    *   `GetInitialInstruction(List<MainRoleType> rolesInPlay, Guid gameId)` (StartGameConfirmationInstruction): **Static factory method for bootstrapping.** Returns the initial instruction required to construct a valid `GameSession`. This pure function performs input validation and generates the startup instruction without creating any game state.
    *   `HandleInput(GameSession session, ModeratorResponse input)` (ProcessResult): **The central state machine orchestrator and silent-transition owner.**
        *   Delegates one phase at a time to `RouteInputToPhaseHandler`.
        *   **Silent Transition Loop:** When a phase returns no instruction after entering another main phase, re-routes through the newly entered phase until an instruction is produced.
        *   Checks for victory at each **phase transition boundary** before any work owned by the newly entered phase runs (see Victory Check Timing below).
        *   Returns a `ProcessResult` with the next instruction.
    *   `RouteInputToPhaseHandler(GameSession session, ModeratorResponse input)` (`private static`): **Routes one processing step to the appropriate phase handler.**
        *   Retrieves the current phase and delegates to the appropriate `IPhaseDefinition` (`PhaseManager`).
        *   **Defensive Null Check:** An invariant check ensures no null instructions escape from non-`MainPhaseHandlerResult` results, as this would indicate a bug in sub-phase or hook stage logic.
    *   `TryGetVictoryInstructions(GameSession session, GamePhase oldPhase, GamePhase newPhase, out ModeratorInstruction?)` (`private static`): Checks for victory conditions **only when transitioning between main phases** (entering Day or Night). This ensures victory is detected at natural game boundaries, preventing scenarios like sending the village to sleep only to immediately announce the game is over.
    *   `CheckVictoryConditions(GameSession session)` (`private static`, returns `(Team WinningTeam, string Description)?`): Evaluates win conditions based on the current game state. Returns `null` if no victory condition is met, or a tuple containing the winning team and description.
*   **Declarative State Machine Architecture:** The game flow is defined by a hierarchy of declarative components:
    *   **`PhaseManager<TSubPhaseEnum>`**: Manages the flow between sub-phases for a single main `GamePhase`. It contains a dictionary of `SubPhaseManager`s. Each `PhaseManager` is **phase-aware**: it determines which `GamePhase` it manages by finding itself in the `PhaseDefinitions` dictionary (cached after first lookup). This enables clean exit when a silent main phase transition occurs—if the session's current phase no longer matches the owned phase, the manager returns immediately with a `MainPhaseHandlerResult(null, currentPhase)`, allowing `HandleInput` to check the transition boundary before processing the new phase.
    *   **`SubPhaseManager<TSubPhase>`**: Defines a single sub-phase. It contains a linear sequence of `SubPhaseStage`s that are executed in order. It also declares all valid transitions to other sub-phases or main phases.
    *   **`SubPhaseStage`**: An abstract class representing a single, **atomic, non-re-entrant** unit of work. `SubPhaseManager<TSubPhase>` requests stage entry through its keyed facade operation, and the Kernel reducer ensures each stage is executed at most once per Sub-Phase entry.
        *   `LogicSubPhaseStage`: Executes a custom logic handler.
        *   `HookSubPhaseStage`: Fires a `GameHook` and dispatches to all registered listeners.
        *   `EliminationCascadeStage`: Drains one scoped Dawn or Day elimination cascade. Registered pre-reveal reactions run in deterministic order before generic Role Reveal or another target-dependent consequence; the stage then commits every Player in the current distinct batch before forced and interactive reactions run, admits chained batches until empty, and may pause for Moderator input. A newly Eliminated Hunter is evaluated through the shared Role Power availability boundary after forced reactions drain; a non-empty legal roster produces one mandatory exact-one target instruction, while an empty roster completes silently. The shot creates a child batch in the same cascade. The stage never navigates.
        *   `NavigationSubPhaseStage`: A stage that results in a transition to a new sub-phase or main phase. Created via factory methods (`NavigationEndStage`, `NavigationEndStageSilent`) as the required final stage for any sub-phase.
*   **State Machine Validation:** The architecture provides strong runtime guarantees:
    *   **Transition Validation:** All transitions are validated against the `PossibleNextSubPhases` and `PossibleNextMainPhaseTransitions` sets defined in the `SubPhaseManager`. An illegal transition throws an `InvalidOperationException`.
    *   **Stage Atomicity:** The `GameSession.TryEnterSubPhaseStage` method prevents any stage from being executed more than once within a single Sub-Phase activation, eliminating the need for idempotent handlers.
*   **Navigation and Key Ownership:** `GameFlowManager` remains the single visible owner of every Main Phase and Sub-Phase destination under ADR-0004. Its `IGameFlowManagerKey` authorizes correlated Pending Instruction/stable-boundary commits and authenticated continuation restoration; it does not provide a second Main Phase path.

## `GameService` Class

Orchestrates the game flow based on moderator input and tracked state. **Delegates state machine management to `GameFlowManager` while handling high-level game logic and external interfaces.** 

*   **Public Methods:** 
    *   `StartNewGame(GameSessionConfig config)` (StartGameConfirmationInstruction): **Orchestrates atomic game initialization.** Validates the configuration, generates a unique game ID, retrieves the initial instruction from `GameFlowManager.GetInitialInstruction`, constructs a `GameSession` with the ID, instruction, and config, stores the session, and returns the instruction.
    *   `ProcessInstruction(Guid gameId, ModeratorResponse input)` (ProcessResult): **The central entry point for processing moderator actions.** 
        *   Retrieves the current `GameSession`, validates the response against the exact pending instruction, and then delegates to `GameFlowManager.HandleInput`.
        *   `GameService` owns consume-time correlation, response-type, and complete payload validation; `GameFlowManager` owns state-machine stage and transition validation.
        *   Returns the `ProcessResult` from the state machine, containing either the next instruction or a failure.
        *   An emitted elimination-reaction input is a stable recovery boundary. Rehydration restores the exact committed Hunter selector and resumes it without re-evaluating Role Power availability, while stale or structurally mismatched selector context fails closed. Devoted Servant uses the same stable-response rule at two semantic boundaries: after the accepted public self-reveal it resumes only the correlated private printed-Role record, and after the accepted swap it resumes target resolution without reopening Continue or duplicating the exchange.
        *   **Session Cleanup:** A `FinishedGameConfirmationInstruction` remains pending until its correlated Continue acknowledgment is accepted; that successful acknowledgment removes the session from the active sessions list.
    *   `GetCurrentInstruction(Guid gameId)` (ModeratorInstruction?): Retrieves the current Pending Instruction through the service boundary.
    *   `GetGameStateView(Guid gameId)` (IGameSession?): Returns the game state via the read-only `IGameSession` interface. This hides the internal mutation methods present on the concrete object, ensuring the UI cannot modify state.
    *   `RehydrateSession(string serializedSession)` (Guid): Restores a game session from its stable recovery snapshot and adds it to the active session collection. Returns the session's GUID.
*   **Internal Logic:** 
    *   `EnsureResponseMatchesPendingInstruction(ModeratorInstruction pendingInstruction, ModeratorResponse response)`: Rejects stale correlation identities, mismatched response types, malformed or incomplete payloads, and non-canonical option order before state can change.
    *   Relies on `GameFlowManager` for all state machine operations. 
    *   Victory condition checking is automatically handled by `GameFlowManager`.

## `GameSessionConfig` Class

Encapsulates and validates all configuration parameters required to start a new game session. This class enforces game configuration constraints at initialization time, preventing invalid game setups.

> **Landed Role Lock-In contract:** `GameSessionConfig` stores the explicit `RoleLockIn` settled in [ADR-0017](../../docs/adr/0017-thief-offer-is-committed-before-the-physical-deal.md). Its `Roles` property is a read-only printed-Role projection of the Deal Pool for compatibility; it is not the full physical inventory. The non-Thief list constructor may derive an implicit all-Deal-Pool lock-in, while Thief requires an explicit Player-count Deal Pool with exactly one Thief plus ordered, distinct, private non-Thief `Offer1` and `Offer2` instances.

*   **Purpose:** Consolidates game initialization parameters (player names and roles) into a single validated object. This replaces the previous approach of passing `List<string>` and `List<MainRoleType>` directly to `GameService.StartNewGame`, providing improved type safety, validation, and maintainability.

*   **Properties:**
    *   `Players` (List<string>): List of player names in clockwise seating order.
    *   `Roles` (IReadOnlyList<MainRoleType>): Compatibility projection of the Deal Pool's printed Roles.
    *   `ActorSetupCards` (`ActorSetupCards`): The Actor's three setup cards as a separate setup artifact outside the Role Composition.
    *   `RoleLockIn` (`RoleLockIn`): Versioned physical Character Card inventory, Player-count Deal Pool, and optional ordered `Offer1`/`Offer2` slots.

*   **Validation & Constraints:**
    *   **Construction:** The `RoleLockIn` constructor validates inventory and zone integrity; `GameSessionConfig` then validates the roster, Deal Pool, and conditional setup before a Game Session can start.
    *   **`TryGetConfigIssues(List<string> players, List<MainRoleType> roles, ActorSetupCards actorSetupCards, out List<GameConfigValidationError> issues)` (static):** Diagnostic validation for the pre-lock-in physical selection. The two-argument overload uses no Actor Setup Cards.
    *   **`TryGetPlayerConfigIssues(List<string> players, out List<GameConfigValidationError> issues)` (static):** Public helper for validating player-related configuration independently of Roles. Checks for non-unique names (case-insensitive) and the Supported Player Count of 5-30.
    *   **`TryGetPhysicalSetupIssues(int playerCount, IReadOnlyList<MainRoleType> roles, ActorSetupCards actorSetupCards, out List<GameConfigValidationError> issues)` (static):** Applies the same physical-setup diagnostics when Player identities are not part of the input.
    *   **`GetExpectedRoleCount(int playerCount, List<MainRoleType> roles)` (static):** Compatibility helper for the expected pre-lock-in Role Composition card count. Thief adds two Character Cards; Actor Setup Cards are excluded.

*   **Validation Rules:**
    *   **Player Count:** 5-30 Players are supported.
    *   **Player Names:** All player names must be unique (case-insensitive).
    *   **Role Lock-In Zones:** A non-Thief inventory contains one Deal Pool card per Player. A Thief inventory contains `PlayerCount + 2` cards, a Player-count Deal Pool with exactly one Thief, and distinct non-Thief `Offer1`/`Offer2` instances; every inventory card occupies exactly one locked zone.
    *   **Hard-Aligned Coverage:** At least one hard-aligned Villager Role and one hard-aligned Werewolf Role are required; Simple Villager and Simple Werewolf are not mandatory by name.
    *   **Actor Setup Cards:** When Actor is present, exactly three eligible hard-aligned Villager Roles with actionable individual powers must be provided outside the Role Composition. A selected Role cannot also be an Actor Setup Card; Simple Villager, Villager-Villager, Two Sisters, and Three Brothers are ineligible.
    *   **Per-Role Constraints:** Each role has specific count constraints defined in `RoleCountConstraints` dictionary:
        *   Simple Werewolf and Simple Villager use `Any`; hard-aligned coverage supplies the Faction requirement.
        *   Special roles use `SingleOptional` constraints (e.g., Seer, Cupid, Witch).
        *   Group roles like Two Sisters and Three Brothers have exact optional count constraints (e.g., `ExactOptional(2)` for Two Sisters).

*   **Role Count Constraints Dictionary:**
    *   Maps `MainRoleType` to `NumberRangeConstraint` for validation.

*   **Validation Error Types:**
    *   `TooFewPlayers`: Fewer than 5 Players provided.
    *   `TooManyPlayers`: More than 30 Players provided.
    *   `NonUniquePlayerNames`: Duplicate player names found.
    *   `TooFewRoles`: Insufficient roles for the player count.
    *   `TooManyRoles`: Excessive roles for the player count.
    *   `RoleCountMismatch`: A specific role violates its per-role constraint.
    *   `MissingExtraThiefRoles`: Insufficient extra roles for Thief role.
    *   `ActorSetupCardCountMismatch`: Actor does not have exactly three separate setup cards.
    *   `ActorSetupCardInRoleComposition`: An Actor Setup Card repeats a Role selected in the Role Composition.
    *   `IneligibleActorSetupCard`: An Actor Setup Card is not an eligible hard-aligned Villager Role with an actionable individual power.
    *   `MissingHardAlignedWerewolf`: The Role Composition has no hard-aligned Werewolf Role.
    *   `MissingHardAlignedVillager`: The Role Composition has no hard-aligned Villager Role.

## `NumberRangeConstraint` Structure

Defines flexible constraints for number ranges, used in role count validation and player selection constraints in moderator instructions.

*   **Structure:**
    *   `Minimum` (int): The minimum value in the range.
    *   `Maximum` (int): The maximum value in the range.
    *   `IsOptional` (bool): When `true`, the constraint also allows a count of 0 (i.e., "either zero OR within the range"). When `false`, the constraint requires the count to be within the range (minimum to maximum inclusive).

*   **Optionality Concept:** The `IsOptional` field enables modeling of scenarios where an action or selection is optional. For example:
    *   `SingleOptional` (0 or 1): Used for vote outcomes that allow ties (0 selected) or a single winner.
    *   `ExactOptional(2)` (0 or exactly 2): Used for roles like Two Sisters that either don't appear in the game or appear as exactly two players.
    *   This eliminates the need for separate "Optional" and "Required" variants of constraints, providing a unified, flexible approach.

*   **Factory Methods:**
    *   `Range(int minimum, int maximum)`: Creates a non-optional range constraint.
    *   `Exact(int count)`: Creates a constraint for an exact count (equivalent to `Range(count, count)`).
    *   `Single`: Shorthand for `Exact(1)`.
    *   `AtLeast(int count)`: Creates a constraint with no upper bound.
    *   `RangeOptional(int minimum, int maximum)`: Creates an optional range constraint.
    *   `ExactOptional(int count)`: Creates an optional exact count constraint.
    *   `SingleOptional`: Shorthand for `ExactOptional(1)` — commonly used for vote outcomes and optional roles.
    *   `Any`: Matches any count (0 to int.MaxValue).

*   **Validation Methods:**
    *   `Enforce<T>(ICollection<T> value)`: Validates a collection against the constraint. Throws `InvalidOperationException` if validation fails.
    *   `IsValid<T>(ICollection<T> value)`: Diagnostic method that returns `true` if the collection satisfies the constraint, `false` otherwise.

## `ProcessResult` Structure

Represents the outcome of a processing operation.

*   `IsSuccess` (bool): True if processing completed successfully.
*   `ModeratorInstruction` (ModeratorInstruction?): The next instruction for the moderator.
*   **Factory Methods:**
    *   `Success(ModeratorInstruction)`: Creates a successful result.
    *   `Failure(ModeratorInstruction)`: Creates a failure result. **Note:** Only used by `GameService` when a game session cannot be found.

## `PhaseHandlerResult` Hierarchy

A hierarchy of records represents the outcome of a `SubPhaseStage`'s execution, signaling the intended next step to the `PhaseManager`.

*   `PhaseHandlerResult(ModeratorInstruction? ModeratorInstruction)`: Abstract base record.
*   `MajorNavigationPhaseHandlerResult`: Abstract record for results that cause a transition.
    *   `MainPhaseHandlerResult(ModeratorInstruction?, GamePhase)`: Signals a transition to a new main phase.
    *   `SubPhaseHandlerResult(ModeratorInstruction?, Enum)`: Signals a transition to a new sub-phase within the current main phase.
*   `StayInSubPhaseHandlerResult(ModeratorInstruction?, bool StageComplete)`: Signals that the state machine should remain in the current sub-phase.
    *   **`StageComplete = true`:** The current stage is marked complete and will not be re-entered. If `ModeratorInstruction` is `null`, the `PhaseManager` immediately executes the next stage. Created via `CompleteSubPhaseStage(instruction)`.
    *   **`StageComplete = false`:** The current stage remains active for re-entry on the next input. Used when pausing mid-stage to await moderator input (e.g., during hook listener execution). Created via `PauseSubPhaseStage(instruction)`.

## Hook System Components
 
*   **`GameHook` Enum:** Defines all possible hook points in the game flow:
    *   `NightMainActionLoop`
    *   `OnVoteConcluded`
    *   `DawnMainActionLoop`

*   **`ListenerIdentifier` Record:** Unified identifier for different types of hook listeners: 
    ```csharp 
    public record ListenerIdentifier 
    { 
        public GameHookListenerType ListenerType { get; } // Enum: MainRole, SpiritCard, StatusEffect 
        public string ListenerId { get; } // Stores the MainRoleType, StatusEffectTypes, or EventCardType enum value as string for better debugging/logging 
    } 
    ```
    *   **Factory Methods:**
        *   `Listener(MainRoleType)`: Creates identifier for main role listeners.
        *   `Listener(StatusEffectTypes)`: Creates identifier for status effect listeners (e.g., Sheriff, Lovers).
    *   **Implicit Conversions:** Supports implicit conversion from `MainRoleType` and `StatusEffectTypes` for convenience. 

*   **`HookListenerActionResult` Class:** Standardized return type for `IGameHookListener.Execute`: 
    *   `NeedInput(instruction)`: Listener requires input, processing pauses.
    *   `Complete()`: Listener finished, processing continues.
    *   `Skip()`: Listener has no work to do.
 
## State Machine Validation
*   **Purpose:** Validation is split at the existing ownership boundary: `GameFlowManager` and the phase managers validate declarative navigation, while the Kernel reducer validates each execution-state candidate.
*   **Validation Features:** 
    *   **Phase Transition Validation:** Every phase transition is validated against the `PhaseTransitionInfo` defined in the source phase's `PossibleTransitions` list. 
    *   **Hook Dispatch Validation:** Ensures hooks are fired in correct sequence and listeners respond appropriately. 
    *   **Execution Transition Validation:** Compares the request's expected `ExecutionView` with current state and validates the complete candidate, cleanup hierarchy, Pending Instruction correlation, and optional stable recovery boundary before mutation or notification.
    *   **Internal Error Detection:** Catches internal state machine inconsistencies and provides detailed error messages for debugging. 
*   **Error Handling:** Validation failures result in exceptions, as they indicate unrecoverable logic errors.

 
## `ModeratorResponse` Class 
Data structure for communication FROM the moderator. 

The fields below describe the current public shape. PRD #110 established instruction correlation, one-way Continue acknowledgments, immutable response payloads, and semantic option IDs with separately localized labels. PRD #93 deliberately migrates the remaining overloaded cases before new Role flows depend on them: #113 owns distinct exact-Role Identification and public Role Reveal responses; #121 owns Faction Agent Group Observation; #111 owns acting-Player/source-power/resource identity; #135/#136 own physical card-instance and zone payloads; and #140 owns Public Group Partition input.
*   `InstructionId` (`Guid`): Identifies the exact pending instruction that produced the response. `GameService` validates this identity, the response type, and the complete payload before any game state or session-lifecycle mutation.
*   `Type` (enum `ExpectedInputType`): Indicates which optional field below is populated. 
*   `SelectedPlayerIds` (`IReadOnlySet<Guid>?`): IDs of selected Players. Currently used for exact-Role identification and Vote outcome; an empty set represents a tie when the instruction permits it.
*   `AssignedPlayerRoles` (`IReadOnlyDictionary<Guid, MainRoleType>?`): Player IDs mapped to main roles. Assignments must contain exactly every Player requested by the instruction, though this field still conflates assignment, private identification, and public reveal until #113.
*   `SelectedOptionIds` (`IReadOnlyList<string>?`): Machine-stable option IDs in the instruction's explicit semantic order. Rendered labels live separately on `ModeratorOption` and never drive Core or simulator decisions.
*   **Continue:** `ExpectedInputType.Continue` carries no Boolean or gameplay payload. A genuine yes/no branch requires its own semantic instruction rather than overloading Continue.
*   **Construction:** External consumers create responses through `ModeratorInstruction` subclass `CreateResponse()` methods. Instruction and response collections are defensively copied and exposed read-only.

**Design Note on Vote Input:** 
 
A key design principle for moderator input, especially during voting phases, is minimizing data entry to enhance usability during live gameplay. The application is designed to guide the moderator through the *process* of voting (whether standard or event-driven like Nightmare, Great Distrust, Punishment), reminding them of the relevant rules. However, the actual vote tallying is expected to happen physically among the players. 
 
Consequently, the `ModeratorResponse` structure requires the moderator to provide only the final *outcome* of the vote: exactly one living target via `SelectedPlayerIds`, or an empty selection for a tie. It does not collect ballots, per-Player choices, or vote totals. This approach significantly reduces the moderator's interaction time and minimizes the potential for input errors. The application functions primarily as a streamlined state tracker and procedural guide, accepting the loss of granular vote data in its logs as an acceptable trade-off for improved real-time usability.
 

 
## `ModeratorInstruction` Class Hierarchy
Polymorphic instruction system for communication TO the moderator. **Assembly Location:** The abstract base class `ModeratorInstruction` and all concrete implementations are located in `Werewolves.Core.StateModels.Models.Instructions`. This placement allows `GameSession` to accept instructions as constructor parameters without circular dependencies.

*   **Encapsulation & Serialization:** Instruction constructors are marked `internal` (or `protected` for the base class) to prevent UI clients from injecting arbitrary instructions—only the trusted `GameFlowManager` should create instruction instances. To support JSON deserialization while preserving this encapsulation, all instruction types use the `[JsonConstructor]` attribute on their internal constructors. Since the custom `ModeratorInstructionConverter` resides in the same assembly (`Werewolves.Core.StateModels`), it can access internal constructors during deserialization.
*   **Abstract Base Class:** 
    *   `InstructionId` (`Guid`): Stable identity generated once and preserved through serialization/rehydration so only a response to the exact pending instruction can be consumed.
    *   `Semantic` (`ModeratorInstructionSemantic`): Machine-stable, execution-only gameplay meaning used by a named Simulator Capability's `HeadlessResponsePolicy` before response-shape dispatch. It is excluded from the session wire contract; generic rehydrated instructions therefore use `Unspecified`, while specialized Start/Finished types reconstruct their fixed semantics.
    *   `PublicAnnouncement` (string?): Text to be read aloud or displayed publicly to all players. 
    *   `PrivateInstruction` (string?): Text for moderator's eyes only, containing reminders, rules, or guidance. 
    *   `AffectedPlayerIds` (IReadOnlyList<Guid>?): Optional: Player(s) this instruction primarily relates to.
    *   `SoundEffects` (IReadOnlyList<SoundEffectsEnum>): Sound effects to play with this instruction. Only listed effects should play; all others should stop. *(Placeholder for future implementation)*
*   **Concrete Implementations:** Each instruction type has its own `CreateResponse` method for validation and response creation:
*   **`ConfirmationInstruction`:** Creates a one-way Continue acknowledgment with no Boolean payload or false branch.
*   **`SelectPlayersInstruction`:** For player selection with `NumberRangeConstraint` (defining min/max counts).
*   **`AssignRolesInstruction`:** Requires a complete mapping for exactly the requested Players. #113 replaces the remaining assignment/identification/reveal conflation.
*   **`SelectOptionsInstruction`:** Carries an ordered list of `ModeratorOption` values, each with a stable ID and a separately rendered label; duplicate labels are allowed but IDs must be unique.

## Enums

### Core Game Flow Enums
*   `GamePhase`: `Night`, `Dawn`, `Day`.
*   `GameHook`: `NightMainActionLoop`, `OnVoteConcluded`, `DawnMainActionLoop`.
*   `PlayerHealth`: `Alive`, `Dead`. 
*   `ExpectedInputType`: `None`, `PlayerSelection`, `AssignPlayerRoles`, `OptionSelection`, `Continue`, `FinishedGame`.

### Faction and Team Enums
*   `Faction`: `Villager`, `Werewolf`, `WhiteWerewolf`. Faction facts use this enum independently for each Player's exclusive Beneficiary and operational Agent knowledge; White Werewolf is a White Werewolf Beneficiary and a Werewolf Agent.
*   `Team`: `Villagers`, `Werewolves`. This legacy setup-alignment vocabulary does not substitute for Faction Beneficiary/Agent facts or typed victory results.

### Role Enums
*   `MainRoleType`: Comprehensive list of all roles (Werewolves, Villagers, Ambiguous, Loners, New Moon).
*   `RoleGroup`: `Werewolves`, `Villagers`, `Ambiguous`, `Loners`, `NewMoon`. Logical groupings for categorizing roles. Used by `MainRoleTypeExtensions.GetRoleGroup()`.

### Night Action & Day Action Enums
*   `NightActionType`: `Unknown`, `WerewolfVictimSelection`, `BigBadWolfVictimSelection`, `WhiteWerewolfVictimSelection`, `AccursedWolfFatherInfection`, `SeerCheck`, `FoxCheck`, `WitchSave`, `WitchKill`, `DefenderProtect`, `PiperCharm`, `RustySword`, `ThiefSwap`, `ActorEmulate`, `WildChildModel`, `CupidLink`, `WolfHoundChoice`.
*   `DayPowerType`: `Unknown`, `JudgeExtraVote`, `DevotedServantSwap`, `TownCrierCardReveal`.
*   `StatusEffectTypes` (Flags enum): Unified enum for all persistent status effects that can be applied to a player. Combines what was previously split between status effects and secondary roles.
    *   **Persistent conditions:** `None`, `ElderProtectionLost`, `LycanthropyInfection`, `WildChildChanged`.
    *   **Hookable status effects:** `Sheriff`, `Lovers`, `Charmed`, `TownCrier`, `Executioner`.
    *   **Note:** This is a `[Flags]` enum to allow multiple status effects to be active simultaneously (e.g., Sheriff + Infected + Charmed).

### Elimination Enum
*   `EliminationReason`: `Unknown`, `WerewolfAttack`, `WitchKill`, `HunterShot`, `LoversHeartbreak`, `RustySword`, `ScapegoatSacrifice`, `EventElimination`, `DayVote`.

### Hook Listener Enums
*   `GameHookListenerType`: `MainRole`, `SpiritCard`, `StatusEffect`. Used to distinguish between different categories of listeners.
*   `HookListenerOutcome`: `Skip`, `NeedInput`, `Complete`. Communicates listener state machine result back to GameFlowManager.

### Role State Machine Enums
*   `StandardNightRoleState`: `AwaitingAwakeConfirmation`, `AwaitingTargetSelection`, `AwaitingSleepConfirmation`, `Asleep`. Continuation states for the "wake → select target → sleep" Night flow, used by `WildChildRole`'s declared workflow.

### Sub-Phase Enums
*   `NightSubPhases`: `Start`.
*   `DawnSubPhases`: `CalculateVictims`, `AnnounceVictims`, `Finalize`.
*   `DaySubPhases`: `Debate`, `DetermineVoteType`, `NormalVoting`, `AccusationVoting`, `FriendVoting`, `HandleNonTieVote`, `ProcessVoteOutcome`, `Finalize`.
*   `VictorySubPhases`: `Complete`.

## Extensions

### `MainRoleTypeExtensions` Class
Located in `Werewolves.Core.StateModels/Extensions/MainRoleTypeExtensions.cs`. Provides extension methods for the `MainRoleType` enum.

*   **`GetRoleGroup(this MainRoleType role)` (RoleGroup):** Returns the logical group that the specified role belongs to. Categorizes all 28 roles:
    *   **Werewolves:** SimpleWerewolf, BigBadWolf, AccursedWolfFather
    *   **Villagers:** SimpleVillager, VillagerVillager, Seer, Cupid, Witch, Hunter, LittleGirl, Defender, Elder, Scapegoat, VillageIdiot, TwoSisters, ThreeBrothers, Fox, BearTamer, StutteringJudge, KnightWithRustySword, Actor
    *   **Ambiguous:** Thief, DevotedServant, WildChild, WolfHound (roles that can change sides or have flexible allegiance)
    *   **Loners:** Angel, Piper, PrejudicedManipulator, WhiteWerewolf (roles with independent win conditions)
    *   **NewMoon:** Gypsy (expansion roles)
*   **`IsHardAlignedVillager` / `IsHardAlignedWerewolf`:** Classify stable setup allegiance independently of UI Role Group. White Werewolf is grouped with Loners for its solo win condition; its holder remains a Werewolf Faction Agent and White Werewolf Faction Beneficiary, and the Role is not hard-aligned Werewolf. Actor is hard-aligned Villager.
*   **`IsEligibleActorSetupCard`:** Identifies hard-aligned Villager Roles whose individual powers can be used as Actor Setup Cards.
*   **Usage:** `GetRoleGroup` supports UI grouping and role selection; the explicit hard-alignment predicates support setup validity.
*   **Initial Werewolf Agency:** The former public StateModels `EstablishesInitialWerewolfAgency` extension has been removed. Production rules consumers use the single closed profile owned by `Werewolves.Core.GameLogic.RoleFactionKnowledge`.

# Game Loop Outline (Declarative Sub-Phase Architecture)

1.  **Bootstrap (Pre-Phase):**
    *   `GameService.StartNewGame` is called with a `GameSessionConfig` instance containing validated player names and roles.
    *   `GameService` generates a unique `Guid` for the game session.
    *   `GameService` calls `GameFlowManager.GetInitialInstruction(rolesInPlay, gameId)` to obtain the startup instruction.
    *   `GameService` constructs `GameSession` with the ID, instruction, and config, ensuring atomic validity.
    *   The initial instruction (`StartGameConfirmationInstruction`) is returned to the caller.
    *   When the moderator confirms this instruction, the game begins directly in the Night phase.

2.  **Night Phase (`GamePhase.Night`):**
    *   The `PhaseManager` for `Night` is activated. It begins executing the `NightSubPhases.Start` sub-phase.
    *   The `SubPhaseManager` for `Start` runs its sequence of atomic stages:
        1.  A `LogicSubPhaseStage` issues the "Village goes to sleep" instruction and increments the turn number.
        2.  A `HookSubPhaseStage` fires the `GameHook.NightMainActionLoop`. It iterates through all registered role listeners (`SimpleWerewolfRole`, `SeerRole`, etc.), calling `Execute` on each.
        3.  If a listener needs input, it returns `HookListenerActionResult.NeedInput`, which becomes a `StayInSubPhaseHandlerResult` via `PauseSubPhaseStage(instruction)`. The stage remains active for re-entry, and the `PhaseManager` pauses.
        4.  Once all listeners complete, the `HookSubPhaseStage`'s `onComplete` delegate runs, returning `CompleteSubPhaseStage(null)` to mark the stage complete.
        5.  The final `EndNavigationSubPhaseStage` executes, returning a `MainPhaseHandlerResult` to transition to `GamePhase.Dawn`.

3.  **Dawn Phase (`GamePhase.Dawn`):**
    *   The `PhaseManager` for `Dawn` is activated, starting at `DawnSubPhases.CalculateVictims`.
    *   **Calculate Victims:** The `NightInteractionResolver` processes all Night actions, resolves conflicts (Witch vs Defender vs Infection), applies settled Status Effects, and records the pending Dawn elimination candidates. It navigates either to `AnnounceVictims` or `Finalize`.
    *   **Announce Victims:** `EliminationCascadeStage` reveals the current distinct victim batch, commits every elimination in that batch, and then runs the centrally ordered forced and interactive reactions. Reaction-caused eliminations become child batches in the same scoped cascade. Only after the cascade is empty does the following navigation stage advance to `Finalize`.
    *   **Finalize:** After all Dawn elimination cascades are empty, the sub-phase prepares victory facts and fires `GameHook.DawnMainActionLoop` in its declared order (Bear Tamer, Gypsy, then Town Crier). It transitions to `GamePhase.Day` only after every listener completes; the Dawn Victory Check Window is evaluated at that transition.

4.  **Day Phase (`GamePhase.Day`):**
    *   The `PhaseManager` for `Day` starts at `DaySubPhases.Debate`.
    *   **Debate:** Issues an instruction for discussion and transitions to `DetermineVoteType`.
    *   **Determine Vote Type:** Determines what's the appropriate vote type, checking for active events or modifiers (defaults to `NormalVoting` sub-phase).
    *   **Normal Voting:** Records a standard village vote. A tie advances directly to `ProcessVoteOutcome`; a selected living Player advances to `HandleNonTieVote`.
    *   **Accusation Voting:** *(Not yet implemented)* Reserved for accusation-based voting mechanics.
    *   **Friend Voting:** *(Not yet implemented)* Reserved for friend-based voting mechanics (e.g., Angel event).
    *   **Handle Non Tie Vote:** Runs a fresh vote-scoped `EliminationCascadeStage`. It reveals the target, settles pre-commit interception such as Village Idiot lynching immunity, commits any resulting Day Vote elimination, drains reactions, and only then transitions to `ProcessVoteOutcome`.
    *   **Process Vote Outcome:** Runs only after the vote-scoped Elimination Cascade is complete, then fires `GameHook.OnVoteConcluded`. The Elder listener can commit session-wide Villager Role Power Suppression and pause for its public confirmation here; only after every listener completes does the navigation stage start an already-committed Consecutive Vote or advance to `Finalize`.
    *   **Finalize:** Transitions to `GamePhase.Night`. Victory is checked at this transition.

# Game Logs 

**Core Principle:** The `GameHistoryLog` serves as the single, canonical source of truth, containing an append-only record of events that determine the game state. All other game state is treated as derived and either cached or computed on-the-fly.

The chosen approach is an abstract base class (`GameLogEntryBase`) providing universal properties (`Timestamp`, `TurnNumber`, `CurrentPhase`) combined with distinct concrete derived types (preferably records) for each specific loggable event. This flat hierarchy significantly reduces boilerplate for universal fields via the base class while maintaining strong type safety, clarity, and maintainability through specific derived types.

## Mutation Mechanism

*   **`Apply(ISessionMutator)`:** Internal method called by the Kernel when a log entry is being committed. It invokes `InnerApply` and then appends the entry to the log via the mutator.
*   **`InnerApply(ISessionMutator)`:** Abstract protected method that each derived log entry must implement. This is where the actual state mutation logic resides. The method receives an `ISessionMutator` to perform changes and returns the log entry (potentially modified, e.g., if `TurnNumber` changed during application).

`PhaseTransitionLogEntry` is the only persistent Main Phase mutation path. `GameFlowManager` chooses the destination, `GameSession` constructs the entry, and the Kernel preflights and applies it through `SessionMutator`. The mutator sends the entry's transient cursor consequence through the same execution reducer, so stale-source and cursor validation happen before the Main Phase changes. The Main Phase observer callback follows that cursor mutation; the log-entry callback follows append of the applied entry.

**Core Principle:** The `GameHistoryLog` serves as the single, canonical source of truth, containing an append-only record of events that determine the game state.

## Implemented Log Entries

1.  **`AssignRoleLogEntry`:** Currently records a batch `MainRoleType` for one or more Players and conflates private identification with public reveal. PRD #93/#113/#121/#135 replaces that use with distinct log-backed facts for exact-Role Identification, Faction Agent Group Observation, public Role Reveal, physical card zones, and current-Role transitions; none may stand in for another.
2.  **`DayActionLogEntry`:** Records actions taken during the day (e.g., Sheriff appointment, specific day powers).
3.  **`NightActionLogEntry`:** Records non-deterministic player choices made during the night (e.g., Seer check, Werewolf attack, Witch potion).
4.  **`PhaseTransitionLogEntry`:** Records the transition between main game phases (`Night` -> `Dawn`, etc.).
5.  **`PlayerEliminatedLogEntry`:** Records the elimination of a player and the reason (Vote, Attack, etc.).
6.  **`StatusEffectLogEntry`:** Records application or removal of a status effect (e.g., `ElderProtectionLost`, `LycanthropyInfection`, `WildChildChanged`) through its typed `IsActive` value.
7.  **`VictoryConditionMetLogEntry`:** Records that a specific team has met their win condition.
8.  **`VoteOutcomeReportedLogEntry`:** Records the result of a day vote (who was eliminated, or if it was a tie).
9.  **`RoleRevealLogEntry`:** Records one public Role Reveal fact for the complete revealed batch.
10. **`EliminationCascadeBatchResolvedLogEntry`:** Records a scoped requested batch and its actual committed eliminations. A zero-commit Vote interception remains durable while its consequence announcement is pending.
11. **`EliminationCascadeReactionCompletedLogEntry`:** Records a reaction's scoped triggering batch and the exact elimination candidates it admitted, allowing recovery to skip the completed side effect and reconstruct its child work.
12. **`EliminationCascadeCompletedLogEntry`:** Records that one Dawn or vote-scoped cascade drained completely.
13. **`VillagerRolePowerSuppressionCommittedLogEntry`:** Records the continuing session-wide suppression fact and its announcement instruction correlation ID, without private holder identity or localized announcement text.
14. **`VillagerRolePowerSuppressionAnnouncementAcknowledgedLogEntry`:** Records delivery acknowledgment for that same correlation ID, also without private holder data.

This list covers the distinct, loggable events derived from the rules. Each entry captures unique information critical for game logic, auditing, or moderator context.

# Victory Condition Checking:
 
`GameFlowManager` evaluates victory at the existing Dawn and Pre-Night windows from one resolved snapshot of living Faction Beneficiaries:
 
*   **Villager:** At least one living Villager Beneficiary exists and every living Beneficiary is Villager.
*   **Werewolf:** At least one living Werewolf Beneficiary exists and either no non-Werewolf Beneficiary remains, or every non-Werewolf Beneficiary is Villager and the Werewolves meet the existing control comparison. A living White Werewolf Beneficiary disables that shortcut.
*   **White Werewolf:** Exactly one Player remains living and that Player is a White Werewolf Beneficiary.
*   **Typed selection:** The shared predicate result set flows through `GameResultSelection`, producing a `SingleFactionGameResult`, order-independent `SharedVictoryGameResult`, `NoWinnerGameResult` when all Players are eliminated, or no terminal result.
*   **Durable terminal boundary:** `VictoryConditionMetLogEntry` records the typed result and `VictoryCheckWindow`; the matching `FinishedGameConfirmationInstruction` is the terminal Pending Instruction and rehydrates without predicate reevaluation.
*   **Future enhancements:** Later work may add Lovers, Angel, Piper, Prejudiced Manipulator, Event-specific, or other complex conditions through the same centralized predicate/selection vocabulary.

## Test Infrastructure

Game Session integration tests drive the full `GameService` → `GameFlowManager` → `GameSession` pipeline. Public setup-validation and value/model tests exercise their public Core boundaries directly when creating a Game Session would cross into unrelated runtime support. The test infrastructure provides fluent helpers to reduce boilerplate when constructing games and advancing through phases.

### `DiagnosticTestBase` (Base Class)

Abstract base class for test classes. Provides builder creation and automatic diagnostic dumps on failure.

*   `CreateBuilder()` (GameTestBuilder): Creates a `GameTestBuilder` with diagnostic output enabled.
*   `MarkTestCompleted()`: Call at the end of a passing test to suppress the diagnostic dump.
*   `Dispose()`: Automatically dumps the full state change log when a test fails (via xUnit's `IDisposable`).

### `GameTestBuilder` (Fluent Builder)

Fluent API for constructing game scenarios and advancing through phases.

*   **Game Setup:**
    *   `Create(output?)` (static): Factory method. Injects `DiagnosticStateObserver` when output is provided.
    *   `WithPlayers(int count)` / `WithPlayers(params string[] names)`: Adds players with auto-generated or specific names in Seating Order.
    *   `WithRoles(params MainRoleType[] roles)`: Sets the Roles for the game.
    *   `WithSimpleGame(playerCount, werewolfCount, includeSeer)`: Shorthand for a game with Werewolves, optional Seer, and Simple Villagers.
    *   `StartGame()` → `StartGameConfirmationInstruction`: Starts the game.
    *   `ConfirmGameStart()` → `ProcessResult`: Confirms start, transitions to Night.

*   **Game State:**
    *   `GetGameState()` (IGameSession?): Current game state.
    *   `GetCurrentInstruction()` (ModeratorInstruction?): Current pending instruction.
    *   `GameId` (Guid), `PlayerNames` (IReadOnlyList\<string\>), `Roles` (IReadOnlyList\<MainRoleType\>).
    *   `GameService`: Underlying service for advanced scenarios.
    *   `Process(ModeratorResponse)` → `ProcessResult`: Processes any Moderator Response directly.

*   **Night Phase Helpers:**
    *   `ConfirmNightStart()` → `ProcessResult`: Confirms the "village goes to sleep" instruction.
    *   `CompleteWerewolfNightAction(werewolfIds, victimId)` → `ProcessResult`: Full Werewolf sequence — identify → select victim → confirm sleep.
    *   `CompleteWerewolfNightActionSubsequentNight(victimId)` → `ProcessResult`: Night 2+ Werewolf sequence — wakeup → select victim → confirm sleep (no identification).
    *   `CompleteSeerNightAction(seerId, targetId)` → `ProcessResult`: Full Seer sequence — identify → select target → confirm sleep.
    *   `CompleteNightPhase(NightActionInputs)` → `ProcessResult`: Completes an entire Night Phase by iterating through Roles in hook dispatch order. Skips Roles with no input provided.
    *   `CompleteNightPhase(werewolfIds, victimId, seerId?, seerTargetId?)` → `ProcessResult`: Convenience overload with individual parameters.

*   **Dawn Phase Helpers:**
*   `CompleteDawnPhase(roleAssignments?)` → `ProcessResult`: Current helper that completes Dawn and historically defaults an omitted unknown Role to Simple Villager. That default is forbidden for PRD #93 evidence and is removed by #113; tests must supply physically observed mappings or seeded simulation truth through the production response contract.

*   **Day Phase Helpers:**
    *   `CompleteDayPhaseWithLynch(lynchTargetId, roleAssignments?)` → `ProcessResult`: Completes the Day Phase with a Vote resulting in Elimination — debate → vote → role assignment → transition to Night. Tests must supply a physically observed mapping when the target Role is unknown.
    *   `CompleteDayPhaseWithTie()` → `ProcessResult`: Completes the Day Phase with a tied Vote (no Elimination) — debate → vote → transition to Night.

*   **Extending for New Roles:** When implementing a new Role, add a `Complete[Role]NightAction` helper that handles the Role's full instruction/response cycle (identify → act → sleep). Then integrate it into `CompleteNightPhase` so it's called in the correct dispatch order.

**`NightActionInputs`:** Data class passed to `CompleteNightPhase` with optional fields for each Role's inputs (`WerewolfIds`, `WerewolfVictimId`, `SeerId`, `SeerTargetId`). Extend this class with new fields as Roles are added.

### `InstructionAssert` (Assertion Helpers)

Static helpers for asserting Moderator Instruction types in tests.

*   `ExpectType<TInstruction>(instruction, context?)` → `TInstruction`: Asserts the instruction is of the expected type and returns it cast. Throws with context message on mismatch.
*   `ExpectSuccessWithType<TInstruction>(result, context?)` → `TInstruction`: Asserts the `ProcessResult` is successful and the instruction is of the expected type.
*   `AssertType<TInstruction>(instruction, context?)`: Type assertion without returning the cast value.

### `ResponseFactory` (Player Lookup)

Static helpers for finding Players in test scenarios.

*   `GetPlayer(session, index)` / `GetPlayer(session, name)` → `IPlayer`: Lookup by index or name.
*   `GetPlayerByRole(session, role)` → `IPlayer?`: First Player with a specific Role.
*   `GetPlayersByRole(session, role)` → `IEnumerable<IPlayer>`: All Players with a specific Role.

### Diagnostic State Observation

For integration testing, an optional `IStateChangeObserver` can be injected into `GameSessionKernel` at construction time. This observer receives callbacks for all state mutations, enabling tests to capture a complete timeline of changes.

The observer is diagnostic only, not a second mutation or execution-read surface. Reducer-backed callbacks are emitted after their complete transition validates and live state changes. Main Phase notification occurs while applying the durable `PhaseTransitionLogEntry`; Sub-Phase, stage, and listener notifications follow their respective keyed owner requests; and Pending Instruction notification follows successful publication of the instruction and any advanced stable recovery boundary. Continuation restoration emits stage and listener callbacks only after both have been installed as one validated transition.

**Tracked State Changes:**
- Main phase transitions (`OnMainPhaseChanged`)
- Sub-phase transitions (`OnSubPhaseChanged`)
- Sub-phase stage changes (`OnSubPhaseStageChanged`)
- Listener and listener state changes (`OnListenerChanged`)
- Turn number increments (`OnTurnNumberChanged`)
- Pending instruction updates (`OnPendingInstructionChanged`)
- Game log entry applications (`OnLogEntryApplied`)

**Production Overhead:** Zero. The observer is null by default; null-conditional calls (`?.`) have negligible cost.

**`DiagnosticStateObserver`:**
*   `SetSession(session)`: Sets the session reference for resolving Player GUIDs to names in log output.
*   `Log` (IReadOnlyList\<string\>): Raw log entries.
*   `GetFormattedLog()` (string): Formatted table view of all state changes with type indicators.
*   `Clear()`: Clears log entries.

### Usage Example

```csharp
public class MyTests : DiagnosticTestBase
{
    public MyTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void Werewolf_attack_eliminates_victim()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var state = builder.GetGameState()!;
        var werewolf = ResponseFactory.GetPlayerByRole(state, MainRoleType.SimpleWerewolf)!;
        var seer = ResponseFactory.GetPlayerByRole(state, MainRoleType.Seer)!;
        var victim = ResponseFactory.GetPlayersByRole(state, MainRoleType.SimpleVillager).First();

        builder.CompleteNightPhase(
            werewolfIds: [werewolf.Id],
            victimId: victim.Id,
            seerId: seer.Id,
            seerTargetId: victim.Id);

        builder.CompleteDawnPhase();

        var updatedState = builder.GetGameState()!;
        Assert.Equal(PlayerHealth.Dead, updatedState.GetPlayerState(victim.Id).Health);

        MarkTestCompleted();
    }
}
```

# Session Persistence

*Status: **Implemented** — Stable recovery snapshot serialization via `System.Text.Json`.*

The Core persistence boundary is `IGameSession.Serialize()` plus `GameService.RehydrateSession(string)`. Serialization returns the latest validated stable recovery snapshot retained by `GameSessionKernel`. It does not serialize the live in-memory execution tail, and Rehydration does not replay the event log.

## Design

*   **Serialization:** `GameSession.Serialize()` delegates to `GameSessionKernel.Serialize()`, which returns the latest installed stable boundary snapshot.
*   **Boundary advancement:** After routing, victory override handling, and next-instruction settlement are complete, `GameFlowManager.HandleInput(...)` submits one keyed `ExecutionCommit`. When that commit advances recovery, the Kernel creates and validates the candidate snapshot before publishing the Pending Instruction or replacing the previous boundary. Accepted observations, committed one-use resources, settled elimination batches, completed elimination reactions, and fully drained elimination cascades are additional semantic boundaries.
*   **Rehydration:** `GameService.RehydrateSession(string serializedSession)` restores the stable snapshot into a new active session and returns the session's GUID. When a governed semantic boundary requires a live continuation, `GameFlowManager` authenticates the durable instruction/cursor and uses its keyed restoration request against an explicit `ExecutionView`; this reconstruction does not make the live listener state durable.

**Committed response checkpoints:** the landed Thief flow creates one narrow mid-Night stable checkpoint atomically with a successful `Offer1`, `Offer2`, or `Decline` response. That checkpoint contains the committed outcome, resulting card zones, current Role and fresh power state, and the pending public sleep instruction before Core returns success. Devoted Servant applies the same semantic-boundary pattern after the accepted public self-reveal and again after the accepted private swap record. Neither flow serializes arbitrary listener progress.

**Elder suppression recovery:** the suppression commitment and its pending public `ConfirmationInstruction` form a stable boundary. Rehydration derives active suppression from the committed log fact and reconstructs the `OnVoteConcluded` listener continuation by matching `ModeratorInstructionSemantic.AnnounceVillagerRolePowerSuppression` and the correlation ID. After confirmation, the acknowledgment fact prevents the announcement from being repeated before vote-outcome navigation continues. The durable facts contain neither the private Elder holder nor localized prose.

## Durable Payload

`GameSessionDto` is the durable recovery payload:

*   `Id`, `SeatingOrder`, `RolesInPlay`: Session identity and setup.
*   `RoleLockIn`, `PhysicalCharacterCards`: Versioned physical inventory plus current card zones and ownership.
*   `Players`: Current derived player cache, including current Role, Moderator-known Role, publicly revealed Role, Status Effects, health, voting facts, and known-or-unknown Faction facts. These facts rehydrate independently without reconstructing unknown values.
*   `TurnNumber`: Derived turn cursor as of the stable boundary. It is durable to avoid double-incrementing after Day-to-Night recovery.
*   `GameHistoryLog`: Event source entries as of the same stable boundary as the derived caches.
*   `PendingInstruction`, `PendingInstructionSemantic`: The exact committed boundary instruction and its machine-stable semantic that the moderator must consume next after Rehydration. This is stable boundary state, not arbitrary listener progress.
*   `IsStableRecoveryBoundary`: Marks payloads that follow the ADR-0002 stable boundary contract.
*   `AcceptedObservationRecoveryCursor` or `DomainRecoveryCursor`: An optional, versioned semantic recovery identity correlated with the exact committed next instruction. A boundary carries at most one cursor family.
*   `PhaseStateCache`: Serialized for DTO compatibility, but stable Rehydration restores only `CurrentPhase`, `SubPhase`, and `CompletedSubPhaseStages`. `ActiveSubPhaseStage`, `CurrentListenerId`, `CurrentListenerType`, and `CurrentListenerState` are ignored because they are transient execution state.

For non-stable payloads, Rehydration restores only the current Main Phase and discards all Sub-Phase, stage, listener, and listener-state details. For stable payloads, it restores the minimal Main Phase/Sub-Phase cursor and completed stages needed to consume the committed boundary `PendingInstruction`. The private phase-cache value performs this DTO conversion inside the Kernel; it is not an external read or mutation seam.

An interrupted elimination cascade is reconstructed from its scoped semantic facts. A batch-resolution fact prevents a settled zero-elimination interception from being reevaluated, completed reaction facts re-admit their recorded child eliminations without executing the reaction again, and a cascade-completed fact prevents a finished vote scope from being reused. Pre-reveal completion facts are replayed in registration order after Moderator input so every admission from that boundary reconstructs the same concurrent next wave. Reconstruction occurs only when each frame reaches the queue head, so nested descendants cannot overtake sibling waves admitted by an earlier frame. The live wave queue, reaction instances, and listener state are never serialized.

## Implementation Details

*   **Polymorphic Converters:** Custom `JsonConverter` implementations handle polymorphic types:
    *   `GameLogEntryConverter`: Serializes/deserializes all `GameLogEntryBase` derived types using a `$type` discriminator.
    *   `ModeratorInstructionConverter`: Serializes/deserializes all `ModeratorInstruction` derived types using a `$type` discriminator.
*   **DTOs:** Internal DTO classes (`GameSessionDto`, `PlayerDto`, `GamePhaseStateCacheDto`) provide a clean serialization boundary.
*   **Deserialization Key:** A private `DeserializationKey` class implements `IStateMutatorKey` to allow direct state restoration during deserialization without going through log entry application.

# Sound Effects

*Status: **Placeholder** - enum and property exist but no sound playback is implemented.*

The architecture supports associating sound effects with moderator instructions for immersive gameplay.

## Design

*   **`SoundEffectsEnum`:** Enum defining available sound effects. Currently contains only `None = 0` as a placeholder.
*   **`ModeratorInstruction.SoundEffects`:** A `List<SoundEffectsEnum>` property on the abstract base class.
    *   Multiple sound effects can be specified per instruction.
    *   The UI should play only the listed effects and stop any others currently playing.

## Future Implementation

When implemented, sound effects may include:
*   Atmospheric night sounds during the Night phase
*   Wolf howls during werewolf actions
*   Alert sounds for eliminations or victory conditions
*   Phase transition sounds 
