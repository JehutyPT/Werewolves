# Integration Test Plan

This document defines the high-level integration test cases for the Werewolves moderator helper application. The initial scope focuses on a **simple game** consisting of:
- **Werewolves** (SimpleWerewolf)
- **Seer**
- **Simple Villagers**

Secondary roles (Sheriff, Lovers, etc.) and advanced roles are excluded from this initial test suite.

---

## Test Categories Overview

| Category | Description | Test File |
|----------|-------------|-----------|
| Game Lifecycle | Game creation, setup, and phase cycle completion | `GameLifecycleTests.cs` |
| Phase Transitions | State machine transitions and cache behavior | `PhaseTransitionTests.cs` |
| Night Actions | Werewolf attacks, Seer investigations, logging | `NightActionTests.cs` |
| Dawn Resolution | Victim calculation, eliminations, role reveals | `DawnResolutionTests.cs` |
| Day Voting | Vote outcomes, ties, elimination flow | `DayVotingTests.cs` |
| Victory Conditions | Both win conditions at correct moments | `VictoryConditionTests.cs` |
| Input Validation | Invalid/unexpected input rejection | `InputValidationTests.cs` |
| Event Sourcing | Log integrity and state reconstruction | `EventSourcingTests.cs` |
| Extension Methods | Role grouping and helper utilities | `ExtensionMethodTests.cs` |
| Status Effects | Unified status effects API and mutations | `StatusEffectsTests.cs` |
| Serialization | Session persistence and restoration | `SerializationTests.cs` |

---

## 1. Game Lifecycle Tests

Tests covering the complete game creation and lifecycle flow.

### 1.1 Game Creation
- **GL-001**: `StartNewGame_WithValidRolesAndPlayers_ReturnsStartGameConfirmationInstruction`
  - Given: Valid player names and matching roles (3 players: 1 WW, 1 Seer, 1 Villager)
  - When: `GameService.StartNewGame()` is called
  - Then: Returns `StartGameConfirmationInstruction` with valid `GameId`

- **GL-002**: `StartNewGame_WithEmptyRoles_ThrowsArgumentException`
  - Given: Valid player names but empty roles list
  - When: `GameService.StartNewGame()` is called
  - Then: Throws `ArgumentException`

- **GL-003**: `StartNewGame_CreatesSessionWithCorrectPlayerCount`
  - Given: 5 players with matching roles
  - When: Game is started and state retrieved
  - Then: Session contains exactly 5 players in correct seating order

### 1.4 Game Configuration Validation
- **GL-030**: `TryGetPlayerConfigIssues_WithDuplicateNames_ReturnsNonUniqueError`
  - Given: Player list with duplicate names (case-insensitive)
  - When: `TryGetPlayerConfigIssues` is called
  - Then: Returns `true` with `NonUniquePlayerNames` error

- **GL-031**: `TryGetPlayerConfigIssues_WithFewerThan5Players_ReturnsTooFewError`
  - Given: Player list with 4 players
  - When: `TryGetPlayerConfigIssues` is called
  - Then: Returns `true` with `TooFewPlayers` error

- **GL-032**: `TryGetPlayerConfigIssues_With5ValidPlayers_ReturnsNoIssues`
  - Given: Player list with 5 unique names
  - When: `TryGetPlayerConfigIssues` is called
  - Then: Returns `false` with empty issues list

- **GL-033**: `GetExpectedRoleCount_WithoutSpecialRoles_ReturnsPlayerCount`
  - Given: 5 players, roles without Thief or Actor
  - When: `GetExpectedRoleCount` is called
  - Then: Returns 5

- **GL-034**: `GetExpectedRoleCount_WithThief_ReturnsPlayerCountPlus2`
  - Given: 5 players, roles including Thief
  - When: `GetExpectedRoleCount` is called
  - Then: Returns 7

- **GL-035**: `GetExpectedRoleCount_WithActor_ReturnsPlayerCountPlus3`
  - Given: 5 players, roles including Actor
  - When: `GetExpectedRoleCount` is called
  - Then: Returns 8

- **GL-036**: `GetExpectedRoleCount_WithThiefAndActor_ReturnsPlayerCountPlus5`
  - Given: 5 players, roles including both Thief and Actor
  - When: `GetExpectedRoleCount` is called
  - Then: Returns 10

### 1.2 Game Start Confirmation
- **GL-010**: `ConfirmGameStart_TransitionsToNightPhase`
  - Given: Game started, pending `StartGameConfirmationInstruction`
  - When: Moderator confirms and triggers Night phase execution
  - Then: Night phase begins, sub-phase is `Start`

- **GL-011**: `ConfirmGameStart_SetsCorrectTurnNumber`
  - Given: Game start confirmed
  - When: Night phase begins
  - Then: `TurnNumber` is 1

### 1.3 Full Game Cycle
- **GL-020**: `CompleteGameCycle_NightToDawnToDay_TransitionsCorrectly`
  - Given: Game in Night phase with simple setup (1 WW, 1 Seer, 2 Villagers)
  - When: All night actions completed
  - Then: Phase transitions Night → Dawn → Day in sequence

---

## 2. Phase Transition Tests

Tests validating the state machine transitions and cache behavior.

### 2.1 Valid Transitions
- **PT-001**: `NewGame_StartsInNightPhase`
  - Given: Game created and start confirmed
  - When: Game begins
  - Then: Initial phase is `Night.Start`

- **PT-002**: `NightStart_ToDawnCalculateVictims_IsValidTransition`
  - Given: Game in `Night.Start`, all actions complete
  - When: Night loop finishes
  - Then: Transitions to `Dawn.CalculateVictims`

- **PT-003**: `DawnFinalize_ToDayDebate_IsValidTransition`
  - Given: Game in `Dawn.Finalize`
  - When: Dawn hooks complete
  - Then: Transitions to `Day.Debate`

- **PT-004**: `DayFinalize_ToNightStart_IsValidTransition`
  - Given: Game in `Day.Finalize`, no victory condition met
  - When: Day completes
  - Then: Transitions to `Night.Start`, `TurnNumber` increments

### 2.2 Phase Cache Behavior
- **PT-010**: `MainPhaseTransition_ClearsSubPhaseCache`
  - Given: Game in `Dawn.AnnounceVictims` with cached sub-phase data
  - When: Transition to `Day` phase
  - Then: Sub-phase cache is cleared

- **PT-011**: `SubPhaseTransition_ClearsStageData`
  - Given: Game in `Day.NormalVoting` with stage data
  - When: Transition to `Day.ProcessVoteOutcome`
  - Then: Previous stage data is cleared

---

## 3. Night Action Tests

Tests for night phase role actions and logging.

### 3.1 Werewolf Actions
- **NA-001**: `Werewolves_WakeAndSelectVictim_LogsNightAction`
  - Given: Night phase, werewolf(s) active
  - When: Werewolves select a victim (Villager)
  - Then: `NightActionLogEntry` with `WerewolfVictimSelection` created

- **NA-002**: `Werewolves_CannotSelectWerewolf_AsVictim`
  - Given: Night phase, multiple werewolves
  - When: Instruction for victim selection is generated
  - Then: Werewolves are excluded from selectable targets

- **NA-003**: `Werewolves_CompleteFlow_WakeSleepCycle`
  - Given: Night phase starting
  - When: Werewolf flow executes
  - Then: States transition: AwaitingAwake → AwaitingTarget → AwaitingSleep → Asleep

### 3.2 Seer Actions
- **NA-010**: `Seer_ChecksWerewolf_ReceivesFeedbackAndLogsAction`
  - Given: Night phase, Seer awake, known werewolf player
  - When: Seer selects the werewolf
  - Then: 
    - Moderator receives feedback instruction indicating target "wakes with werewolves" (thumbs up)
    - `NightActionLogEntry` with `SeerCheck` is created

- **NA-011**: `Seer_ChecksVillager_ReceivesFeedbackAndLogsAction`
  - Given: Night phase, Seer awake, known villager player
  - When: Seer selects the villager
  - Then:
    - Moderator receives feedback instruction indicating target does NOT "wake with werewolves" (thumbs down)
    - `NightActionLogEntry` with `SeerCheck` is created

- **NA-012**: `Seer_ActionLogged_WithCorrectDetails`
  - Given: Seer performs check
  - When: Action completes
  - Then: `NightActionLogEntry` with `SeerCheck` and target recorded, correct `TurnNumber` and `CurrentPhase`

- **NA-013**: `Seer_CannotCheckSelf`
  - Given: Night phase, Seer awake
  - When: Target selection instruction is generated
  - Then: Seer's own ID is excluded from selectable targets

- **NA-014**: `Seer_CannotCheckDeadPlayers`
  - Given: Night 2, one player is dead from Night 1
  - When: Seer target selection instruction is generated
  - Then: Dead player's ID is excluded from selectable targets

### 3.3 Edge Cases
- **NA-020**: `Seer_TargetedNight1_StillActsBeforeDawn`
  - Given: Werewolves select Seer as victim on Night 1
  - When: Seer's turn in the night action order
  - Then: Seer still performs their investigation (death resolves at Dawn)

- **NA-021**: `Seer_KilledNight1_CannotActNight2`
  - Given: Seer was killed Night 1 (eliminated at Dawn 1)
  - When: Night 2 begins
  - Then: Seer is skipped in the night action loop (Health = Dead)

- **NA-022**: `FirstNight_RoleIdentification_RecordsRoleCorrectly`
  - Given: First night, Seer wakes
  - When: Seer identified by moderator input
  - Then: Player's `MainRole` is set to `Seer`

- **NA-023**: `Werewolves_CannotTargetDeadPlayers`
  - Given: Night 2, one player is dead from Night 1
  - When: Werewolf victim selection instruction is generated
  - Then: Dead player's ID is excluded from selectable targets

---

## 4. Dawn Resolution Tests

Tests for victim calculation and elimination processing.

### 4.1 Victim Calculation
- **DR-001**: `WerewolfVictim_Unprotected_IsEliminated`
  - Given: Werewolves selected a villager victim
  - When: Dawn resolution occurs
  - Then: Victim is eliminated with `EliminationReason.WerewolfAttack`

- **DR-002**: `NoWerewolfVictim_NoElimination_Impossible`
  - Given: Valid victims exist (i.e. villagers)
  - When: Werewolves wake up
  - Then: Not possible to have no victim selected

### 4.2 Role Reveal Flow
- **DR-010**: `VictimEliminated_RoleRevealRequested`
  - Given: Player eliminated at dawn
  - When: Elimination processed
  - Then: Instruction requests role assignment for the victim

- **DR-011**: `VictimRole_Revealed_CreatesAssignRoleLogEntry`
  - Given: Moderator provides victim's role
  - When: Role assignment processed
  - Then: `AssignRoleLogEntry` created with correct player and role

- **DR-012**: `VictimHealthStatus_SetToDead`
  - Given: Player eliminated and role revealed
  - When: State checked
  - Then: Player's `Health` is `Dead`

---

## 5. Day Voting Tests

Tests for the voting phase and elimination outcomes.

### 5.1 Normal Vote Flow
- **DV-001**: `DebatePhase_TransitionsToVoting`
  - Given: Day phase, `Debate` sub-phase
  - When: Debate confirmation received
  - Then: Transitions to `DetermineVoteType` then `NormalVoting`

- **DV-002**: `VoteOutcome_SinglePlayer_RequestsRoleReveal`
  - Given: Voting sub-phase
  - When: Moderator reports single player received most votes
  - Then: Transitions to request role assignment for eliminated player

- **DV-003**: `VoteElimination_CreatesVoteOutcomeLogEntry`
  - Given: Player eliminated by vote
  - When: Vote processed
  - Then: `VoteOutcomeReportedLogEntry` created

- **DV-004**: `VoteElimination_PlayerHealthSetToDead`
  - Given: Player eliminated by vote
  - When: Elimination processed
  - Then: Player's `Health` is `Dead`

### 5.2 Tie Votes
- **DV-010**: `TieVote_NoPlayerSelected_NoElimination`
  - Given: Voting sub-phase
  - When: Moderator reports tie (empty player selection)
  - Then: No player eliminated, day proceeds normally

- **DV-011**: `TieVote_LogsCorrectOutcome`
  - Given: Tie vote occurred
  - When: Vote processed
  - Then: `VoteOutcomeReportedLogEntry` indicates tie

### 5.3 Vote Target Validation
- **DV-020**: `Vote_CannotSelectDeadPlayer`
  - Given: Day phase after dawn elimination
  - When: Vote outcome instruction is generated
  - Then: Dead player's ID is excluded from selectable targets

---

## 6. Victory Condition Tests

Tests for both victory paths.

### 6.1 Villager Victory

- **VC-001**: `WerewolfEliminated_AtDawn_VillagerVictory`
  - Given: Last werewolf killed by special ability at dawn (future: Knight's sword, witch's poison, etc.)
  - When: Dawn resolution completes
  - Then: Victory detected, `WinningTeam` is `Villagers`

- **VC-002**: `WerewolfEliminated_AtDay_VillagerVictory`
  - Given: Last werewolf voted out during day
  - When: Vote elimination processed
  - Then: Victory detected, `WinningTeam` is `Villagers`

### 6.2 Werewolf Victory
- **VC-010**: `WerewolvesEqualVillagers_WerewolvesWin`
  - Given: 1 werewolf, 1 villager remaining
  - When: Victory check performed
  - Then: `WinningTeam` is `Werewolves`

- **VC-011**: `WerewolvesOutnumberVillagers_WerewolvesWin`
  - Given: 2 werewolves, 1 villager remaining
  - When: Victory check performed
  - Then: `WinningTeam` is `Werewolves`

- **VC-012**: `VillagerKilled_AtDawn_WerewolfVictory`
  - Given: 1 werewolf, 2 villagers; werewolf kills 1 villager
  - When: Dawn resolution completes (now 1 WW, 1 Villager)
  - Then: Victory detected, `WinningTeam` is `Werewolves`

- **VC-013**: `VillagerKilled_AtDay_WerewolfVictory`
  - Given: 1 werewolf, 3 villagers; werewolf kills 1 villager during the night; a villager is voted for lynching
  - When: Day resolution completes (now 1 WW, 1 Villager)
  - Then: Victory detected, `WinningTeam` is `Werewolves

### 6.3 Victory Timing
- **VC-020**: `VictoryCondition_CheckedAtDawn`
  - Given: Victory condition met after night kills
  - When: Dawn phase completes victim processing
  - Then: Victory detected before Day phase starts

- **VC-021**: `VictoryCondition_CheckedAfterVote`
  - Given: Victory condition met after day vote
  - When: Vote elimination processed
  - Then: Victory detected, game ends

- **VC-022**: `NoVictoryCondition_GameContinues`
  - Given: Multiple werewolves and villagers alive
  - When: Victory check performed
  - Then: Game continues to next phase

---

## 7. Input Validation Tests

Tests for handling invalid or unexpected moderator inputs. These tests verify that the game gracefully rejects bad inputs rather than entering invalid states.

### 7.1 Response Type Validation
- **IV-001**: `ProcessInstruction_WrongResponseType_ReturnsFailure`
  - Given: Pending `SelectPlayersInstruction` (e.g., werewolf victim selection)
  - When: Moderator provides a `ConfirmationResponse` instead of `SelectPlayersResponse`
  - Then: `ProcessResult.IsSuccess` is `false`, game state unchanged

- **IV-002**: `ProcessInstruction_NullResponse_ReturnsFailure`
  - Given: Any pending instruction
  - When: Moderator provides `null` response
  - Then: `ProcessResult.IsSuccess` is `false`, game state unchanged

### 7.2 Player Selection Validation
- **IV-010**: `SelectPlayers_EmptySelection_WhenSingleRequired_ReturnsFailure`
  - Given: `SelectPlayersInstruction` with `NumberRangeConstraint.Single`
  - When: Moderator provides empty player selection
  - Then: `ProcessResult.IsSuccess` is `false`

- **IV-011**: `SelectPlayers_TooManyPlayers_ReturnsFailure`
  - Given: `SelectPlayersInstruction` with `NumberRangeConstraint.Single`
  - When: Moderator provides two player IDs
  - Then: `ProcessResult.IsSuccess` is `false`

- **IV-012**: `SelectPlayers_InvalidPlayerId_ReturnsFailure`
  - Given: `SelectPlayersInstruction` with valid selectable player list
  - When: Moderator provides a GUID not in the selectable list
  - Then: `ProcessResult.IsSuccess` is `false`

- **IV-013**: `SelectPlayers_NonSelectablePlayer_ReturnsFailure`
  - Given: `SelectPlayersInstruction` excluding werewolves from selectable targets
  - When: Moderator provides a werewolf player ID
  - Then: `ProcessResult.IsSuccess` is `false`

### 7.3 Role Assignment Validation
- **IV-020**: `AssignRole_InvalidRole_ReturnsFailure`
  - Given: `AssignRolesInstruction` requesting role for eliminated player
  - When: Moderator provides a role not in `RolesInPlay`
  - Then: `ProcessResult.IsSuccess` is `false`

- **IV-021**: `AssignRole_WrongPlayer_ReturnsFailure`
  - Given: `AssignRolesInstruction` for specific player (e.g., eliminated victim)
  - When: Moderator provides a different player's ID
  - Then: `ProcessResult.IsSuccess` is `false`

---

## 8. Event Sourcing Tests

Tests for log integrity and state reconstruction.

### 7.1 Log Integrity
- **ES-001**: `AllActions_CreateLogEntries`
  - Given: Complete game cycle (Night → Dawn → Day)
  - When: Game history queried
  - Then: Log contains entries for all major actions

- **ES-002**: `LogEntries_ContainCorrectTurnNumber`
  - Given: Multi-turn game
  - When: Log entries examined
  - Then: Each entry has correct `TurnNumber` for when it occurred

- **ES-003**: `LogEntries_ContainCorrectPhase`
  - Given: Actions in different phases
  - When: Log entries examined
  - Then: Each entry has correct `CurrentPhase`

### 7.2 State Reconstruction
- **ES-010**: `ReplayLog_ReconstructsPlayerHealth`
  - Given: Game log with eliminations
  - When: State reconstructed from log
  - Then: Eliminated players have `Health = Dead`

- **ES-011**: `ReplayLog_ReconstructsKnownRoles`
  - Given: Game log with role assignments
  - When: State reconstructed from log
  - Then: Players have correct `MainRole` values

- **ES-012**: `DerivedState_MatchesCachedState`
  - Given: Active game session
  - When: Compare cached state vs. derived-from-log state
  - Then: States are equivalent

---

## 9. Extension Method Tests

Tests for extension methods and helper utilities.

### 9.1 MainRoleTypeExtensions
- **EX-001**: `GetRoleGroup_Werewolves_ReturnsWerewolvesGroup`
  - Given: Werewolf roles (SimpleWerewolf, BigBadWolf, AccursedWolfFather, WhiteWerewolf)
  - When: `GetRoleGroup()` is called
  - Then: Returns `RoleGroup.Werewolves`

- **EX-002**: `GetRoleGroup_Villagers_ReturnsVillagersGroup`
  - Given: Villager roles (Seer, Witch, Hunter, Defender, etc.)
  - When: `GetRoleGroup()` is called
  - Then: Returns `RoleGroup.Villagers`

- **EX-003**: `GetRoleGroup_Ambiguous_ReturnsAmbiguousGroup`
  - Given: Ambiguous roles (Thief, DevotedServant, Actor, WildChild, WolfHound)
  - When: `GetRoleGroup()` is called
  - Then: Returns `RoleGroup.Ambiguous`

- **EX-004**: `GetRoleGroup_Loners_ReturnsLonersGroup`
  - Given: Loner roles (Angel, Piper, PrejudicedManipulator)
  - When: `GetRoleGroup()` is called
  - Then: Returns `RoleGroup.Loners`

- **EX-005**: `GetRoleGroup_NewMoon_ReturnsNewMoonGroup`
  - Given: New Moon roles (Gypsy)
  - When: `GetRoleGroup()` is called
  - Then: Returns `RoleGroup.NewMoon`

- **EX-006**: `GetRoleGroup_AllRoles_ReturnValidGroup`
  - Given: All defined `MainRoleType` values
  - When: `GetRoleGroup()` is called for each
  - Then: No exceptions thrown, each returns a valid `RoleGroup`

---

## 10. Status Effects API Tests

Tests for the unified status effects system.

### 10.1 Status Effect Querying
- **SE-001**: `GetActiveStatusEffects_NoEffects_ReturnsEmptyList`
  - Given: Player with no status effects applied
  - When: `player.State.GetActiveStatusEffects()` is called
  - Then: Returns empty list

- **SE-002**: `GetActiveStatusEffects_SingleEffect_ReturnsSingleItemList`
  - Given: Player with `LycanthropyInfection` applied
  - When: `player.State.GetActiveStatusEffects()` is called
  - Then: Returns list containing only `StatusEffectTypes.LycanthropyInfection`

- **SE-003**: `HasStatusEffect_None_ReturnsTrueWhenNoEffects`
  - Given: Player with no status effects applied
  - When: `player.State.HasStatusEffect(StatusEffectTypes.None)` is called
  - Then: Returns `true` (semantic: "does this player have zero effects?" → Yes)

- **SE-003b**: `HasStatusEffect_None_ReturnsFalseWhenHasEffects`
  - Given: Player with `Sheriff` effect applied
  - When: `player.State.HasStatusEffect(StatusEffectTypes.None)` is called
  - Then: Returns `false` (player has effects, so "has none" is false)

- **SE-004**: `GetActiveStatusEffects_ExcludesNone`
  - Given: Player with `Sheriff` and `Charmed` applied
  - When: `player.State.GetActiveStatusEffects()` is called
  - Then: Returns list containing `Sheriff` and `Charmed`, excluding `None`

- **SE-005**: `HasStatusEffect_EffectNotPresent_ReturnsFalse`
  - Given: Player with no status effects
  - When: `player.State.HasStatusEffect(StatusEffectTypes.Sheriff)` is called
  - Then: Returns `false`

- **SE-006**: `HasStatusEffect_EffectPresent_ReturnsTrue`
  - Given: Player with `Sheriff` effect applied
  - When: `player.State.HasStatusEffect(StatusEffectTypes.Sheriff)` is called
  - Then: Returns `true`

### 10.2 Status Effect Mutations
- **SE-020**: `ApplyStatusEffect_ElderProtectionLost_SetsEffect`
  - Given: Elder player
  - When: `session.ApplyStatusEffect(StatusEffectTypes.ElderProtectionLost, playerId)` is called
  - Then: `player.State.HasStatusEffect(StatusEffectTypes.ElderProtectionLost)` returns `true`

- **SE-021**: `ApplyStatusEffect_LycanthropyInfection_SetsEffect`
  - Given: Player targeted by Accursed Wolf-Father
  - When: `session.ApplyStatusEffect(StatusEffectTypes.LycanthropyInfection, playerId)` is called
  - Then: `player.State.HasStatusEffect(StatusEffectTypes.LycanthropyInfection)` returns `true`

- **SE-022**: `ApplyStatusEffect_WildChildChanged_SetsEffectAndChangesRole`
  - Given: Wild Child player whose model was eliminated
  - When: `session.ApplyStatusEffect(StatusEffectTypes.WildChildChanged, playerId)` is called
  - Then: Player has `WildChildChanged` effect AND `MainRole` is `SimpleWerewolf`

- **SE-023**: `ApplyStatusEffect_LynchingImmunityUsed_SetsEffect`
  - Given: Village Idiot player
  - When: `session.ApplyStatusEffect(StatusEffectTypes.LynchingImmunityUsed, playerId)` is called
  - Then: `player.State.HasStatusEffect(StatusEffectTypes.LynchingImmunityUsed)` returns `true`
  - And: `player.State.IsImmuneToLynching` returns `false` (immunity used up)

### 10.3 Status Effect Extension Methods
- **SE-030**: `WithStatusEffect_FiltersPlayersCorrectly`
  - Given: 3 players, 1 with `Sheriff` effect
  - When: `players.WithStatusEffect(StatusEffectTypes.Sheriff)` is called
  - Then: Returns only the Sheriff player

- **SE-031**: `WithoutStatusEffect_FiltersPlayersCorrectly`
  - Given: 3 players, 1 with `LycanthropyInfection` effect
  - When: `players.WithoutStatusEffect(StatusEffectTypes.LycanthropyInfection)` is called
  - Then: Returns only the 2 non-infected players

---

## 11. Serialization Tests

Tests for session serialization and deserialization.

### 11.1 Round-Trip Serialization
- **SZ-001**: `Serialize_NewGame_ProducesValidJson`
  - Given: Newly created game session
  - When: `session.Serialize()` is called
  - Then: Returns valid JSON string containing session ID, players, and initial state

- **SZ-002**: `Deserialize_ValidJson_RestoresSessionId`
  - Given: Serialized game session JSON
  - When: `GameSessionKernel.Deserialize(json)` is called
  - Then: Restored session has same `Id` as original

- **SZ-003**: `RoundTrip_PreservesPlayerData`
  - Given: Game with 5 players, roles assigned
  - When: Session is serialized then deserialized
  - Then: All player IDs, names, roles, and health are preserved

- **SZ-004**: `RoundTrip_PreservesStatusEffects`
  - Given: Game where player has `LycanthropyInfection` and `Sheriff` effects
  - When: Session is serialized then deserialized
  - Then: Player's `ActiveEffects` flags are preserved

- **SZ-005**: `RoundTrip_PreservesSeatingOrder`
  - Given: Game with 5 players in specific seating order
  - When: Session is serialized then deserialized
  - Then: Seating order is identical

### 11.2 Polymorphic Type Serialization
- **SZ-010**: `Serialize_GameHistoryLog_PreservesAllEntryTypes`
  - Given: Game with `AssignRoleLogEntry`, `NightActionLogEntry`, `PhaseTransitionLogEntry` in log
  - When: Session is serialized then deserialized
  - Then: All log entries are correctly typed and data preserved

- **SZ-011**: `Serialize_ModeratorInstruction_PreservesPolymorphicType`
  - Given: Game with `SelectPlayersInstruction` as pending instruction
  - When: Session is serialized then deserialized
  - Then: Pending instruction is correctly typed with all properties preserved

- **SZ-012**: `Serialize_StatusEffectLogEntry_PreservesEffectType`
  - Given: Game log containing `StatusEffectLogEntry` with `ElderProtectionLost`
  - When: Session is serialized then deserialized
  - Then: Log entry has correct `EffectType` and `PlayerId`

### 11.3 Phase State Serialization
- **SZ-020**: `RoundTrip_PreservesCurrentPhase`
  - Given: Game in `Day` phase
  - When: Session is serialized then deserialized
  - Then: Current phase is `Day`

- **SZ-021**: `RoundTrip_PreservesSubPhase`
  - Given: Game in `Day` phase, `Debate` sub-phase
  - When: Session is serialized then deserialized
  - Then: Sub-phase is correctly restored

- **SZ-022**: `RoundTrip_PreservesTurnNumber`
  - Given: Game on turn 3
  - When: Session is serialized then deserialized
  - Then: Turn number is 3

### 11.4 Integration Serialization
- **SZ-030**: `Serialize_MidGame_CanContinueAfterDeserialization`
  - Given: Game in progress (Night phase, some actions completed)
  - When: Serialized, deserialized, then ProcessInstruction called
  - Then: Game continues normally from saved state

- **SZ-031**: `RehydrateSession_AddsToActiveSessions`
  - Given: Serialized game session
  - When: `GameService.RehydrateSession(json)` is called
  - Then: Session is accessible via `GameService.GetGameStateView(id)`

---

## Test Helpers

### GameTestBuilder
Fluent builder for creating test scenarios:
```csharp
var game = GameTestBuilder.Create()
    .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true)
    .StartGame();
```

### Night Phase Helpers

#### NightActionInputs
Data class for providing role-specific inputs to `CompleteNightPhase`. The test helper iterates through `GameFlowManager.HookListeners[NightMainActionLoop]` in order, ensuring tests follow the actual game flow:
```csharp
var inputs = new NightActionInputs
{
    WerewolfIds = [werewolfId],
    WerewolfVictimId = victimId,
    SeerId = seerId,           // Optional
    SeerTargetId = seerTargetId // Required if SeerId is provided
};
builder.CompleteNightPhase(inputs);

// Or use the convenience overload:
builder.CompleteNightPhase(werewolfIds, victimId, seerId, seerTargetId);
```

#### Subsequent Night Helper
For Night 2 and beyond, werewolves are already identified (identification happens once on Night 1). Use `CompleteWerewolfNightActionSubsequentNight()` to skip the identification step:

```csharp
// Night 1: Full flow with identification
builder.CompleteWerewolfNightAction([werewolfId], victimId);

// Night 2+: Shortened flow (no identification needed)
builder.CompleteWerewolfNightActionSubsequentNight(victimId);
```

**When to use each method:**
| Method | Use Case | Flow |
|--------|----------|------|
| `CompleteWerewolfNightAction(werewolfIds, victimId)` | Night 1 | Identify → Wake → Select Victim → Sleep |
| `CompleteWerewolfNightActionSubsequentNight(victimId)` | Night 2+ | Wake → Select Victim → Sleep |

The difference is that on Night 1, the moderator must identify which players are werewolves (so they can wake together). On subsequent nights, the werewolves are already known, so only the wakeup confirmation is needed.

### ResponseFactory
Factory methods for creating `ModeratorResponse` instances:
```csharp
ResponseFactory.Confirm(true);
ResponseFactory.SelectPlayer(playerId);
ResponseFactory.AssignRole(playerId, MainRoleType.Seer);
```

---

## Future Expansion

When adding support for additional roles, add corresponding test sections:
- **Witch Tests**: Save/kill potion interactions
- **Hunter Tests**: Shot on death trigger
- **Defender Tests**: Protection mechanics
- **Elder Tests**: Multi-life handling
- **Sheriff Tests**: Double vote, succession

---

## Conventions

- Test method names follow: `MethodUnderTest_Scenario_ExpectedResult`
- Test IDs use prefixes: GL (Lifecycle), PT (Transitions), NA (Night), DR (Dawn), DV (Day), VC (Victory), IV (Input Validation), ES (Event Sourcing)
- Each test should be independent and not rely on other tests' execution order
- Use `GameTestBuilder` for consistent setup across tests

### DiagnosticTestBase Pattern
- All integration tests should extend `DiagnosticTestBase` for automatic state change logging on failure
- Call `MarkTestCompleted()` at the end of each successful test to suppress diagnostic dump
- Use `CreateBuilder()` to get a builder with diagnostics wired up
- On test failure, the complete state change timeline is automatically dumped to test output

### Victory Timing
- Victory is checked at **phase transition boundaries** (entering Day or Night), not at sub-phases
- `VictoryConditionMetLogEntry.CurrentPhase` reflects the *destination* phase of the transition
- Dawn victory → logged as `GamePhase.Day`; Day victory → logged as `GamePhase.Night`

### Role Knowledge and Instructions
- Roles become "known" when a player performs a night action (werewolves wake, seer investigates, etc.)
- When a player with a **known role** is eliminated (dawn or vote), expect `ConfirmationInstruction` (death announcement)
- When a player with an **unknown role** is eliminated, expect `AssignRolesInstruction` (role reveal request)
- Tests must account for which roles wake during night when expecting instruction types

### Player Count Considerations
- When testing specific victory timing (e.g., victory at day vote vs. dawn), ensure player counts don't trigger earlier victories
- Example: Testing villager victory via day vote requires enough villagers to survive dawn without triggering werewolf victory
- `WithSimpleGame(playerCount, werewolfCount, includeSeer)` assigns roles in order: werewolves first, then seer (if included), then villagers

### Instruction Result Handling
- When victory is detected mid-flow, the `FinishedGameConfirmationInstruction` may be in `ProcessResult.ModeratorInstruction`
- Use `result.ModeratorInstruction` rather than `builder.GetCurrentInstruction()` when checking victory after processing

### Night Action Order
- Roles wake in a defined order during night, determined by `GameFlowManager.HookListeners[NightMainActionLoop]`
- `CompleteNightPhase` iterates through this order dynamically, ensuring tests stay in sync with game logic
- **Night 1 vs Night 2+**: On Night 1, werewolves need to be *identified* first (moderator points them out). On subsequent nights, identification is skipped since werewolves are already known.
  - Use `CompleteWerewolfNightAction()` for Night 1 (includes identification step)
  - Use `CompleteWerewolfNightActionSubsequentNight()` for Night 2+ (skips identification)
- Deaths from night actions resolve at **Dawn**, not during night (targeted player still acts)
- When adding a new role to `ListenerFactories`, also add its handler to `CompleteNightPhase`'s switch and extend `NightActionInputs`

### Log Verification Patterns
- Use LINQ with `OfType<TLogEntry>()` to filter the game history log
- Filter by `TurnNumber` and `CurrentPhase` for precise log entry matching
- Common pattern: `session.GameHistoryLog.OfType<NightActionLogEntry>().Where(e => e.ActionType == ...)`
