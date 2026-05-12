## **Architectural Proposal: Sub-Phase Stage Atomicity and Idempotency**
### 1. Abstract

This proposal details a critical refinement to the sub-phase state machine architecture, focusing on the atomicity and idempotency of `SubPhaseStage` execution. The current model, while declarative, allows a single stage to perform complex, multi-step operations, including pausing for moderator input. This creates ambiguity and increases the risk of state corruption if a stage is re-entered. The proposed change refactors `SubPhaseStage` to enforce that each stage is an atomic, non-interruptible unit of work. Stages that require moderator input will now be split into two distinct stages: one to request input and a subsequent one to process it. This ensures that each stage is executed exactly once per sub-phase entry, eliminating state management complexities and making the game flow more robust and predictable.

### 2. Motivation

The previous revision introduced a declarative state machine for sub-phases, a significant improvement over the imperative `switch` statements. However, a key architectural flaw remains: a single `SubPhaseStage` can be re-entered multiple times. This occurs when a stage's handler fires a hook that requires moderator input (e.g., `HookSubPhaseStage`). The game pauses, and upon receiving the response, the *same* stage is executed again.

This design has several critical drawbacks:

*   **Idempotency Burden:** Every stage handler must be written to be perfectly idempotent. It must correctly handle being called once to initiate an action and a second time to process the result, without accidentally re-triggering the initial action. This is complex and error-prone.
*   **State Management Complexity:** The `GamePhaseStateCache` must track not only the current sub-phase but also which `GameHook` is active and which `RoleHookListener` is paused. This logic is fragile and leaks concerns between the phase manager and the listener implementation.
*   **Lack of Atomicity:** A stage does not represent a single, atomic operation. It represents a potentially long-running, interruptible workflow, which violates the principle of a clean state machine transition.
*   **Obscured Flow:** The linear sequence of operations is hidden within a single stage's implementation. It is not clear from the declarative definition that a stage might pause and resume.

The motivation for this revision is to enforce true atomicity at the stage level. Each `SubPhaseStage` should represent a single, uninterruptible step in the game's logic. This simplifies state management, eliminates the need for complex idempotency logic, and makes the game flow fully transparent and auditable from the declarative `PhaseManager` definition.

### 3. Proposed Architectural Changes

The core of this proposal is to redefine the responsibilities of the components managing sub-phase execution. `PhaseManager` will still orchestrate sub-phases, but `SubPhaseManager` will now manage a linear sequence of atomic stages, and the `GamePhaseStateCache` will be enhanced to prevent stage re-entry.

#### 3.1. New and Modified Components

##### 3.1.1. `GamePhaseStateCache` Enhancements
The cache will be updated to track stage execution with greater precision, acting as a "mutex" to ensure atomicity.

*   A new `_currentSubPhaseStage` field will track the specific stage currently executing.
*   A new `_previousSubPhaseStages` list will record all stages that have already completed within the current sub-phase.
*   On each sub-phase transition (i.e., when `TransitionSubPhase` is called), both `_currentSubPhaseStage` and `_previousSubPhaseStages` are cleared.
*   New methods `TryEnterSubPhaseStage()`, `StartSubPhaseStage()`, and `CompleteSubPhaseStage()` will manage this lifecycle, ensuring a stage runs only once.

```csharp
// In Werewolves.StateModels/Models/GamePhaseStateCache.cs

// ... private fields ...
private string? _currentSubPhaseStage;
private List<string> _previousSubPhaseStages = new();

// ... methods ...
internal bool TryEnterSubPhaseStage(string subPhaseStageId)
{
    // Logic to check if we can enter:
    // 1. Is another stage already active?
    // 2. Has this stage already been completed in this sub-phase?
    // If checks pass, call StartSubPhaseStage(subPhaseStageId).
    // ...
}

internal void CompleteSubPhaseStage()
{
    _previousSubPhaseStages.Add(_currentSubPhaseStage!);
    _currentSubPhaseStage = null;
    // ...
}

private void ClearCurrentSubPhase()
{
    _currentSubPhase = null;
    _previousSubPhaseStages = [];
    ClearSubPhaseStage();
}
```

##### 3.1.2. `SubPhaseStage` Hierarchy
The monolithic `SubPhaseStage` will be replaced by a more specialized, abstract class hierarchy. This clearly separates stages based on their purpose (logic, navigation, or hook execution) and enforces the new atomic execution model.

```csharp
// In Werewolves.GameLogic/Models/StateMachine/SubPhaseStage.cs

// Abstract base class
internal abstract class SubPhaseStage
{
    public string Id { get; }
    // ... constructor and common TryExecute logic ...
    protected abstract PhaseHandlerResult InnerExecute(GameSession session, ModeratorResponse input);
}

// For stages that execute custom logic without navigating
internal sealed class LogicSubPhaseStage : SubPhaseStage { /* ... */ }

// For stages that fire a game hook
internal sealed class HookSubPhaseStage : SubPhaseStage { /* ... */ }

// Base for navigation stages
internal abstract class NavigationSubPhaseStage : SubPhaseStage { /* ... */ }

// For navigation points within a sub-phase sequence
internal sealed class MidNavigationSubPhaseStage : NavigationSubPhaseStage { /* ... */ }

// MUST be the last stage in a sequence to transition out of the sub-phase
internal sealed class EndNavigationSubPhaseStage : NavigationSubPhaseStage { /* ... */ }
```
The `TryExecute` method on the base `SubPhaseStage` will now use the `GamePhaseStateCache` to ensure it runs only once.

##### 3.1.3. `SubPhaseManager<TSubPhase>`
This new record replaces the `SubPhaseStage` from the previous design. It now encapsulates the logic for a *single sub-phase*, which is composed of a *linear sequence of atomic stages*.

```csharp
// In Werewolves.GameLogic/Models/StateMachine/SubPhaseManager.cs

internal record SubPhaseManager<TSubPhase> where TSubPhase : struct, Enum
{
    public TSubPhase StartSubPhase { get; init; }
    private List<SubPhaseStage> SubPhaseStages { get; }
    public HashSet<TSubPhase>? PossibleNextSubPhases { get; init; }
    public HashSet<PhaseTransitionInfo>? PossibleNextMainPhaseTransitions { get; init; }

    public PhaseHandlerResult Execute(GameSession session, ModeratorResponse input)
    {
        foreach (var stage in SubPhaseStages)
        {
            // TryExecute now uses the cache to run only once.
            // If it produces a result (i.e., an instruction for the moderator), we stop.
            if (stage.TryExecute(session, input, out var result) && result != null)
            {
                return result;
            }
        }
        // This should be unreachable if the last stage is an EndNavigationSubPhaseStage
        throw new InvalidOperationException("Sub-phase completed without a navigation result.");
    }
}
```

##### 3.1.4. `PhaseManager<TSubPhaseEnum>` (Previously `PhaseDefinition<T>`)
This class is renamed for clarity and now manages a dictionary of `SubPhaseManager` instances. Its core responsibility remains the same: dispatching to the correct sub-phase and validating the final transition.

```csharp
// In Werewolves.GameLogic/Models/StateMachine/PhaseManager.cs

internal class PhaseManager<TSubPhaseEnum> : IPhaseDefinition where TSubPhaseEnum : struct, Enum
{
    private readonly Dictionary<TSubPhaseEnum, SubPhaseManager<TSubPhaseEnum>> _subPhaseDictionary;
    // ...

    public PhaseHandlerResult ProcessInputAndUpdatePhase(GameSession session, ModeratorResponse input)
    {
        PhaseHandlerResult result;
        do
        {
            // ... find the correct SubPhaseManager ...
            var subPhase = _subPhaseDictionary[subPhaseState];

            // Execute the sub-phase's sequence of stages
            result = subPhase.Execute(session, input);

            // Validate the final transition
            AttemptTransition(session, subPhaseState, result, subPhase);

        } while (result.ModeratorInstruction == null); // Loop if a stage completes silently

        return result;
    }
    // ...
}
```

#### 3.2. Refactoring `GameFlowManager.cs`
The instantiation of the phase definitions will be updated to reflect the new, more granular structure. Workflows that were previously a single, re-entrant stage will now be explicitly defined as a sequence of atomic stages.

**Example: Refactoring the Day Vote**

**Old (Conceptual):**
```csharp
// A single stage that is re-entered
new() {
    StartSubPhase = DaySubPhases.NormalVoting,
    Handler = HandleDayNormalVote, // This handler did everything
    // ...
}
```

**New:**
```csharp
// In GameFlowManager.cs
new SubPhaseManager<DaySubPhases>(
    subPhase: DaySubPhases.NormalVoting,
    subPhaseStages: [
        // Stage 1: Ask the moderator for the vote outcome.
        LogicStage(DaySubPhaseStage.StartNormalVote, HandleDayNormalVoteOutcomeRequest),
        
        // Stage 2: Process the moderator's response. This is a navigation stage.
        NavigationEndStage(DaySubPhaseStage.ProcessVote, HandleDayNormalVoteOutcomeResponse)
    ],
    possibleNextSubPhases: [ DaySubPhases.ProcessVoteRoleReveal, DaySubPhases.Finalize ]
),
// ... subsequent sub-phase managers for role reveal, etc.
```
This new structure makes the two-step "request/response" flow explicit in the state machine's definition.

### 4. Impact Analysis

#### 4.1. Benefits

*   **Atomicity and Simplicity:** Each `SubPhaseStage` is now a simple, atomic operation that executes exactly once. This dramatically reduces the cognitive load required to understand and write stage logic.
*   **Elimination of Idempotency Requirement:** Handlers no longer need to be idempotent, as the framework guarantees they will not be re-entered.
*   **Improved Clarity and Transparency:** The entire flow of a sub-phase, including request/response cycles, is now explicitly visible in the declarative `SubPhaseManager` definition. There is no hidden complexity.
*   **Enhanced Robustness:** The `GamePhaseStateCache` provides a strong guarantee against accidental state corruption from stage re-entry. The state machine becomes more predictable and easier to debug.
*   **Clean Separation of Concerns:** The `GamePhaseStateCache` manages *what* stage is running, while the `SubPhaseStage` itself only contains the logic for *how* it runs.

#### 4.2. Considerations & Mitigations

*   **Consideration: Increased Granularity**
    *   The number of defined stages will increase, as workflows are broken into smaller, atomic parts.
    *   **Mitigation:** This is the intended outcome. This granularity is what provides the clarity and safety that the previous model lacked. The use of static helper methods (`LogicStage`, `HookStage`, `NavigationEndStage`) on `SubPhaseStage` will keep the declarative definitions concise and readable.

*   **Consideration: Refactoring Effort**
    *   All existing phase definitions must be refactored to use the new `PhaseManager`/`SubPhaseManager`/`SubPhaseStage` structure.
    *   **Mitigation:** The change is systematic. Each existing handler method can be analyzed and split into its constituent atomic parts. The compiler will guide much of the refactoring process. The `GameFlowManager` provides a centralized location to perform these updates, and the work can be done phase-by-phase.
