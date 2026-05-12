Of course. Here is the updated architectural proposal, revised to incorporate the clarifications and decisions from our discussion. The original structure and formal tone have been preserved, with changes made only where necessary to reflect the refined design.

***

### **Architectural Proposal: Declarative Sub-Phase State Machine for GameFlowManager**

**Document Version:** 1.1
**Date:** November 5, 2025

### 1. Abstract

This proposal outlines a refactoring of the `GameFlowManager` to replace its current imperative, switch-based sub-phase management with a declarative, validated state machine. The new architecture will be strongly typed and modeled after the successful `RoleHookListener<TRoleStateEnum>` pattern. This change will introduce a generic `PhaseDefinition<TSubPhaseEnum>` class containing a map of `SubPhaseStage` records. Each stage will declaratively define its handler and its valid next transitions, providing robust runtime validation of the game's core flow. This will significantly improve the clarity, maintainability, and reliability of the game loop logic without altering the high-level `GameFlowManager.HandleInput` orchestrator.

### 2. Motivation

The current architecture manages game flow at two levels of granularity. At the main-phase level (`Setup`, `Night`, `Day_Dawn`, etc.), the `GameFlowManager.PhaseDefinitions` dictionary and the `HandleInput` method provide robust validation for transitions between these major phases.

However, within complex main phases, the logic is less structured. Methods like `HandleNightPhase` and `HandleDayDawnPhase` rely on internal, imperative `switch` statements to route logic based on a sub-phase enum retrieved from the `GamePhaseStateCache`. This approach has several drawbacks:

*   **Lack of Clarity:** The flow of sub-phases is obscured within the procedural code of a large handler method. It is difficult to see, at a glance, how sub-phases are meant to connect to one another.
*   **Brittleness:** There is no validation preventing an illegal transition between sub-phases. A coding error could cause the state to jump from a `Start` sub-phase to a `Finalize` sub-phase, bypassing critical intermediate steps without any runtime error indicating the state machine violation.
*   **Inconsistency:** The robust, declarative, and self-documenting state machine pattern successfully implemented in `RoleHookListener<TRoleStateEnum>` for role logic is not applied to the core game loop, creating an architectural inconsistency.
*   **High Cognitive Load:** Modifying the flow of a phase requires careful reading and understanding of the entire `Handle...Phase` method, increasing the risk of introducing bugs.

The primary motivation for this change is to apply the same principles of declarative state management and runtime validation to the sub-phase level, making the core game flow as transparent and robust as the role-level state machines.

### 3. Proposed Architectural and Documentation Changes

This section details the specific implementation changes required to achieve the goal. The core principle is to replace the coarse `PhaseDefinition` with a more intelligent, generic dispatcher that manages a declarative map of its internal sub-phase stages.

#### 3.1. New and Modified Components

##### 3.1.1. `PhaseHandlerResult` Hierarchy
To make the intent of a handler's outcome more explicit, the existing `PhaseHandlerResult` will be converted into an abstract base class with distinct concrete implementations for each type of outcome: transitioning to a new main phase, transitioning to a new sub-phase, or staying in the current sub-phase (e.g., while awaiting hook listener input).

```csharp
// Abstract Base Record
internal abstract record PhaseHandlerResult(ModeratorInstruction? ModeratorInstruction);

// For transitioning between main phases (e.g., Night -> Day_Dawn)
internal sealed record MainPhaseHandlerResult(
    ModeratorInstruction? ModeratorInstruction, 
    GamePhase MainPhase, 
    PhaseTransitionReason TransitionReason) : PhaseHandlerResult(ModeratorInstruction);

// For transitioning between sub-phases (e.g., Night.Start -> Night.ActionLoop)
internal sealed record SubPhaseHandlerResult(
    ModeratorInstruction? ModeratorInstruction, 
    object SubGamePhase) : PhaseHandlerResult(ModeratorInstruction);

// For remaining in the current sub-phase (e.g., awaiting moderator input for a hook)
internal sealed record StayInSubPhaseHandlerResult(
    ModeratorInstruction? ModeratorInstruction) : PhaseHandlerResult(ModeratorInstruction);
```

##### 3.1.2. `IPhaseDefinition` Interface
To allow the main `PhaseDefinitions` dictionary to hold phase handlers for different, unrelated sub-phase enums, a non-generic interface is required. The `GameService` parameter is removed from the signature as it was determined to be an unnecessary dependency.

```csharp
// Location: Werewolves.GameLogic/Models/StateMachine/IPhaseDefinition.cs

internal interface IPhaseDefinition
{
    PhaseHandlerResult ProcessInputAndUpdatePhase(GameSession session, ModeratorResponse input);
}
```

##### 3.1.3. `SubPhaseStage<TSubPhaseEnum>` Record
This new generic record is the fundamental building block of the declarative state machine. It defines a single, atomic step within a main phase's lifecycle.

```csharp
// Location: Werewolves.GameLogic/Models/StateMachine/SubPhaseStage.cs

/// <summary>
/// Defines a single, validated stage within a main game phase's state machine.
/// </summary>
/// <typeparam name="TSubPhaseEnum">The enum type defining the sub-phases for the parent phase.</typeparam>
internal record SubPhaseStage<TSubPhaseEnum> where TSubPhaseEnum : struct, Enum
{
    /// <summary>
    /// The specific sub-phase that triggers this stage.
    /// </summary>
    public TSubPhaseEnum StartSubPhase { get; init; }
    
    /// <summary>
    /// The handler function that executes the logic for this stage.
    /// </summary>
    public Func<GameSession, ModeratorResponse, PhaseHandlerResult> Handler { get; init; }

    /// <summary>
    /// A declarative set of all valid sub-phases that this stage is allowed to transition to.
    /// If null, any sub-phase transition is considered an error.
    /// </summary>
    public HashSet<TSubPhaseEnum>? PossibleNextSubPhases { get; init; }

    /// <summary>
    /// A declarative set of all valid main phase transitions that this stage is allowed to initiate.
    /// If null, any main phase transition is considered an error.
    /// </summary>
    public HashSet<PhaseTransitionInfo>? PossibleNextMainPhaseTransitions { get; init; }
}
```

##### 3.1.4. `PhaseDefinition<TSubPhaseEnum>` Class
This new generic class will replace the old `PhaseDefinition`. It will implement `IPhaseDefinition` and act as the dispatcher and validator for its defined sub-phase stages.

```csharp
// Location: Werewolves.GameLogic/Models/StateMachine/PhaseDefinition.cs

internal class PhaseDefinition<TSubPhaseEnum> : IPhaseDefinition where TSubPhaseEnum : struct, Enum
{
    private readonly Dictionary<TSubPhaseEnum, SubPhaseStage<TSubPhaseEnum>> _subPhaseStages;
    private readonly TSubPhaseEnum _entrySubPhase;

    public PhaseDefinition(List<SubPhaseStage<TSubPhaseEnum>> stages, TSubPhaseEnum entrySubPhase)
    {
        _entrySubPhase = entrySubPhase;
        // Validate that all stages are unique and build the lookup dictionary.
        _subPhaseStages = stages.ToDictionary(s => s.StartSubPhase);
    }

    public PhaseHandlerResult ProcessInputAndUpdatePhase(GameSession session, ModeratorResponse input)
    {
        // 1. Determine the current sub-phase state, defaulting to the defined entry point.
        var subPhaseState = session.GetSubPhase<TSubPhaseEnum>() ?? _entrySubPhase;

        // 2. Find the corresponding stage definition.
        if (!_subPhaseStages.TryGetValue(subPhaseState, out var stageToExecute))
        {
            throw new InvalidOperationException($"Internal State Machine Error: No sub-phase stage definition found for phase '{session.GetCurrentPhase()}' and sub-phase '{subPhaseState}'.");
        }

        // 3. Execute the specific handler for this stage.
        var result = stageToExecute.Handler(session, input);

        // 4. Validate the resulting transition against the declarative rules of the stage.
        ValidateTransition(subPhaseState, result, stageToExecute);

        return result;
    }
    
    private void ValidateTransition(
        TSubPhaseEnum currentSubPhase, 
        PhaseHandlerResult result, 
        SubPhaseStage<TSubPhaseEnum> executedStage)
    {
        switch (result)
        {
            case SubPhaseHandlerResult subPhaseResult:
            {
                var nextSubPhase = (TSubPhaseEnum)subPhaseResult.SubGamePhase;
                var allowed = executedStage.PossibleNextSubPhases;
                if (allowed == null || !allowed.Contains(nextSubPhase))
                {
                    throw new InvalidOperationException(
                        $"Internal State Machine Error: Illegal sub-phase transition from '{currentSubPhase}' to '{nextSubPhase}'. " +
                        $"Valid next sub-phases are: {(allowed == null ? "None" : string.Join(", ", allowed))}.");
                }
                break;
            }
            case MainPhaseHandlerResult mainPhaseResult:
            {
                var allowed = executedStage.PossibleNextMainPhaseTransitions;
                var requested = new PhaseTransitionInfo(mainPhaseResult.MainPhase, mainPhaseResult.TransitionReason);
                if (allowed == null || !allowed.Contains(requested))
                {
                     throw new InvalidOperationException(
                        $"Internal State Machine Error: Illegal main-phase transition from '{currentSubPhase}' to '{requested.TargetPhase}' with reason '{requested.ConditionOrReason}'. " +
                        $"Valid main phase transitions are: {(allowed == null ? "None" : string.Join(", ", allowed))}.");
                }
                break;
            }
            case StayInSubPhaseHandlerResult:
                // This result type explicitly signals the intent to not transition, so no validation is needed.
                break;
        }
    }
}
```

##### 3.1.5. Refactoring `GameFlowManager.cs`
The `PhaseDefinitions` dictionary will be updated to use the new components, and the large `Handle...Phase` methods will be removed and broken down into smaller, focused `private static` methods. This design enforces handler statelessness; any complex, transient intra-phase state would need to be managed via a dedicated cache if the need arises.

**New `GameFlowManager` Structure:**
```csharp
// In GameFlowManager.cs

private static readonly Dictionary<GamePhase, IPhaseDefinition> PhaseDefinitions = new()
{
    // Example for a simple phase like Setup
    [GamePhase.Setup] = new PhaseDefinition<SetupSubPhases>(
    [
        new() {
            StartSubPhase = SetupSubPhases.Confirm,
            Handler = HandleSetupConfirmation,
            PossibleNextMainPhaseTransitions = [ new(GamePhase.Night, PhaseTransitionReason.SetupConfirmed) ]
        }
    ], entrySubPhase: SetupSubPhases.Confirm),

    // Example for a complex phase like Night
    [GamePhase.Night] = new PhaseDefinition<NightSubPhases>(
    [
        new() {
            StartSubPhase = NightSubPhases.Start,
            Handler = HandleNightStart,
            PossibleNextSubPhases = [ NightSubPhases.ActionLoop ]
        },
        new() {
            StartSubPhase = NightSubPhases.ActionLoop,
            Handler = HandleNightActionLoop,
            // The handler can either stay in ActionLoop (via StayInSubPhaseResult) 
            // or transition to Day_Dawn when the hook completes.
            PossibleNextMainPhaseTransitions = [ new(GamePhase.Day_Dawn, PhaseTransitionReason.NightActionLoopComplete) ]
        }
    ], entrySubPhase: NightSubPhases.Start),

    // Other phases (Day_Dawn, Day_Vote, etc.) will be refactored similarly.
};

// The large "HandleNightPhase" method is DELETED.
// It is replaced by these small, focused, private static methods:

private static PhaseHandlerResult HandleSetupConfirmation(GameSession session, ModeratorResponse input)
{
    var nightStartInstruction = new ConfirmationInstruction(/* ... */);
    return new MainPhaseHandlerResult(nightStartInstruction, GamePhase.Night, PhaseTransitionReason.SetupConfirmed);
}

private static PhaseHandlerResult HandleNightStart(GameSession session, ModeratorResponse input)
{
    var instruction = new ConfirmationInstruction(/* ... */);
    // This transition will now be validated by PhaseDefinition<NightSubPhases>
    return new SubPhaseHandlerResult(instruction, NightSubPhases.ActionLoop);
}

private static PhaseHandlerResult HandleNightActionLoop(GameSession session, ModeratorResponse input)
{
    var hookResult = FireHook(GameHook.NightActionLoop, session, input);

    if (hookResult.Outcome == HookHandlerOutcome.NeedInput)
    {
        // The new explicit result type for staying in the current state.
        return new StayInSubPhaseHandlerResult(hookResult.Instruction);
    }
    
    var instruction = new ConfirmationInstruction(/* ... */);
    // This main phase transition will now be validated
    return new MainPhaseHandlerResult(instruction, GamePhase.Day_Dawn, PhaseTransitionReason.NightActionLoopComplete);
}
```

#### 3.2. Documentation Changes (`architecture.md`)

The `architecture.md` file must be updated to reflect this superior architecture.

*   **`GameFlowManager` Section:** The description of this class will be rewritten. It will no longer mention large `Handle...Phase` methods with `switch` statements. Instead, it will describe the new declarative state machine. It will explain that `PhaseDefinitions` is a dictionary mapping a `GamePhase` to an `IPhaseDefinition`, and that the `PhaseDefinition<TSubPhaseEnum>` implementation contains a declarative list of `SubPhaseStage` records that define the graph of possible state transitions for that phase, providing strong runtime validation.
*   **`Game Loop Outline` Section:** The description of how the game flows from one phase to another will be updated. It will emphasize that the `ProcessInputAndUpdatePhase` method on the active `IPhaseDefinition` is called, which in turn dispatches to the correct sub-phase handler based on the declarative map, ensuring all transitions are valid.

### 4. Impact Analysis

#### 4.1. Benefits

*   **Clarity and Readability:** The flow of sub-phases within a main phase becomes explicit and self-documenting. A developer can understand the entire state machine for a phase by simply reading the `PhaseDefinition` instantiation.
*   **Robustness and Safety:** Illegal state transitions between sub-phases (or from a sub-phase to a main phase) will be caught immediately at runtime with a detailed `InvalidOperationException`. This eliminates a significant class of potential bugs.
*   **Maintainability:** Adding, removing, or re-ordering sub-phases becomes a matter of updating the declarative list of stages. The logic is isolated to small handlers, reducing the risk of unintended side effects and lowering the cognitive load required to make changes.
*   **Architectural Consistency:** This change aligns the core game flow management with the robust and successful state machine pattern already used for role listeners, making the entire codebase more cohesive.

#### 4.2. Considerations & Mitigations

*   **Consideration: Initial Refactoring Effort**
    *   The change is non-trivial and will touch the core logic of every game phase.
    *   **Mitigation:** The refactoring can be performed incrementally, one `GamePhase` at a time. The `IPhaseDefinition` interface allows the old `PhaseDefinition` and the new `PhaseDefinition<T>` to coexist during the transition period. We can start with the most complex phases (`Night`, `Day_Dawn`) to gain the most benefit early on.

*   **Consideration: Increased Verbosity**
    *   Defining the state machine declaratively is inherently more verbose than a compact `switch` statement.
    *   **Mitigation:** This is a deliberate and accepted trade-off. The increased code size is justified by the immense gains in clarity, safety, and maintainability. The "verbosity" is, in fact, the self-documentation that we are seeking. The use of a `PhaseHandlerResult` hierarchy also adds to this, but is justified by making the intent of each handler's outcome explicit and unambiguous.

*   **Consideration: Phases without Sub-Phases**
    *   Some phases, like `Setup` or `GameOver`, are simple and may not have a complex sub-phase structure.
    *   **Mitigation:** These phases can be modeled with a simple, single-state enum (e.g., `enum SetupSubPhases { Confirm }`). This maintains consistency in the pattern. The overhead is minimal and worth the uniformity of the approach across all game phases.