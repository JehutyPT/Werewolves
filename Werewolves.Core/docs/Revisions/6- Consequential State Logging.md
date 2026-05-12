# Architectural Proposal: Consequential State Logging & Unified Status API

## 1. Abstract
This proposal recommends a strategic refinement to the State Management Philosophy and API design within the `Werewolves` solution. Currently, the architecture restricts the `GameHistoryLog` to strictly **non-deterministic inputs** and encourages specific, granular methods for state mutation.

We propose two major shifts:
1.  **Consequential State Logging:** Expanding the log definition to include **deterministic outcomes** of complex rule resolutions (e.g., protection loss, infection). This ensures historical immutability regardless of future logic changes.
2.  **Unified Status API:** Implementing a generic `ApplyStatusEffect` pattern within `GameSession` to handle these mutations, replacing the need for ad-hoc methods (e.g., `WoundElder`, `InfectPlayer`) and significantly reducing the API surface area.

## 2. Motivation

The current architecture relies on the assumption that replaying non-deterministic *inputs* through the `GameFlowManager` will always correctly reconstruct the game state. However, analysis reveals critical flaws in this approach regarding long-term maintenance and encapsulation.

### 2.1. The Logic Versioning Paradox
Game rules evolve. If the logic for "Elder Protection" changes in Version 2.0 (e.g., "Defender now protects against the specific attack that wounds the Elder"), loading a game log from Version 1.0 would result in a corrupted state. The V2.0 logic would process V1.0 inputs and conclude the Elder was *not* wounded, effectively rewriting history.
*   **Requirement:** We must freeze the *consequence* of the rule resolution at the moment it occurs to guarantee truthful replay.

### 2.2. Assembly Separation & Dependency Inversion
`Werewolves.StateModels` is designed as a passive state container. If we rely on logic replay to determine if a player is `Infected` or `Wounded`, the State model implicitly depends on the Rules Engine (`Werewolves.GameLogic`) to interpret its own data.
*   **Requirement:** `StateModels` must be able to reconstruct the full player state (including derived flags) using only its own internal logic (Log `Apply` methods).

### 2.3. API Surface Area & Encapsulation
Creating specific mutation methods on `GameSession` (e.g., `WoundElder()`, `InfectPlayer()`, `MutePlayer()`) leads to:
*   **Pollution:** An ever-growing list of ad-hoc methods on the core class.
*   **Logic Leakage:** `WoundElder()` implies the State assembly knows how to find the Elder.
*   **Encapsulation Breach:** These methods require privileged access to `PlayerState` setters, bypassing the strict `StateMutator` pattern.

## 3. Proposed Changes

### 3.1. Architectural Changes (Code Level)

1.  **New Enum Definition:**
    Introduce `StatusEffectType` in `Werewolves.StateModels` to catalog all unary state mutations.
    ```csharp
    public enum StatusEffectType { ElderProtectionLost, LycanthropyInfection, WildChildTransformation, FoxPowerLost, ... }
    ```

2.  **Unified Log Entry:**
    Create a single, consolidated log entry for these events.
    ```csharp
    public record PlayerStatusEffectLogEntry(
        int TurnNumber, 
        GamePhase Phase, 
        Guid TargetPlayerId, 
        StatusEffectType EffectType
    ) : GameLogEntryBase(TurnNumber, Phase) { ... }
    ```

3.  **Unified Session API:**
    Refactor `GameSession` to expose a single, curated entry point for state flags.
    ```csharp
    // Replaces WoundElder(), InfectPlayer(), etc.
    internal void ApplyStatusEffect(StatusEffectType type, Guid targetId) {
        var log = new PlayerStatusEffectLogEntry(..., targetId, type);
        _gameHistoryLog.Add(log);
        log.Apply(_stateMutator);
    }
    ```

4.  **Logic Flow Correction:**
    Update `GameFlowManager.HandleDawnCalculateVictims` to strictly enforce precedence (Defender -> Witch -> Elder) and utilize the new API.

### 3.2. Documentation Changes (`architecture.md`)

**Section: State Management Philosophy**
*   **Current:** "The `GameSession.GameHistoryLog` is the single, canonical source of truth for all **persistent, non-deterministic** game events."
*   **Proposed:** "The `GameSession.GameHistoryLog` is the single, canonical source of truth for all **state-altering** game events. This includes both **non-deterministic inputs** (moderator choices) and **deterministic consequences** (rule resolutions like infection) that must be preserved historically independent of future rule logic changes."

**Section: Game Logs**
*   Add Subsection: **Consequential State Logs (Deterministic Resolutions)**.
*   *Definition:* "Entries that record the **result** of a rule resolution (e.g., `PlayerStatusEffectLogEntry`). While technically deterministic based on the logic at the time of execution, they are logged to explicitly freeze the state change and decouple history from logic versions."

**Section: GameSession Class (Encapsulated Public API)**
*   **Update:** Explicitly mention the **Unified Status API** pattern.
*   *Text:* "`GameSession` exposes a curated, generic API for unary state modifications (`ApplyStatusEffect`) rather than specific methods for every possible state flag. This maintains a stable interface while allowing the `StatusEffectType` enum to expand."

## 4. Impact Analysis

### 4.1. Benefits
*   **Immutable History:** Saved games are protected against "retconning" caused by engine updates. A player infected in V1 remains infected in V2, even if V2 rules would have prevented it.
*   **Clean Separation of Concerns:**
    *   `GameLogic` decides **WHO** and **WHY** (Rule Resolution).
    *   `StateModels` handles **HOW** (Recording and Storing).
*   **Stable Interface:** Adding a new role capability (e.g., "Silenced") only requires adding an Enum value and a Switch Case in the Log Entry. The `GameSession` public/internal API remains untouched.
*   **Auditing:** Queries for state changes become trivial (`Log.OfType<PlayerStatusEffectLogEntry>().Where(...)`) without duplicating complex rule logic in the query.

### 4.2. Considerations
*   **Data Redundancy:** We are effectively logging "Double Data" (The input action + the result).
    *   *Mitigation:* This is an intentional architectural trade-off (Checkpointing) to ensure stability. The storage cost is negligible.
*   **Generic API Scope Creep:** There is a risk of trying to force non-status events into this generic method.
    *   *Mitigation:* The API is strictly limited to **Unary** operations (One Target, One Enum). Complex interactions (e.g., "Swapping Roles" between two players) must remain as distinct methods with distinct Log Entries.

### 4.3. Conclusion
Adopting the Consequential State Logging pattern via a Unified Status API resolves the identified fragility in versioning and encapsulation. It aligns the implementation with the "Open-Closed Principle" (open for extension via Enum, closed for modification via Generic API) and solidifies the `StateModels` assembly as a truly self-contained source of truth.