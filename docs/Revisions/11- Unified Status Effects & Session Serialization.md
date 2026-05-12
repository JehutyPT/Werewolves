# Implementation Plan: Unified Status Effects & Session Serialization

**Created:** December 3, 2025  
**Author:** Planner Agent  

---

## Abstract

This plan addresses two critical features required by the Werewolves.UI.Maui project:

1. **Unified Status Effects System** — Merge `SecondaryRoleType` into `StatusEffectTypes` and expose a `GetActiveStatusEffects()` method on `IPlayerState` for UI consumption.

2. **Session Serialization/Deserialization** — Implement full game session persistence using `System.Text.Json`, enabling games to survive app closure and resume.

The status effects unification is an architectural simplification that consolidates two overlapping concepts (secondary roles and status effects) into a single, coherent model. This reduces complexity and provides the UI with a unified API for displaying player status.

---

## Motivation

### UI Requirements
The `Werewolves.UI.Maui` project requires:
- **PlayerList expansion panel** needs to display status effect icons for each player without understanding which `IPlayerState` properties constitute "status effects"
- **Persistence** to survive app closure/resume, requiring session serialization to `FileSystem.AppDataDirectory`

### Architectural Simplification
The current architecture has two overlapping concepts:
- **`SecondaryRoleType`** (Flags enum): `Lovers`, `Charmed`, `TownCrier`, `Executioner`, `Sheriff` — described as "stacked roles linked to specific GameHooks"
- **`StatusEffectTypes`** (enum): `ElderProtectionLost`, `LycanthropyInfection`, `WildChildChanged`, `LynchingImmunityUsed` — described as "persistent conditions affecting gameplay"

Both fundamentally represent **persistent conditions affecting gameplay**. The only distinction is that some effects can register as hook listeners. This distinction doesn't warrant separate type hierarchies; it's better modeled as a capability (can-register-to-hooks) rather than a type split.

---

## Proposed Changes

### Phase 1: Unified Status Effects System

#### 1.1 Enum Consolidation

**File:** `Werewolves.Core.StateModels/Enums/StatusEffectTypes.cs` (rename from location in `NightActionType.cs`)

Merge `SecondaryRoleType` values into `StatusEffectTypes`:

```csharp
/// <summary>
/// Specifies all persistent status effects that can be applied to a player.
/// These effects persist across turns and affect gameplay or UI display.
/// Some effects can register as hook listeners (e.g., Sheriff, Executioner).
/// </summary>
[Flags]
public enum StatusEffectTypes
{
    None = 0,
    
    // Persistent conditions (non-hookable)
    ElderProtectionLost = 1 << 0,      // Elder's extra life has been used
    LycanthropyInfection = 1 << 1,     // Player has been infected by the wolf father
    WildChildChanged = 1 << 2,         // Wild Child has changed their role
    LynchingImmunityUsed = 1 << 3,     // Village Idiot has used their immunity
    
    // Hookable status effects (formerly SecondaryRoleType)
    Sheriff = 1 << 4,                  // Player holds the Sheriff title
    Lovers = 1 << 5,                   // Player is one of the Lovers
    Charmed = 1 << 6,                  // Player has been charmed by the Piper
    TownCrier = 1 << 7,                // Player is the Town Crier (New Moon)
    Executioner = 1 << 8,              // Player is the Executioner (New Moon)
}
```

**Rationale for Flags:** Multiple status effects can be active simultaneously (e.g., Sheriff + Infected + Charmed). The Flags pattern allows efficient storage and querying.

#### 1.2 Remove SecondaryRoleType

**Files to modify:**
- `Werewolves.Core.StateModels/Enums/SecondaryRoleType.cs` — **DELETE** this file
- All files referencing `SecondaryRoleType` — update to use `StatusEffectTypes`

#### 1.3 Update IPlayerState Interface

**File:** `Werewolves.Core.StateModels/Core/GameSessionKernel.Player.cs`

```csharp
public interface IPlayerState
{
    public MainRoleType? MainRole { get; }
    public PlayerHealth Health { get; }
    public Team Team { get; }
    
    /// <summary>
    /// Returns a list of all currently active status effects for this player.
    /// Intended for UI consumption to display status effect icons.
    /// </summary>
    public List<StatusEffectTypes> GetActiveStatusEffects();
    
    /// <summary>
    /// Checks if this player has a specific status effect active.
    /// This is the single method that performs bitwise flag checks.
    /// </summary>
    public bool HasStatusEffect(StatusEffectTypes effect);
}
```

**Removed Properties:** The following convenience boolean properties are **removed** from `IPlayerState`:
- `IsInfected` → Use `HasStatusEffect(StatusEffectTypes.LycanthropyInfection)`
- `IsSheriff` → Use `HasStatusEffect(StatusEffectTypes.Sheriff)`
- `HasUsedElderExtraLife` → Use `HasStatusEffect(StatusEffectTypes.ElderProtectionLost)`
- `IsImmuneToLynching` → Computed property, consider moving to extension method
- `LynchingImmunityAnnouncement` → Move to extension method or separate helper
- `SecondaryRoles` → Replaced by `ActiveEffects` internal field

**Implementation in `PlayerState`:**

```csharp
private class PlayerState : IPlayerState
{
    public MainRoleType? MainRole { get; internal set; } = null;
    public PlayerHealth Health { get; internal set; } = PlayerHealth.Alive;
    
    // Internal flags field - not exposed on interface
    internal StatusEffectTypes ActiveEffects { get; set; } = StatusEffectTypes.None;
    
    public Team Team => /* computed from MainRole and effects */;
    
    /// <summary>
    /// Checks if a specific status effect is active.
    /// For None: returns true only if the player has zero active effects.
    /// For other effects: performs standard HasFlag check.
    /// </summary>
    public bool HasStatusEffect(StatusEffectTypes effect) => 
        effect == StatusEffectTypes.None 
            ? ActiveEffects == StatusEffectTypes.None
            : ActiveEffects.HasFlag(effect);
    
    /// <summary>
    /// Returns all active status effects as a list for UI consumption.
    /// </summary>
    public List<StatusEffectTypes> GetActiveStatusEffects()
    {
        var effects = new List<StatusEffectTypes>();
        foreach (StatusEffectTypes effect in Enum.GetValues<StatusEffectTypes>())
        {
            if (effect != StatusEffectTypes.None && HasStatusEffect(effect))
            {
                effects.Add(effect);
            }
        }
        return effects;
    }
    
    // Internal-only mutation methods (accessible only by SessionMutator)
    internal void AddEffect(StatusEffectTypes effect) => 
        ActiveEffects |= effect;
    
    internal void RemoveEffect(StatusEffectTypes effect) => 
        ActiveEffects &= ~effect;
}
```

**Key Design:**
- `HasStatusEffect()` is the **single method** that performs bitwise flag checks
- `ActiveEffects` field is **internal** (not exposed on `IPlayerState`)
- `AddEffect()`/`RemoveEffect()` are **internal-only** on `PlayerState`, accessible only via `SessionMutator`

#### 1.4 Update ListenerIdentifier

**File:** `Werewolves.Core.StateModels/Models/ListenerIdentifier.cs` (or wherever it's defined)

```csharp
public record ListenerIdentifier 
{ 
    public GameHookListenerType ListenerType { get; }
    public string ListenerId { get; }
    
    // Factory methods for clarity
    public static ListenerIdentifier ForRole(MainRoleType role) => 
        new() { ListenerType = GameHookListenerType.MainRole, ListenerId = role.ToString() };
    
    public static ListenerIdentifier ForStatusEffect(StatusEffectTypes effect) => 
        new() { ListenerType = GameHookListenerType.StatusEffect, ListenerId = effect.ToString() };
    
    public static ListenerIdentifier ForEvent(string eventId) => 
        new() { ListenerType = GameHookListenerType.SpiritCard, ListenerId = eventId };
}
```

#### 1.5 Update GameHookListenerType Enum

**File:** `Werewolves.Core.StateModels/Enums/GameHookListenerType.cs` (or wherever it's defined)

```csharp
public enum GameHookListenerType
{
    MainRole,
    StatusEffect,    // Renamed from SecondaryRole
    SpiritCard
}
```

#### 1.6 Add PlayerState Extension Helpers

**New File:** `Werewolves.Core.StateModels/Extensions/PlayerExtensionHelpers.cs`

```csharp
public static class PlayerExtensionHelpers
{
    /// <summary>
    /// Filters players by those who have a specific status effect.
    /// </summary>
    public static IEnumerable<IPlayer> WithStatusEffect(
        this IEnumerable<IPlayer> players, StatusEffectTypes effect) =>
        players.Where(p => p.State.HasStatusEffect(effect));
    
    /// <summary>
    /// Filters players by those who do NOT have a specific status effect.
    /// </summary>
    public static IEnumerable<IPlayer> WithoutStatusEffect(
        this IEnumerable<IPlayer> players, StatusEffectTypes effect) =>
        players.Where(p => !p.State.HasStatusEffect(effect));
}
```

**Note:** No convenience extension methods for specific status effects (e.g., `IsInfected()`, `IsSheriff()`). Consumers should use `HasStatusEffect(StatusEffectTypes.X)` directly.

#### 1.7 Update ISessionMutator Interface

**File:** `Werewolves.Core.StateModels/Core/ISessionMutator.cs`

Update to use the new status effect pattern. The existing methods like `SetPlayerInfected`, `SetElderExtraLifeUsed` are replaced with a single generic method:

```csharp
void SetStatusEffect(Guid playerId, StatusEffectTypes effect, bool isActive);
```

**Implementation:** The `SessionMutator` implementation calls `PlayerState.AddEffect()` or `PlayerState.RemoveEffect()` (the internal-only methods) based on the `isActive` parameter.

#### 1.8 Update StatusEffectLogEntry

**File:** `Werewolves.Core.StateModels/Log/StatusEffectLogEntry.cs`

Simplify the `InnerApply` method to use the unified mutation method:

```csharp
protected override GameLogEntryBase InnerApply(ISessionMutator mutator)
{
    mutator.SetStatusEffect(PlayerId, EffectType, true);
    return this;
}
```

The complex switch statement is no longer needed since all effects are handled uniformly.

---

### Phase 2: Session Serialization

#### 2.1 Create JSON Converters

**New File:** `Werewolves.Core.StateModels/Serialization/GameLogEntryConverter.cs`

Polymorphic converter for `GameLogEntryBase` derived types:

```csharp
public class GameLogEntryConverter : JsonConverter<GameLogEntryBase>
{
    // Discriminator-based serialization for:
    // - AssignRoleLogEntry
    // - DayActionLogEntry
    // - NightActionLogEntry
    // - PhaseTransitionLogEntry
    // - PlayerEliminatedLogEntry
    // - StatusEffectLogEntry
    // - VictoryConditionMetLogEntry
    // - VoteOutcomeReportedLogEntry
}
```

**New File:** `Werewolves.Core.StateModels/Serialization/ModeratorInstructionConverter.cs`

Polymorphic converter for `ModeratorInstruction` derived types:

```csharp
public class ModeratorInstructionConverter : JsonConverter<ModeratorInstruction>
{
    // Discriminator-based serialization for:
    // - ConfirmationInstruction
    // - SelectPlayersInstruction
    // - AssignRolesInstruction
    // - SelectOptionsInstruction
    // - StartGameConfirmationInstruction
    // - FinishedGameConfirmationInstruction
    // etc.
}
```

#### 2.2 Create Serialization DTOs

**New File:** `Werewolves.Core.StateModels/Serialization/GameSessionDto.cs`

```csharp
internal class GameSessionDto
{
    public Guid Id { get; set; }
    public List<PlayerDto> Players { get; set; } = new();
    public List<Guid> SeatingOrder { get; set; } = new();
    public List<MainRoleType> RolesInPlay { get; set; } = new();
    public int TurnNumber { get; set; }
    
    // Transient state
    public GamePhaseStateCacheDto PhaseStateCache { get; set; } = new();
    public ModeratorInstruction? PendingInstruction { get; set; }
    
    // Event source
    public List<GameLogEntryBase> GameHistoryLog { get; set; } = new();
}

internal class PlayerDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public MainRoleType? MainRole { get; set; }
    public StatusEffectTypes ActiveEffects { get; set; }
    public PlayerHealth Health { get; set; }
}

internal class GamePhaseStateCacheDto
{
    public GamePhase CurrentPhase { get; set; }
    public string? SubPhase { get; set; }
    public string? ActiveSubPhaseStage { get; set; }
    public List<string> CompletedSubPhaseStages { get; set; } = new();
    public string? CurrentListenerId { get; set; }
    public string? CurrentListenerType { get; set; }
    public string? CurrentListenerState { get; set; }
}
```

#### 2.3 Implement Serialization in GameSessionKernel

**File:** `Werewolves.Core.StateModels/Core/GameSessionKernel.cs`

```csharp
internal string Serialize()
{
    var dto = new GameSessionDto
    {
        Id = Id,
        TurnNumber = _turnNumber,
        SeatingOrder = _playerSeatingOrder.ToList(),
        RolesInPlay = _rolesInPlay.ToList(),
        PendingInstruction = _pendingModeratorInstruction,
        GameHistoryLog = _gameHistoryLog.GetAllLogEntries().ToList(),
        PhaseStateCache = _phaseStateCache.ToDto(),
        Players = _players.Values.Select(p => new PlayerDto
        {
            Id = p.Id,
            Name = p.Name,
            MainRole = p.State.MainRole,
            ActiveEffects = p.State.ActiveEffects,
            Health = p.State.Health
        }).ToList()
    };

    var options = new JsonSerializerOptions
    {
        Converters = 
        { 
            new GameLogEntryConverter(),
            new ModeratorInstructionConverter(),
            new JsonStringEnumConverter()
        },
        WriteIndented = false
    };

    return JsonSerializer.Serialize(dto, options);
}

public static GameSessionKernel Deserialize(string json)
{
    var options = new JsonSerializerOptions
    {
        Converters = 
        { 
            new GameLogEntryConverter(),
            new ModeratorInstructionConverter(),
            new JsonStringEnumConverter()
        }
    };

    var dto = JsonSerializer.Deserialize<GameSessionDto>(json, options)
        ?? throw new InvalidOperationException("Failed to deserialize game session");

    return new GameSessionKernel(dto);
}

// Private constructor for deserialization
private GameSessionKernel(GameSessionDto dto)
{
    Id = dto.Id;
    _turnNumber = dto.TurnNumber;
    _playerSeatingOrder = dto.SeatingOrder;
    _rolesInPlay = dto.RolesInPlay;
    _pendingModeratorInstruction = dto.PendingInstruction;
    _phaseStateCache = GamePhaseStateCache.FromDto(dto.PhaseStateCache);
    
    foreach (var playerDto in dto.Players)
    {
        var player = new Player(playerDto.Name, playerDto.Id);
        player.State.MainRole = playerDto.MainRole;
        player.State.ActiveEffects = playerDto.ActiveEffects;
        player.State.Health = playerDto.Health;
        _players.Add(player.Id, player);
    }
    
    // Restore log entries (already deserialized, just store them)
    foreach (var entry in dto.GameHistoryLog)
    {
        _gameHistoryLog.AppendEntry(entry);
    }
}
```

#### 2.4 Implement Serialization in GameSession (Facade)

**File:** `Werewolves.Core.StateModels/Core/GameSession.cs`

```csharp
public string Serialize() => _gameSessionKernel.Serialize();

internal GameSession(string json)
{
    _gameSessionKernel = GameSessionKernel.Deserialize(json);
}
```

#### 2.5 Implement RehydrateSession in GameService

**File:** `Werewolves.Core.GameLogic/Services/GameService.cs`

```csharp
internal Guid RehydrateSession(string serializedSession)
{
    var session = new GameSession(serializedSession);
    _activeSessions[session.Id] = session;
    return session.Id;
}
```

---

### Documentation Changes

#### Update `Documentation/architecture.md`

1. **Remove `SecondaryRoleType`** from all references
2. **Update `StatusEffectTypes`** documentation to include the merged values
3. **Update `IPlayerState`** documentation to include `GetActiveStatusEffects()` and `ActiveEffects`
4. **Update `ListenerIdentifier`** documentation to use `StatusEffectTypes` instead of `SecondaryRoleType`
5. **Update `GameHookListenerType`** enum documentation (rename `SecondaryRole` to `StatusEffect`)
6. **Mark Session Persistence as "Implemented"** instead of "Planned"

#### Update `Documentation/game-rules.md`

No changes required — game rules remain the same.

#### Update `Documentation/tests.md`

Add test specifications for:
1. **Status Effects API Tests**
   - `GetActiveStatusEffects()` returns correct effects
   - Flag manipulation via `AddStatusEffect`/`RemoveStatusEffect`
   - Backward compatibility of convenience properties
   
2. **Serialization Tests**
   - Round-trip serialization/deserialization
   - Polymorphic type handling
   - State integrity after deserialization
   - Edge cases (empty game, mid-phase serialization)

---

### Test Changes

#### New Test Files

**`Werewolves.Core.Tests/Unit/StatusEffectsTests.cs`**
- Test `GetActiveStatusEffects()` decomposition
- Test flag-based effect manipulation
- Test convenience property accessors

**`Werewolves.Core.Tests/Unit/SerializationTests.cs`**
- Test round-trip serialization of `GameSession`
- Test polymorphic `GameLogEntryBase` serialization
- Test polymorphic `ModeratorInstruction` serialization
- Test `GamePhaseStateCache` serialization

**`Werewolves.Core.Tests/Integration/SerializationIntegrationTests.cs`**
- Test full game flow with mid-game serialization/deserialization
- Test that deserialized game can continue correctly

---

## Impact Analysis

### Benefits

1. **Simplified Mental Model:** One enum for all persistent player conditions instead of two overlapping concepts
2. **UI Enablement:** `GetActiveStatusEffects()` provides exactly what the UI needs
3. **Persistence:** Games survive app restarts, improving user experience
4. **Future-Proof:** New status effects can be added to one place
5. **Reduced Coupling:** UI doesn't need to understand internal property semantics

### Considerations & Mitigations

| Consideration | Mitigation |
|--------------|------------|
| **Breaking Change:** `SecondaryRoleType` removal | This is intentional. All compile errors must be resolved as part of implementation. |
| **Flags Enum Complexity:** Flags require bitwise operations | Provide helper methods and ensure `GetActiveStatusEffects()` abstracts this |
| **Serialization Versioning:** Future schema changes | Include version field in DTO for future migration support |
| **Performance:** JSON serialization overhead | Acceptable for save/load operations; not used in hot paths |
| **Polymorphic Serialization:** Complex converter implementation | Use discriminator pattern with comprehensive test coverage |

### Files to Modify (Summary)

**StateModels Assembly:**
- `Enums/SecondaryRoleType.cs` — DELETE
- `Enums/NightActionType.cs` — Extract `StatusEffectTypes` to own file, convert to Flags
- `Core/GameSessionKernel.Player.cs` — Update `IPlayerState`, `PlayerState` (remove bool properties, add `HasStatusEffect`, internal mutation methods)
- `Core/GameSessionKernel.cs` — Add serialization logic
- `Core/GameSession.cs` — Update `Serialize()`, deserialization constructor
- `Core/ISessionMutator.cs` — Replace multiple setters with `SetStatusEffect(playerId, effect, isActive)`
- `Core/GameSessionKernel.SessionMutator.cs` — Implement new method using `PlayerState.AddEffect`/`RemoveEffect`
- `Log/StatusEffectLogEntry.cs` — Simplify `InnerApply`
- `Models/ListenerIdentifier.cs` — Update to use `StatusEffectTypes`
- **NEW:** `Extensions/PlayerExtensionHelpers.cs` — Filtering and convenience extension methods
- **NEW:** `Serialization/GameLogEntryConverter.cs`
- **NEW:** `Serialization/ModeratorInstructionConverter.cs`
- **NEW:** `Serialization/GameSessionDto.cs`
- **NEW:** `Enums/StatusEffectTypes.cs` (dedicated file)

**GameLogic Assembly:**
- `Services/GameService.cs` — Implement `RehydrateSession`
- Any role listeners using `SecondaryRoleType` — Update imports

**Tests Assembly:**
- **NEW:** `Unit/StatusEffectsTests.cs`
- **NEW:** `Unit/SerializationTests.cs`
- **NEW:** `Integration/SerializationIntegrationTests.cs`
- Existing tests referencing `SecondaryRoleType` — Update

---

## Implementation Order

1. **Phase 1.1-1.2:** Enum consolidation (StatusEffectTypes as Flags, delete SecondaryRoleType)
2. **Phase 1.3:** Update IPlayerState and PlayerState (remove bool properties, add `HasStatusEffect`, internal mutation)
3. **Phase 1.4-1.5:** Update ListenerIdentifier, GameHookListenerType
4. **Phase 1.6:** Create PlayerExtensionHelpers with filtering and convenience methods
5. **Phase 1.7-1.8:** Update ISessionMutator, SessionMutator, StatusEffectLogEntry
6. **Compile & Fix:** Resolve all compilation errors from SecondaryRoleType removal and bool property removal
7. **Phase 2.1-2.2:** Create JSON converters and DTOs
8. **Phase 2.3-2.5:** Implement serialization in Kernel, Facade, and Service
9. **Tests:** Write unit and integration tests
10. **Documentation:** Update architecture.md and tests.md

---

## Open Questions (None)

All questions were resolved during the clarification phase.

---

## Agent Documentation Review

**Files Reviewed:** `.github/agents/*.md`

After analyzing the agent definitions against the planned changes, I found the following updates needed:

### `coder-agent.md` Updates Required

**Section "3. Modifying Persistent State"** mentions specific methods like `session.EliminatePlayer()`, `session.PerformNightAction()`. This section should be updated to include:
- Reference to `session.ApplyStatusEffect()` being replaced by `SetStatusEffect()` pattern in `ISessionMutator`

**Section "Development Guidelines"** mentions `ISessionMutator` pattern. After this implementation:
- Update to reflect the new `SetStatusEffect(playerId, effect, isActive)` unified method

### `docs-agent.md` Updates Required

No updates needed. The docs agent's workflow is generic and doesn't reference specific enums or types.

### `qa-agent.md` Updates Required

**Section "3. Verification Strategy"** mentions checking `GameHistoryLog`. After this implementation:
- Status effect verification will use `StatusEffectLogEntry` with the unified `StatusEffectTypes` enum
- Tests should verify effects via `player.State.HasStatusEffect(StatusEffectTypes.X)`

### Recommendation

These updates are minor and can be incorporated during the **Documentation phase** by the `docs_agent`. No blocking changes are required before the `coder_agent` begins work.
