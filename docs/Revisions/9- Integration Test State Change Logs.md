# Architectural Proposal: Integration Test State Change Diagnostics

## 1. Abstract

This proposal introduces a diagnostic logging mechanism for integration tests that captures all state changes within the `GameSessionKernel` as they occur. When a test fails, the accumulated diagnostic log is automatically surfaced in the test failure output, providing a complete timeline of state mutations. The solution uses an internal, optional `IStateChangeObserver` interface injected into the Kernel at construction time. This preserves the encapsulation of the production code while giving tests full visibility into intermediate state changes—including phase transitions, listener state changes, turn number increments, and game log entries.

## 2. Motivation

### 2.1 The Problem

Debugging failed integration tests in the Werewolves game engine is time-consuming. When a test fails, developers must manually trace through the game flow to understand:

- Which phases and sub-phases the game transitioned through
- What listener states were active at each point
- Which game log entries were applied
- When the turn number incremented
- What moderator instructions were pending

Currently, this requires setting breakpoints, stepping through code, or adding temporary logging statements. The game's event-sourced architecture and multi-step hook-based flow make it particularly difficult to reconstruct the sequence of state changes that led to a failure.

### 2.2 The Goal

To provide a zero-friction diagnostic mechanism that:

1. **Captures Intermediate Changes**: Records all state mutations as they happen, not just the final state after each `ProcessInstruction` call.
2. **Automatic Output on Failure**: Dumps the complete state change timeline to xUnit's test output when any assertion fails.
3. **Zero Production Overhead**: The observer mechanism is optional and internal; production code paths remain unchanged when no observer is attached.
4. **Minimal Production Code Impact**: Uses an `internal` interface and null-check pattern, keeping the diagnostic surface invisible to external consumers.

### 2.3 Rejected Alternatives

#### 2.3.1 State Snapshot Approach (Rejected)

Capturing complete state snapshots after each `ProcessInstruction` call was considered. While this requires no production code changes, it fails to capture *intermediate* state changes within a single instruction processing cycle. For example, a werewolf attack that triggers Elder's extra life, then a protection save, involves multiple phase cache and listener state transitions—all invisible to snapshot-based logging.

#### 2.3.2 Test-Only Subclass of `GameSessionKernel` (Rejected)

Creating a `TestableGameSessionKernel` subclass was considered. However, `GameSessionKernel` is `internal` with `private` fields and non-virtual property setters. This approach would require significant production code changes (making members `protected virtual`) that violate the encapsulation goals.

#### 2.3.3 Reflection-Based Spy (Rejected)

Using reflection to poll state or subscribe to internal events was considered. This is brittle (breaks on refactoring) and cannot capture intermediate changes within a single instruction cycle.

#### 2.3.4 Conditional Compilation (`#if DEBUG`) (Rejected)

Adding `#if DEBUG` or `#if TEST` blocks was considered. While this eliminates release overhead, it pollutes production code with test-specific logic and makes the diagnostic mechanism harder to maintain and reason about.

## 3. Proposed Architectural & Documentation Changes

### 3.1 New Interface: `IStateChangeObserver`

Introduce an internal, optional observer interface in `Werewolves.StateModels`:

```csharp
// File: Werewolves.StateModels/Core/IStateChangeObserver.cs
namespace Werewolves.StateModels.Core;

/// <summary>
/// Optional observer for state change diagnostics.
/// Used by tests to capture intermediate state mutations.
/// </summary>
internal interface IStateChangeObserver
{
    void OnMainPhaseChanged(GamePhase newPhase);
    void OnSubPhaseChanged(string? newSubPhase);
    void OnSubPhaseStageChanged(string? newSubPhaseStage);
    void OnListenerChanged(ListenerIdentifier? listener, string? listenerState);
    void OnTurnNumberChanged(int newTurnNumber);
    void OnPendingInstructionChanged(ModeratorInstruction? instruction);
    void OnLogEntryApplied(GameLogEntryBase entry);
}
```

**Design Decisions:**

- **`internal` visibility**: The interface is invisible to external consumers (UI layer).
- **Nullable parameters**: Reflects that sub-phases, stages, and listeners can be cleared (set to null).
- **Separate methods per field**: Enables fine-grained logging with clear semantics.
- **`OnLogEntryApplied`**: Called after the log entry's `InnerApply` completes, providing access to the entry's `ToString()` for summarization.

### 3.2 `GameSessionKernel` Modifications

#### 3.2.1 Observer Storage

Add an optional observer field to `GameSessionKernel`:

```csharp
// In GameSessionKernel.cs
internal sealed partial class GameSessionKernel
{
    private readonly IStateChangeObserver? _stateChangeObserver;
    
    // Existing fields...
}
```

#### 3.2.2 Constructor Extension

Extend the constructor to accept an optional observer:

```csharp
internal GameSessionKernel(
    Guid id, 
    ModeratorInstruction initialInstruction, 
    List<string> playerNamesInOrder, 
    List<MainRoleType> rolesInPlay,
    List<string>? eventCardIdsInDeck = null,
    IStateChangeObserver? stateChangeObserver = null)
{
    // Existing initialization...
    _stateChangeObserver = stateChangeObserver;
    
    // Notify observer of initial state
    _stateChangeObserver?.OnPendingInstructionChanged(initialInstruction);
    _stateChangeObserver?.OnMainPhaseChanged(GamePhase.Night);
    _stateChangeObserver?.OnTurnNumberChanged(1);
}
```

#### 3.2.3 Observer Notifications at Mutation Points

Inject observer notifications at each state mutation point. The pattern is: **mutate state first, then notify observer**.

**Phase State Cache Mutations (in `GameSessionKernel.PhaseStateCache.cs`):**

The `GamePhaseStateCache` struct needs access to the observer. This can be achieved by either:
- **Option A**: Passing the observer to each mutation method (verbose but explicit)
- **Option B**: Making `GamePhaseStateCache` a class that holds a reference to the observer

**Recommended: Option A** — Keep the struct immutable in design, pass observer to each call:

```csharp
// In GameSessionKernel.cs wrapper methods
internal void TransitionSubPhase(Enum subPhase)
{
    _phaseStateCache.TransitionSubPhase(subPhase);
    _stateChangeObserver?.OnSubPhaseChanged(subPhase.ToString());
}

internal void StartSubPhaseStage(string subPhaseStage)
{
    _phaseStateCache.StartSubPhaseStage(subPhaseStage);
    _stateChangeObserver?.OnSubPhaseStageChanged(subPhaseStage);
}

internal void CompleteSubPhaseStage()
{
    _phaseStateCache.CompleteSubPhaseStage();
    _stateChangeObserver?.OnSubPhaseStageChanged(null);
}

internal void TransitionListenerAndState(ListenerIdentifier listener, string state)
{
    _phaseStateCache.TransitionListenerAndState(listener, state);
    _stateChangeObserver?.OnListenerChanged(listener, state);
}
```

**SessionMutator Modifications (in `GameSessionKernel.SessionMutator.cs`):**

```csharp
public void SetCurrentPhase(GamePhase newPhase)
{
    kernel._phaseStateCache.TransitionMainPhase(Key, newPhase);
    kernel._stateChangeObserver?.OnMainPhaseChanged(newPhase);

    if (newPhase == GamePhase.Night)
    {
        kernel.IncrementTurnNumber(Key);
        kernel._stateChangeObserver?.OnTurnNumberChanged(kernel.TurnNumber);
    }
}

public void AddLogEntry<T>(T entry) where T : GameLogEntryBase
{
    kernel._gameHistoryLog.AddLogEntry(Key, entry);
    kernel._stateChangeObserver?.OnLogEntryApplied(entry);
}
```

**Pending Instruction Modification (in `GameSessionKernel.cs`):**

```csharp
internal void SetPendingModeratorInstruction(ModeratorInstruction instruction)
{
    _pendingModeratorInstruction = instruction;
    _stateChangeObserver?.OnPendingInstructionChanged(instruction);
}
```

### 3.3 `GameLogEntryBase.ToString()` Override Requirement

Enforce that all `GameLogEntryBase` subclasses provide meaningful string representations for diagnostic output:

```csharp
// In GameLogEntryBase.cs
public abstract record GameLogEntryBase
{
    // Existing members...

    /// <summary>
    /// Provides a human-readable summary of the log entry for diagnostics.
    /// Must be overridden by all subclasses.
    /// </summary>
    public abstract override string ToString();
}
```

**Example Implementation:**

```csharp
// In NightActionLogEntry.cs
public override string ToString() =>
    $"NightAction: {ActionType} by {ActingPlayerId}" +
    (TargetPlayerId.HasValue ? $" targeting {TargetPlayerId}" : "");
```

### 3.4 `GameSession` Constructor Extension

Extend the facade's constructor to pass through the observer:

```csharp
// In GameSession.cs
internal GameSession(
    Guid id,
    ModeratorInstruction initialInstruction,
    List<string> playerNamesInOrder,
    List<MainRoleType> rolesInPlay,
    List<string>? eventCardIdsInDeck = null,
    IStateChangeObserver? stateChangeObserver = null)
{
    _gameSessionKernel = new GameSessionKernel(
        id, initialInstruction, playerNamesInOrder, rolesInPlay, 
        eventCardIdsInDeck, stateChangeObserver);
}
```

### 3.5 Test Infrastructure: `DiagnosticStateObserver`

Create a concrete observer implementation in the test project:

```csharp
// File: Werewolves.Tests/Helpers/DiagnosticStateObserver.cs
namespace Werewolves.Tests.Helpers;

internal class DiagnosticStateObserver : IStateChangeObserver
{
    private readonly List<string> _log = new();
    private readonly object _lock = new();

    public IReadOnlyList<string> Log
    {
        get { lock (_lock) return _log.ToList(); }
    }

    public void OnMainPhaseChanged(GamePhase newPhase)
    {
        lock (_lock) _log.Add($"[Phase] → {newPhase}");
    }

    public void OnSubPhaseChanged(string? newSubPhase)
    {
        lock (_lock) _log.Add($"[SubPhase] → {newSubPhase ?? "(cleared)"}");
    }

    public void OnSubPhaseStageChanged(string? newSubPhaseStage)
    {
        lock (_lock) _log.Add($"[SubPhaseStage] → {newSubPhaseStage ?? "(cleared)"}");
    }

    public void OnListenerChanged(ListenerIdentifier? listener, string? listenerState)
    {
        var listenerStr = listener?.ToString() ?? "(none)";
        var stateStr = listenerState ?? "(none)";
        lock (_lock) _log.Add($"[Listener] → {listenerStr} | State: {stateStr}");
    }

    public void OnTurnNumberChanged(int newTurnNumber)
    {
        lock (_lock) _log.Add($"[Turn] → {newTurnNumber}");
    }

    public void OnPendingInstructionChanged(ModeratorInstruction? instruction)
    {
        var instructionStr = instruction?.GetType().Name ?? "(null)";
        lock (_lock) _log.Add($"[Instruction] → {instructionStr}");
    }

    public void OnLogEntryApplied(GameLogEntryBase entry)
    {
        lock (_lock) _log.Add($"[Log] {entry.GetType().Name}: {entry}");
    }

    public string GetFormattedLog()
    {
        lock (_lock)
        {
            if (_log.Count == 0) return "(no state changes recorded)";
            
            var sb = new StringBuilder();
            sb.AppendLine("=== State Change Timeline ===");
            for (int i = 0; i < _log.Count; i++)
            {
                sb.AppendLine($"{i + 1,4}. {_log[i]}");
            }
            return sb.ToString();
        }
    }

    public void Clear()
    {
        lock (_lock) _log.Clear();
    }
}
```

### 3.6 Test Infrastructure: `GameTestBuilder` Integration

Modify `GameTestBuilder` to inject the observer and expose the diagnostic log:

```csharp
// In GameTestBuilder.cs
public class GameTestBuilder
{
    // Existing fields...
    private readonly DiagnosticStateObserver _diagnosticObserver = new();
    private readonly ITestOutputHelper? _output;

    /// <summary>
    /// Creates a new test builder instance.
    /// </summary>
    public static GameTestBuilder Create(ITestOutputHelper? output = null) => new(output);

    private GameTestBuilder(ITestOutputHelper? output = null)
    {
        _output = output;
    }

    /// <summary>
    /// Gets the diagnostic state change log.
    /// </summary>
    public string DiagnosticLog => _diagnosticObserver.GetFormattedLog();

    /// <summary>
    /// Dumps the diagnostic log to the test output.
    /// Call this in a finally block or when debugging.
    /// </summary>
    public void DumpDiagnostics()
    {
        _output?.WriteLine(DiagnosticLog);
    }

    // Modify StartGame to pass the observer through
    public StartGameConfirmationInstruction StartGame()
    {
        // Existing validation...
        
        // Pass observer to GameService (requires GameService modification - see 3.7)
        var instruction = _gameService.StartNewGame(
            _playerNames, _roles, null, _diagnosticObserver);
        
        // Rest of existing logic...
    }
}
```

### 3.7 `GameService` Constructor Extension

Extend `GameService.StartNewGame` to pass the observer through:

```csharp
// In GameService.cs
public StartGameConfirmationInstruction StartNewGame(
    List<string> playerNamesInOrder,
    List<MainRoleType> rolesInPlay,
    List<string>? eventCardIdsInDeck = null,
    IStateChangeObserver? stateChangeObserver = null)
{
    var gameId = Guid.NewGuid();
    var initialInstruction = GameFlowManager.GetInitialInstruction(rolesInPlay, gameId);
    
    var session = new GameSession(
        gameId, initialInstruction, playerNamesInOrder, rolesInPlay, 
        eventCardIdsInDeck, stateChangeObserver);
    
    _sessions.TryAdd(session.Id, session);
    return initialInstruction;
}
```

**Note:** `IStateChangeObserver` is `internal` to `Werewolves.StateModels`. For `GameService` (in `Werewolves.GameLogic`) to accept it as a parameter, ensure the interface is visible via `InternalsVisibleTo` or consider making it `public` but documenting it as internal-use-only.

### 3.8 Test Base Class for Automatic Failure Dumping

Create an optional base class that automatically dumps diagnostics on failure:

```csharp
// File: Werewolves.Tests/Helpers/DiagnosticTestBase.cs
namespace Werewolves.Tests.Helpers;

public abstract class DiagnosticTestBase : IDisposable
{
    protected readonly ITestOutputHelper Output;
    protected GameTestBuilder? Builder;
    private bool _testCompleted;

    protected DiagnosticTestBase(ITestOutputHelper output)
    {
        Output = output;
    }

    protected GameTestBuilder CreateBuilder()
    {
        Builder = GameTestBuilder.Create(Output);
        return Builder;
    }

    /// <summary>
    /// Call at the end of a successful test to suppress diagnostic dump.
    /// </summary>
    protected void MarkTestCompleted() => _testCompleted = true;

    public void Dispose()
    {
        // If test didn't complete successfully, dump diagnostics
        if (!_testCompleted && Builder != null)
        {
            Output.WriteLine("\n⚠️ TEST DID NOT COMPLETE - DUMPING DIAGNOSTICS:");
            Builder.DumpDiagnostics();
        }
        GC.SuppressFinalize(this);
    }
}
```

**Usage Example:**

```csharp
public class WerewolfAttackTests : DiagnosticTestBase
{
    public WerewolfAttackTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void Werewolf_CanSelectVictim_DuringNightPhase()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(5, werewolfCount: 1)
            .StartGame();
        
        // Test logic...
        
        MarkTestCompleted(); // Suppresses diagnostic dump on success
    }
}
```

### 3.9 Fields Tracked by the Observer

For clarity, the following fields trigger observer notifications:

| Field | Notification Method | Trigger Location |
|-------|---------------------|------------------|
| `GamePhaseStateCache._currentPhase` | `OnMainPhaseChanged` | `SessionMutator.SetCurrentPhase` |
| `GamePhaseStateCache._currentSubPhase` | `OnSubPhaseChanged` | `GameSessionKernel.TransitionSubPhase` |
| `GamePhaseStateCache._currentSubPhaseStage` | `OnSubPhaseStageChanged` | `GameSessionKernel.StartSubPhaseStage`, `CompleteSubPhaseStage` |
| `GamePhaseStateCache._currentListener` + `_currentListenerState` | `OnListenerChanged` | `GameSessionKernel.TransitionListenerAndState` |
| `GameSessionKernel._turnNumber` | `OnTurnNumberChanged` | `SessionMutator.SetCurrentPhase` (when entering Night) |
| `GameSessionKernel._pendingModeratorInstruction` | `OnPendingInstructionChanged` | `GameSessionKernel.SetPendingModeratorInstruction` |
| `GameLogManager` (any entry added) | `OnLogEntryApplied` | `SessionMutator.AddLogEntry` |

**Excluded Fields:**
- `GamePhaseStateCache._previousSubPhaseStages` (list of completed stages - per user request)
- `GamePhaseStateCache` dictionary-based or complex fields

### 3.10 Documentation Updates (`architecture.md`)

Add a section under "Testing Infrastructure":

```markdown
### Diagnostic State Observation

For integration testing, an optional `IStateChangeObserver` can be injected into
`GameSessionKernel` at construction time. This observer receives callbacks for
all state mutations, enabling tests to capture a complete timeline of changes.

- **Production Overhead:** Zero. The observer is null by default.
- **Test Integration:** `GameTestBuilder` automatically injects a `DiagnosticStateObserver`.
- **Failure Output:** `DiagnosticTestBase` automatically dumps the state change log when a test fails.
```

## 4. Impact Analysis

### 4.1 Benefits

1. **Debugging Efficiency**: Developers can immediately see the full state change timeline when a test fails, eliminating manual tracing.

2. **Intermediate Visibility**: Unlike snapshot-based approaches, captures every state transition within a single `ProcessInstruction` call.

3. **Zero Production Overhead**: The observer is optional and null by default. The null-check pattern (`_stateChangeObserver?.Method()`) compiles to a negligible branch.

4. **Encapsulation Preserved**: The `IStateChangeObserver` interface is `internal`, invisible to UI consumers. The production API surface is unchanged.

5. **Test Infrastructure Integration**: Seamlessly integrates with existing `GameTestBuilder` pattern and xUnit's `ITestOutputHelper`.

6. **Future Utility**: The `GameLogEntryBase.ToString()` override requirement provides value beyond diagnostics—useful for game history display, replay features, and debug logging.

7. **Automatic Failure Detection**: The `DiagnosticTestBase` pattern ensures diagnostic output on any test failure without requiring explicit `try/finally` blocks in every test.

### 4.2 Considerations & Mitigations

1. **Production Code Modification**
   - *Concern:* Adding the observer field and null-checks is technically a production code change.
   - *Mitigation:* The interface is `internal`, the field is optional (nullable), and null-checks have negligible performance impact. The diagnostic surface is invisible to external consumers.

2. **Constructor Parameter Proliferation**
   - *Concern:* `GameSessionKernel` and `GameSession` constructors gain another optional parameter.
   - *Mitigation:* Use optional parameters with `null` defaults. Normal production code paths don't need to change. Consider a builder pattern for `GameSession` construction if parameter count grows further.

3. **Thread Safety**
   - *Concern:* The observer might be called from multiple threads if game processing is parallelized.
   - *Mitigation:* `DiagnosticStateObserver` uses locking. If performance becomes an issue, consider lock-free data structures or accept slight log ordering variance.

4. **Observer Notification Placement**
   - *Concern:* Developers might forget to add observer notifications when adding new state fields.
   - *Mitigation:* Document the pattern clearly. Consider a code review checklist item for state mutations. The fields are explicitly enumerated in section 3.9.

5. **`InternalsVisibleTo` Requirement**
   - *Concern:* `GameService` is in `Werewolves.GameLogic` and needs access to `IStateChangeObserver` from `Werewolves.StateModels`.
   - *Mitigation:* Ensure `Werewolves.StateModels` has `[assembly: InternalsVisibleTo("Werewolves.GameLogic")]` and `[assembly: InternalsVisibleTo("Werewolves.Tests")]` in `AssemblyInfo.cs` or the project file.

6. **Test Migration Effort**
   - *Concern:* Existing tests may need updates to use `DiagnosticTestBase` or pass `ITestOutputHelper`.
   - *Mitigation:* Migration is optional. Existing tests continue to work. The diagnostic features are additive—tests can opt-in by inheriting from `DiagnosticTestBase` or manually calling `Builder.DumpDiagnostics()`.

7. **Log Entry `ToString()` Implementation Burden**
   - *Concern:* All `GameLogEntryBase` subclasses must implement `ToString()`.
   - *Mitigation:* This is a one-time effort with clear value. The base record type already provides a default `ToString()` via record semantics—making it abstract enforces intentional, human-readable implementations.
