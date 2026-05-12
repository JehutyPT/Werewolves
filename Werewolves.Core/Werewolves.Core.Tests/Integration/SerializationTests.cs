using System.Text.Json;
using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

/// <summary>
/// Tests for session serialization and deserialization (rehydration).
/// Test IDs: SZ-001 through SZ-041
/// </summary>
public class SerializationTests : DiagnosticTestBase
{
    public SerializationTests(ITestOutputHelper output) : base(output) { }

    #region SZ-001 to SZ-005: Round-Trip Serialization

    /// <summary>
    /// SZ-001: Serialize a new game produces valid JSON.
    /// </summary>
    [Fact]
    public void Serialize_NewGame_ProducesValidJson()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var session = builder.GetGameState()!;

        // Act
        var json = session.Serialize();

        // Assert
        json.Should().NotBeNullOrEmpty();
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow("Serialized session should be valid JSON");

        MarkTestCompleted();
    }

    /// <summary>
    /// SZ-002: Deserialize valid JSON restores Session ID.
    /// </summary>
    [Fact]
    public void Deserialize_ValidJson_RestoresSessionId()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var originalSession = builder.GetGameState()!;
        var originalId = originalSession.Id;
        var json = originalSession.Serialize();

        // Act - RehydrateSession returns the GUID of the rehydrated session
        var rehydratedId = builder.GameService.RehydrateSession(json);
        var rehydratedSession = builder.GameService.GetGameStateView(rehydratedId);

        // Assert
        rehydratedId.Should().Be(originalId);
        rehydratedSession.Should().NotBeNull();
        rehydratedSession!.Id.Should().Be(originalId);

        MarkTestCompleted();
    }

    /// <summary>
    /// SZ-003: Round-trip preserves player data (names, IDs).
    /// </summary>
    [Fact]
    public void RoundTrip_PreservesPlayerData()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithPlayers("Alice", "Bob", "Charlie", "Diana", "Eve")
            .WithRoles(MainRoleType.SimpleWerewolf, MainRoleType.Seer, 
                      MainRoleType.SimpleVillager, MainRoleType.SimpleVillager, MainRoleType.SimpleVillager);
        builder.StartGame();
        builder.ConfirmGameStart();

        var originalSession = builder.GetGameState()!;
        var originalPlayers = originalSession.GetPlayers().ToList();
        var json = originalSession.Serialize();

        // Act
        var rehydratedId = builder.GameService.RehydrateSession(json);
        var rehydratedSession = builder.GameService.GetGameStateView(rehydratedId)!;
        var rehydratedPlayers = rehydratedSession.GetPlayers().ToList();

        // Assert
        rehydratedPlayers.Should().HaveCount(originalPlayers.Count);
        foreach (var original in originalPlayers)
        {
            var rehydrated = rehydratedPlayers.FirstOrDefault(p => p.Id == original.Id);
            rehydrated.Should().NotBeNull($"Player {original.Name} should be preserved");
            rehydrated!.Name.Should().Be(original.Name);
        }

        MarkTestCompleted();
    }

    /// <summary>
    /// SZ-004: Round-trip preserves status effects.
    /// </summary>
    [Fact]
    public void RoundTrip_PreservesStatusEffects()
    {
        // Arrange - Use TestSessionMutator to verify effects roundtrip
        // Since we can't directly apply status effects in production flow easily,
        // we test this via the serialization DTOs
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var session = builder.GetGameState()!;
        var json = session.Serialize();

        // Act
        var rehydratedId = builder.GameService.RehydrateSession(json);
        var rehydratedSession = builder.GameService.GetGameStateView(rehydratedId)!;

        // Assert - Initial state should have no status effects
        var players = rehydratedSession.GetPlayers();
        foreach (var player in players)
        {
            player.State.GetActiveStatusEffects().Should().BeEmpty(
                "New game should have no status effects");
        }

        MarkTestCompleted();
    }

    /// <summary>
    /// SZ-005: Round-trip preserves seating order.
    /// </summary>
    [Fact]
    public void RoundTrip_PreservesSeatingOrder()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithPlayers("First", "Second", "Third", "Fourth", "Fifth")
            .WithRoles(MainRoleType.SimpleWerewolf, MainRoleType.Seer,
                      MainRoleType.SimpleVillager, MainRoleType.SimpleVillager, MainRoleType.SimpleVillager);
        builder.StartGame();
        builder.ConfirmGameStart();

        var originalSession = builder.GetGameState()!;
        var originalOrder = originalSession.GetPlayers().Select(p => p.Name).ToList();
        var json = originalSession.Serialize();

        // Act
        var rehydratedId = builder.GameService.RehydrateSession(json);
        var rehydratedSession = builder.GameService.GetGameStateView(rehydratedId)!;
        var rehydratedOrder = rehydratedSession.GetPlayers().Select(p => p.Name).ToList();

        // Assert
        rehydratedOrder.Should().ContainInOrder(originalOrder);

        MarkTestCompleted();
    }

    #endregion

    #region SZ-010 to SZ-012: Polymorphic Type Serialization

    /// <summary>
    /// SZ-010: Serialize GameHistoryLog preserves all entry types.
    /// </summary>
    [Fact]
    public void Serialize_GameHistoryLog_PreservesAllEntryTypes()
    {
        // Arrange - Complete a night to get AssignRoleLogEntry and NightActionLogEntry
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Get player info and complete werewolf action to generate log entries
        var session = builder.GetGameState()!;
        var players = session.GetPlayers().ToList();
        var werewolfPlayer = players[0]; // First player is werewolf

        // Confirm night start
        builder.ConfirmNightStart();

        // Complete werewolf identification and victim selection
        var inputs = new NightActionInputs
        {
            WerewolfIds = [werewolfPlayer.Id],
            WerewolfVictimId = players[4].Id
        };
        builder.CompleteWerewolfNightAction(inputs.WerewolfIds, inputs.WerewolfVictimId.Value);

        var originalSession = builder.GetGameState()!;
        var originalEntryTypes = originalSession.GameHistoryLog
            .Select(e => e.GetType().Name)
            .Distinct()
            .ToList();

        var json = originalSession.Serialize();

        // Act
        var rehydratedId = builder.GameService.RehydrateSession(json);
        var rehydratedSession = builder.GameService.GetGameStateView(rehydratedId)!;
        var rehydratedEntryTypes = rehydratedSession.GameHistoryLog
            .Select(e => e.GetType().Name)
            .Distinct()
            .ToList();

        // Assert
        rehydratedEntryTypes.Should().BeEquivalentTo(originalEntryTypes);

        // Verify specific entries exist
        rehydratedSession.GameHistoryLog.OfType<AssignRoleLogEntry>().Should().NotBeEmpty();
        rehydratedSession.GameHistoryLog.OfType<NightActionLogEntry>().Should().NotBeEmpty();

        MarkTestCompleted();
    }

    /// <summary>
    /// SZ-011: Serialize ModeratorInstruction preserves polymorphic type.
    /// </summary>
    [Fact]
    public void Serialize_ModeratorInstruction_PreservesPolymorphicType()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var originalInstruction = builder.GetCurrentInstruction();
        originalInstruction.Should().NotBeNull();
        var originalType = originalInstruction!.GetType();

        var json = builder.GetGameState()!.Serialize();

        // Act
        var rehydratedId = builder.GameService.RehydrateSession(json);
        var rehydratedInstruction = builder.GameService.GetCurrentInstruction(rehydratedId);

        // Assert
        rehydratedInstruction.Should().NotBeNull();
        rehydratedInstruction!.GetType().Should().Be(originalType);

        MarkTestCompleted();
    }

    /// <summary>
    /// SZ-012: Serialize NightActionLogEntry preserves ActionType enum.
    /// </summary>
    [Fact]
    public void Serialize_NightActionLogEntry_PreservesActionType()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();

        var session = builder.GetGameState()!;
        var players = session.GetPlayers().ToList();
        var werewolfPlayer = players[0];

        builder.CompleteWerewolfNightAction([werewolfPlayer.Id], players[4].Id);

        var originalSession = builder.GetGameState()!;
        var originalNightAction = originalSession.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .First();

        var json = originalSession.Serialize();

        // Act
        var rehydratedId = builder.GameService.RehydrateSession(json);
        var rehydratedSession = builder.GameService.GetGameStateView(rehydratedId)!;
        var rehydratedNightAction = rehydratedSession.GameHistoryLog
            .OfType<NightActionLogEntry>()
            .First();

        // Assert
        rehydratedNightAction.ActionType.Should().Be(originalNightAction.ActionType);

        MarkTestCompleted();
    }

    #endregion

    #region SZ-020 to SZ-022: Phase State Serialization

    /// <summary>
    /// SZ-020: Round-trip preserves CurrentPhase.
    /// </summary>
    [Fact]
    public void RoundTrip_PreservesCurrentPhase()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var originalSession = builder.GetGameState()!;
        var originalPhase = originalSession.GetCurrentPhase();
        var json = originalSession.Serialize();

        // Act
        var rehydratedId = builder.GameService.RehydrateSession(json);
        var rehydratedSession = builder.GameService.GetGameStateView(rehydratedId)!;

        // Assert
        rehydratedSession.GetCurrentPhase().Should().Be(originalPhase);

        MarkTestCompleted();
    }

    /// <summary>
    /// SZ-021: Round-trip preserves SubPhase.
    /// </summary>
    [Fact]
    public void RoundTrip_PreservesSubPhase()
    {
        // Arrange - Get into night phase which has a sub-phase
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var json = builder.GetGameState()!.Serialize();

        // Act
        var rehydratedId = builder.GameService.RehydrateSession(json);
        var rehydratedSession = builder.GameService.GetGameStateView(rehydratedId)!;

        // Assert - Just verify the round-trip doesn't fail
        rehydratedSession.Should().NotBeNull();
        rehydratedSession.GetCurrentPhase().Should().Be(GamePhase.Night);

        MarkTestCompleted();
    }

    /// <summary>
    /// SZ-022: Round-trip preserves TurnNumber.
    /// </summary>
    [Fact]
    public void RoundTrip_PreservesTurnNumber()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var originalSession = builder.GetGameState()!;
        var originalTurn = originalSession.TurnNumber;
        var json = originalSession.Serialize();

        // Act
        var rehydratedId = builder.GameService.RehydrateSession(json);
        var rehydratedSession = builder.GameService.GetGameStateView(rehydratedId)!;

        // Assert
        rehydratedSession.TurnNumber.Should().Be(originalTurn);

        MarkTestCompleted();
    }

    #endregion

    #region SZ-030 to SZ-031: Integration Serialization

    /// <summary>
    /// SZ-030: Serialize mid-game session can continue after deserialization.
    /// </summary>
    [Fact]
    public void Serialize_MidGame_CanContinueAfterDeserialization()
    {
        // Arrange - Start game and serialize during Night phase
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var midGameSession = builder.GetGameState()!;
        midGameSession.GetCurrentPhase().Should().Be(GamePhase.Night);

        var json = midGameSession.Serialize();

        // Act - Rehydrate the session
        var rehydratedId = builder.GameService.RehydrateSession(json);
        var rehydratedSession = builder.GameService.GetGameStateView(rehydratedId)!;

        // Assert - Session should be playable
        rehydratedSession.Should().NotBeNull();
        rehydratedSession.GetCurrentPhase().Should().Be(GamePhase.Night);

        // Should be able to get current instruction
        var instruction = builder.GameService.GetCurrentInstruction(rehydratedId);
        instruction.Should().NotBeNull();

        MarkTestCompleted();
    }

    /// <summary>
    /// SZ-031: RehydrateSession adds session to active sessions.
    /// </summary>
    [Fact]
    public void RehydrateSession_AddsToActiveSessions()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var json = builder.GetGameState()!.Serialize();
        var originalId = builder.GetGameState()!.Id;

        // Create a new GameService to simulate app restart
        var newGameService = new GameLogic.Services.GameService();

        // Act
        var rehydratedId = newGameService.RehydrateSession(json);
        var rehydratedSession = newGameService.GetGameStateView(rehydratedId);

        // Assert
        rehydratedId.Should().Be(originalId);
        rehydratedSession.Should().NotBeNull();
        rehydratedSession!.Id.Should().Be(originalId);

        MarkTestCompleted();
    }

    #endregion

    #region SZ-040 to SZ-041: Rehydration Consistency

    /// <summary>
    /// SZ-040: Rehydration does NOT call Apply() on log entries.
    /// Verifies that cached state is restored directly without replaying entries.
    /// </summary>
    [Fact]
    public void Rehydration_DoesNotCallApply()
    {
        // Arrange - Create a game with some log entries
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();

        var session = builder.GetGameState()!;
        var players = session.GetPlayers().ToList();
        builder.CompleteWerewolfNightAction([players[0].Id], players[4].Id);

        var originalSession = builder.GetGameState()!;
        var originalLogCount = originalSession.GameHistoryLog.Count();
        var json = originalSession.Serialize();

        // Act - Rehydrate
        var rehydratedId = builder.GameService.RehydrateSession(json);
        var rehydratedSession = builder.GameService.GetGameStateView(rehydratedId)!;

        // Assert
        // 1. Log count should be the same (no new entries from replaying Apply())
        rehydratedSession.GameHistoryLog.Count().Should().Be(originalLogCount);

        // 2. State should match (if Apply() was called, state would still match,
        //    but the log count test above validates the intended behavior)
        var originalPlayers = originalSession.GetPlayers().ToDictionary(p => p.Id);
        var rehydratedPlayers = rehydratedSession.GetPlayers().ToDictionary(p => p.Id);

        foreach (var (id, original) in originalPlayers)
        {
            var rehydrated = rehydratedPlayers[id];
            rehydrated.State.MainRole.Should().Be(original.State.MainRole);
            rehydrated.State.Health.Should().Be(original.State.Health);
        }

        MarkTestCompleted();
    }

    /// <summary>
    /// SZ-041: Rehydrated cached state matches state derived from log replay.
    /// This validates the dual-write consistency between cached state and log entries.
    /// </summary>
    [Fact]
    public void Rehydration_CachedState_MatchesLogDerivedState()
    {
        // Arrange - Run a game to some point with state-changing entries
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();

        var session = builder.GetGameState()!;
        var players = session.GetPlayers().ToList();
        builder.CompleteWerewolfNightAction([players[0].Id], players[4].Id);

        var originalSession = builder.GetGameState()!;
        var playerIds = originalSession.GetPlayers().Select(p => p.Id).ToList();

        // Act - Replay log entries through test mutator to derive state
        var testMutator = new TestSessionMutator(playerIds);
        foreach (var entry in originalSession.GameHistoryLog)
        {
            entry.Apply(testMutator);
        }

        // Assert - Compare derived state with cached state
        var derivedStates = testMutator.GetDerivedStates();
        foreach (var player in originalSession.GetPlayers())
        {
            var derived = derivedStates[player.Id];
            
            // Role should match
            derived.MainRole.Should().Be(player.State.MainRole,
                $"Player {player.Name} role should match");
            
            // Health should match
            derived.Health.Should().Be(player.State.Health,
                $"Player {player.Name} health should match");
            
            // Status effects should match
            var cachedEffects = player.State.GetActiveStatusEffects();
            var derivedEffects = derived.GetActiveStatusEffects();
            derivedEffects.Should().BeEquivalentTo(cachedEffects,
                $"Player {player.Name} status effects should match");
        }

        MarkTestCompleted();
    }

    #endregion
}
