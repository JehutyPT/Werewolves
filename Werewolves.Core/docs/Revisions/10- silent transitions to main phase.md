# Architectural Revision: Silent Main Phase Transition Handling

**Revision Number:** 10  
**Date:** November 27, 2025  
**Status:** Implemented

---

## Abstract

This revision addresses a bug in the state machine's phase transition handling where silent transitions between main game phases (transitions that do not pause for moderator input) caused the `PhaseManager` to continue executing with stale context. The fix introduces a two-layer solution: (1) self-aware `PhaseManager` instances that detect when they are no longer the active phase and exit cleanly, and (2) an outer routing loop in `GameFlowManager.RouteInputToPhaseHandler` that continues processing into the correct new phase's handler.

---

## Motivation

### The Bug

The game's state machine is structured as a hierarchy:

```
GameFlowManager.RouteInputToPhaseHandler
  → PhaseManager.ProcessInputAndUpdatePhase (loops until instruction produced)
    → SubPhaseManager.Execute (iterates through stages)
      → SubPhaseStage (executes logic/hooks/navigation)
```

Within `PhaseManager<TSubPhaseEnum>.ProcessInputAndUpdatePhase`, a `do-while` loop continues executing sub-phases until a `ModeratorInstruction` is produced:

```csharp
do
{
    var subPhaseState = session.GetSubPhase<TSubPhaseEnum>() ?? _entrySubPhase;
    // ... find and execute sub-phase ...
    result = subPhase.Execute(session, input);
    AttemptTransition(session, subPhaseState, result, subPhase);
} while (result.ModeratorInstruction == null);
```

The `NavigationEndStageSilent(GamePhase phaseEnum)` factory method creates a stage that transitions to a different main phase without producing a moderator instruction:

```csharp
internal static SubPhaseStage NavigationEndStageSilent(GamePhase phaseEnum)
    => new NavigationSubPhaseStage(phaseEnum,
        (_, _) => new MainPhaseHandlerResult(null, phaseEnum));
```

When this executes:
1. `MainPhaseHandlerResult(null, targetPhase)` is returned with a `null` instruction
2. `AttemptTransition` calls `session.TransitionMainPhase(targetPhase)` — the session's main phase **changes**
3. The `while` condition checks `result.ModeratorInstruction == null` — which is `true`
4. **The loop continues**, but the session is now in a different main phase entirely

On the next iteration, `session.GetSubPhase<TSubPhaseEnum>()` attempts to interpret the new phase's state using the **old** phase's enum type, leading to undefined behavior.

### Requirements for the Fix

1. **`PhaseManager` must exit cleanly** when the main phase changes mid-loop
2. **Something must re-route** to the new phase's `PhaseManager` and continue silent execution

---

## Proposed Architectural and Documentation Changes

### Design Decision: Self-Aware Phase Managers with Dynamic Phase Detection

Several approaches were considered:

| Proposal | Description | Trade-offs |
|----------|-------------|------------|
| **A: Loop in RouteInputToPhaseHandler only** | Add outer loop, modify `PhaseManager` exit condition to check result type | Implicit exit condition in `PhaseManager` |
| **B: Explicit result type check** | `PhaseManager` breaks on `MainPhaseHandlerResult`, outer loop re-routes | Two changes, clear separation |
| **C: New PhaseRouter intermediary** | Extract routing logic to dedicated class | Over-engineering for scope |
| **D: Phase-aware PhaseManager** | `PhaseManager` knows its owned phase, exits when no longer active | Most robust, self-contained |

**Chosen Approach: Proposal D with Dynamic Phase Detection**

Rather than passing the `GamePhase` to each `PhaseManager` constructor (which would require refactoring `PhaseDefinitions` initialization), the owned phase is determined dynamically by finding the `PhaseManager` instance in the `PhaseDefinitions` dictionary:

```csharp
private GamePhase? _cachedOwnedPhase;

private GamePhase GetOwnedPhase()
{
    _cachedOwnedPhase ??= GameFlowManager.PhaseDefinitions
        .First(kvp => ReferenceEquals(kvp.Value, this))
        .Key;
    return _cachedOwnedPhase.Value;
}
```

The result is cached after first lookup to avoid repeated dictionary scans.

### Code Changes

#### 1. `PhaseManager<TSubPhaseEnum>` (PhaseManager.cs)

**New using statements:**
```csharp
using Werewolves.GameLogic.Services;
using Werewolves.StateModels.Enums;
```

**New field and method:**
```csharp
private GamePhase? _cachedOwnedPhase;

/// <summary>
/// Determines which GamePhase this manager handles by finding itself in the PhaseDefinitions dictionary.
/// The result is cached after the first lookup.
/// </summary>
private GamePhase GetOwnedPhase()
{
    _cachedOwnedPhase ??= GameFlowManager.PhaseDefinitions
        .First(kvp => ReferenceEquals(kvp.Value, this))
        .Key;
    return _cachedOwnedPhase.Value;
}
```

**Modified `ProcessInputAndUpdatePhase`:**
```csharp
public PhaseHandlerResult ProcessInputAndUpdatePhase(GameSession session, ModeratorResponse input)
{
    var ownedPhase = GetOwnedPhase();
    PhaseHandlerResult result;
    do
    {
        // 1. Determine the current sub-phase state, defaulting to the defined entry point
        var subPhaseState = session.GetSubPhase<TSubPhaseEnum>() ?? _entrySubPhase;
        
        // ... existing logic ...
        
    // Exit if we're no longer the active phase (silent main phase transition occurred)
    } while (result.ModeratorInstruction == null && session.GetCurrentPhase() == ownedPhase);

    return result;
}
```

#### 2. `GameFlowManager.RouteInputToPhaseHandler` (GameFlowManager.cs)

**Modified method:**
```csharp
private static PhaseHandlerResult RouteInputToPhaseHandler(GameSession session, ModeratorResponse input)
{
    PhaseHandlerResult result;
    do
    {
        var currentPhase = session.GetCurrentPhase();
        
        if (!PhaseDefinitions.TryGetValue(currentPhase, out var phaseDef))
        {
            throw new InvalidOperationException($"No phase definition found for phase: {currentPhase}");
        }

        result = phaseDef.ProcessInputAndUpdatePhase(session, input);
    } 
    while (result is MainPhaseHandlerResult { ModeratorInstruction: null });

    // Defensive check: null instructions should only bubble up from MainPhaseHandlerResult
    // during silent phase transitions (handled by the loop above). If we get here with a null
    // instruction, something has gone wrong at the sub-phase or hook level.
    if (result.ModeratorInstruction == null)
    {
        throw new InvalidOperationException(
            $"Internal State Machine Error: Received null ModeratorInstruction from non-MainPhaseHandlerResult. " +
            $"Result type: {result.GetType().Name}, Current phase: {session.GetCurrentPhase()}");
    }

    return result;
}
```

### Documentation Updates

The `architecture.md` document should be updated to reflect:

1. **`PhaseManager` is phase-aware**: Each `PhaseManager` instance can determine which `GamePhase` it manages by finding itself in the `PhaseDefinitions` dictionary, with caching for efficiency.

2. **Silent transition handling**: When a sub-phase stage produces a `MainPhaseHandlerResult` with no instruction, the `PhaseManager` detects the phase change on the next loop iteration and exits cleanly. The `RouteInputToPhaseHandler` outer loop then continues processing in the new phase's handler.

3. **Defensive null instruction check**: After the routing loop exits, an invariant check ensures no null instructions escape from non-`MainPhaseHandlerResult` results, as this would indicate a bug in sub-phase or hook stage logic.

---

## Impact Analysis

### Benefits

1. **Bug fix**: Silent main phase transitions now work correctly, allowing phases like Dawn to silently transition to Day when there are no victims to announce.

2. **Self-contained fix**: The `PhaseManager` change is entirely internal — no changes to constructors, `PhaseDefinitions` initialization, or public APIs.

3. **Robust invariant enforcement**: The defensive check in `RouteInputToPhaseHandler` catches any future bugs where null instructions incorrectly bubble up from sub-phase or hook stages.

4. **Minimal coupling increase**: While `PhaseManager` now references `GameFlowManager.PhaseDefinitions`, this coupling is acceptable given that `PhaseManager` instances are exclusively created within that dictionary.

### Considerations & Mitigations

| Consideration | Mitigation |
|---------------|------------|
| **Runtime dictionary scan** | Cached after first lookup; dictionary has only 3-4 entries, so O(n) lookup is negligible |
| **`PhaseManager` coupling to `GameFlowManager`** | Acceptable since `PhaseManager` instances are only used within `PhaseDefinitions`; would throw if used elsewhere (which would be a misuse) |
| **Potential infinite loop in routing** | The loop only continues on `MainPhaseHandlerResult { ModeratorInstruction: null }`; any other result type or non-null instruction exits. Phase handlers are designed to eventually produce instructions. |
| **Defensive exception may mask root cause** | Exception message includes result type and current phase to aid debugging |

### Files Modified

- `Werewolves.GameLogic/Models/StateMachine/PhaseManager.cs`
- `Werewolves.GameLogic/Services/GameFlowManager.cs`
