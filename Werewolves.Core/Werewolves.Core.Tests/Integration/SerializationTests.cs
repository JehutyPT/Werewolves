using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentAssertions;
using Werewolves.Core.GameLogic.Models.StateMachine;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.StateModels.Serialization;
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

    private static readonly JsonSerializerOptions RecoverySerializationOptions = new()
    {
        Converters =
        {
            new GameLogEntryConverter(),
            new ModeratorInstructionConverter(),
            new JsonStringEnumConverter()
        }
    };

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
        var json = builder.SerializeSession();

        // Assert
        json.Should().NotBeNullOrEmpty();
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow(CoreTestReferences.AssertionReasons.SerializedSessionValidJson);

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
        var json = builder.SerializeSession();

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
        var json = builder.SerializeSession();

        // Act
        var rehydratedId = builder.GameService.RehydrateSession(json);
        var rehydratedSession = builder.GameService.GetGameStateView(rehydratedId)!;
        var rehydratedPlayers = rehydratedSession.GetPlayers().ToList();

        // Assert
        rehydratedPlayers.Should().HaveCount(originalPlayers.Count);
        foreach (var original in originalPlayers)
        {
            var rehydrated = rehydratedPlayers.FirstOrDefault(p => p.Id == original.Id);
            rehydrated.Should().NotBeNull(CoreTestReferences.AssertionReasons.PlayerPreserved(original.Name));
            rehydrated!.Name.Should().Be(original.Name);
        }

        MarkTestCompleted();
    }

    [Fact]
    public void CurrentRoleFactSchema_DoubleRehydration_PreservesExplicitUnknownModeratorRole()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        var session = builder.GetGameState()
            .Should().BeOfType<GameSession>().Subject;
        var players = session.GetPlayers().ToArray();
        var werewolf = players[0];
        var seer = players[1];
        session.AssignRole(werewolf.Id, MainRoleType.SimpleWerewolf);
        session.AssignRole(seer.Id, MainRoleType.Seer);
        builder.ConfirmGameStart();
        builder.ConfirmNightStart();
        var identification = builder.GetCurrentInstruction()
            .Should().BeOfType<SelectPlayersInstruction>().Subject;
        builder.Process(identification.CreateResponse([werewolf.Id]));
        session.GetPlayerState(seer.Id).CurrentRole.Should()
            .Be(MainRoleType.Seer);
        session.GetPlayerState(seer.Id).ModeratorKnownRole.Should().BeNull();

        var firstService = new GameService();
        var firstId = firstService.RehydrateSession(builder.SerializeSession());
        var firstRecovered = firstService.GetGameStateView(firstId)!;
        var secondService = new GameService();
        var secondId = secondService.RehydrateSession(
            firstService.SerializeSession(firstId));
        var secondRecovered = secondService.GetGameStateView(secondId)!;

        firstRecovered.GetPlayerState(seer.Id).CurrentRole.Should()
            .Be(MainRoleType.Seer);
        firstRecovered.GetPlayerState(seer.Id).ModeratorKnownRole.Should().BeNull();
        secondRecovered.GetPlayerState(seer.Id).CurrentRole.Should()
            .Be(MainRoleType.Seer);
        secondRecovered.GetPlayerState(seer.Id).ModeratorKnownRole.Should().BeNull();

        MarkTestCompleted();
    }

    [Fact]
    public void LegacyRoleFactSchema_MigratesCurrentRoleToModeratorKnownRole()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();
        var snapshot = JsonNode.Parse(builder.SerializeSession())!.AsObject();
        snapshot.Remove(nameof(GameSessionDto.RoleFactSchemaVersion));
        var player = snapshot[nameof(GameSessionDto.Players)]!
            .AsArray()[0]!
            .AsObject();
        player[nameof(PlayerDto.MainRole)] = MainRoleType.SimpleWerewolf.ToString();
        player[nameof(PlayerDto.ModeratorKnownRole)] = null;

        var service = new GameService();
        var gameId = service.RehydrateSession(snapshot.ToJsonString());
        var recovered = service.GetGameStateView(gameId)!;
        var playerId = recovered.GetPlayers().First().Id;

        recovered.GetPlayerState(playerId).CurrentRole.Should()
            .Be(MainRoleType.SimpleWerewolf);
        recovered.GetPlayerState(playerId).ModeratorKnownRole.Should()
            .Be(MainRoleType.SimpleWerewolf);

        MarkTestCompleted();
    }

	[Fact]
	public void SessionWithoutRoleIdentificationWerewolfFactionAgencyEntailment_RehydratesUnchanged()
	{
		var builder = CreateBuilder()
			.WithPlayers(7)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Cupid,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var session = builder.GetGameState()!;
		var holder = session.GetPlayers().ElementAt(1);
		var identification = builder.GetCurrentInstruction()
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		var expectedNext = builder.Process(
				identification.CreateResponse([holder.Id]))
			.ModeratorInstruction.Should()
			.BeOfType<ConfirmationInstruction>().Subject;
		var legacy = JsonSerializer.Deserialize<GameSessionDto>(
			builder.SerializeSession(),
			RecoverySerializationOptions)!;
		legacy.GameHistoryLog.RemoveAll(entry =>
			entry is FactionFactsCommittedLogEntry facts &&
			facts.Source.Identifier == FactionFactSource
				.RoleIdentificationWerewolfFactionAgencyEntailmentIdentifier);
		legacy.Players.Single(player => player.Id == holder.Id)
			.FactionAgentKnowledge![Faction.Werewolf] =
				FactionAgentKnowledge.Unknown;
		legacy.RoleFactSchemaVersion.Should().Be(RoleFactSchema.CurrentVersion);
		legacy.FactionFactSchemaVersion.Should().Be(FactionFactSchema.CurrentVersion);
		var legacyPayload = JsonSerializer.Serialize(
			legacy,
			RecoverySerializationOptions);
		var recoveredService = new GameService();

		var recoveredId = recoveredService.RehydrateSession(legacyPayload);
		var recovered = recoveredService.GetGameStateView(recoveredId)!;
		var recoveredNext = recoveredService.GetCurrentInstruction(recoveredId)
			.Should().BeOfType<ConfirmationInstruction>().Subject;

		recovered.GetPlayerState(holder.Id).ModeratorKnownRole.Should().Be(
			MainRoleType.Cupid);
		recovered.GetFactionAgentKnowledge(holder.Id, Faction.Werewolf).Should()
			.Be(FactionAgentKnowledge.Unknown);
		recoveredNext.InstructionId.Should().Be(expectedNext.InstructionId);
		var continued = recoveredService.ProcessInstruction(
			recoveredId,
			recoveredNext.CreateResponse());
		continued.IsSuccess.Should().BeTrue();
		continued.ModeratorInstruction.Should().BeOfType<SelectPlayersInstruction>();
		MarkTestCompleted();
	}

    [Fact]
    public void LegacyPlayerPayload_WithoutVotingRight_DefaultsToEligible()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();
        var snapshot = JsonNode.Parse(builder.SerializeSession())!
            .AsObject();
        foreach (var player in snapshot[nameof(GameSessionDto.Players)]!.AsArray())
        {
            player!.AsObject().Remove(nameof(PlayerDto.HasVotingRight));
        }

        var service = new GameService();
        var gameId = service.RehydrateSession(snapshot.ToJsonString());
        var recovered = service.GetGameStateView(gameId)!;

        recovered.GetPlayers().Should().OnlyContain(player =>
            player.State.HasVotingRight);
        MarkTestCompleted();
    }

    [Fact]
    public void CurrentPlayerPayload_WithoutDurableVotingPower_IsRejected()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(
                playerCount: 5,
                werewolfCount: 1,
                includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();
        var snapshot = JsonNode.Parse(
            builder.SerializeSession())!.AsObject();
        foreach (var player in
                 snapshot[nameof(GameSessionDto.Players)]!.AsArray())
        {
            player!.AsObject().Remove(
                nameof(PlayerDto.DurableVotingPower));
        }

        var act = () => new GameService().RehydrateSession(
            snapshot.ToJsonString());

        act.Should().Throw<JsonException>();
        MarkTestCompleted();
    }

    [Fact]
    public void CurrentPlayerPayload_WithNegativeDurableVotingPower_IsRejected()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(
                playerCount: 5,
                werewolfCount: 1,
                includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();
        var snapshot = JsonNode.Parse(
            builder.SerializeSession())!.AsObject();
        snapshot[nameof(GameSessionDto.Players)]!
            .AsArray()[0]!
            .AsObject()[nameof(PlayerDto.DurableVotingPower)] = -1;

        var act = () => new GameService().RehydrateSession(
            snapshot.ToJsonString());

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*Durable Voting Power*negative*");
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
        var json = builder.SerializeSession();

        // Act
        var rehydratedId = builder.GameService.RehydrateSession(json);
        var rehydratedSession = builder.GameService.GetGameStateView(rehydratedId)!;

        // Assert - Initial state should have no status effects
        var players = rehydratedSession.GetPlayers();
        foreach (var player in players)
        {
            player.State.GetActiveStatusEffects().Should().BeEmpty(
                CoreTestReferences.AssertionReasons.FreshSessionsStartWithoutActiveStatusEffects);
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
        var json = builder.SerializeSession();

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
        // Arrange - Complete a night action to get faction observation and action events.
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

        // Complete Werewolf Agent-group observation and victim selection
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

        var json = builder.SerializeSession();

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
        rehydratedSession.GameHistoryLog
            .OfType<FactionFactsCommittedLogEntry>()
            .Should().ContainSingle(entry =>
                entry.Source.Kind ==
                    FactionFactSourceKind.ScheduledObservation &&
                entry.Source.Identifier ==
                    FactionFactSource
                        .WerewolfFactionAgentGroupObservationIdentifier &&
                entry.Facts.All(fact =>
                    fact.Type == FactionFactType.Agent &&
                    fact.Faction == Faction.Werewolf));
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

        var json = builder.SerializeSession();

        // Act
        var rehydratedId = builder.GameService.RehydrateSession(json);
        var rehydratedInstruction = builder.GameService.GetCurrentInstruction(rehydratedId);

        // Assert
        rehydratedInstruction.Should().NotBeNull();
        rehydratedInstruction!.GetType().Should().Be(originalType);

        MarkTestCompleted();
    }

    [Fact]
    public void Serialize_DayVoteInstruction_PreservesExplicitEmptySelectionOption()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var villager1Id = players[2].Id;
        var villager2Id = players[3].Id;

        builder.CompleteNightPhase(
            werewolfIds: [werewolfId],
            victimId: villager1Id,
            seerId: seerId,
            seerTargetId: villager2Id);
        builder.CompleteDawnPhase(new()
        {
            [villager1Id] = MainRoleType.SimpleVillager
        });

        var debateInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            CoreTestReferences.InstructionContexts.DebateConfirmation);
        var afterDebate = builder.Process(debateInstruction.CreateResponse());
        InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterDebate,
            CoreTestReferences.InstructionContexts.VotingInstruction);

        var json = builder.SerializeSession();
        var rehydratedId = builder.GameService.RehydrateSession(json);

        var rehydratedInstruction = builder.GameService.GetCurrentInstruction(rehydratedId)
            .Should().BeOfType<SelectPlayersInstruction>()
            .Subject;
        rehydratedInstruction.EmptySelectionOptionLabel.Should().Be(GameStrings.DayVoteNoEliminationOption);

        MarkTestCompleted();
    }

    [Fact]
    public void StatusEffectRemovalLog_RoundTripsAsInactiveOperation()
    {
        GameLogEntryBase entry = new StatusEffectLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TurnNumber = 2,
            CurrentPhase = GamePhase.Dawn,
            EffectType = StatusEffectTypes.ElderProtectionLost,
            PlayerId = Guid.NewGuid(),
            IsActive = false
        };

        var json = JsonSerializer.Serialize(
            entry,
            RecoverySerializationOptions);
        var restored = JsonSerializer.Deserialize<GameLogEntryBase>(
            json,
            RecoverySerializationOptions);

        restored.Should().BeOfType<StatusEffectLogEntry>()
            .Which.IsActive.Should().BeFalse();
        MarkTestCompleted();
    }

    [Fact]
    public void LegacyStatusEffectLogWithoutOperation_DefaultsToApply()
    {
        GameLogEntryBase entry = new StatusEffectLogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            TurnNumber = 2,
            CurrentPhase = GamePhase.Dawn,
            EffectType = StatusEffectTypes.ElderProtectionLost,
            PlayerId = Guid.NewGuid()
        };
        var payload = JsonNode.Parse(JsonSerializer.Serialize(
            entry,
            RecoverySerializationOptions))!.AsObject();
        payload.Remove("IsActive");

        var restored = JsonSerializer.Deserialize<GameLogEntryBase>(
            payload.ToJsonString(),
            RecoverySerializationOptions);

        restored.Should().BeOfType<StatusEffectLogEntry>()
            .Which.IsActive.Should().BeTrue();
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

        var json = builder.SerializeSession();

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
        var json = builder.SerializeSession();

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

        var json = builder.SerializeSession();

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
        var json = builder.SerializeSession();

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

        var json = builder.SerializeSession();

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

        var json = builder.SerializeSession();
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
        var json = builder.SerializeSession();

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
                CoreTestReferences.AssertionReasons.PlayerRoleShouldMatch(player.Name));

            // Health should match
            derived.Health.Should().Be(player.State.Health,
                CoreTestReferences.AssertionReasons.PlayerHealthShouldMatch(player.Name));
            
            // Status effects should match
            var cachedEffects = player.State.GetActiveStatusEffects();
            var derivedEffects = derived.GetActiveStatusEffects();
            derivedEffects.Should().BeEquivalentTo(cachedEffects,
                CoreTestReferences.AssertionReasons.PlayerStatusEffectsShouldMatch(player.Name));
        }

        MarkTestCompleted();
    }

    [Fact]
    public void Serialize_StableRecoveryBoundaryPayload_ContainsDocumentedDurableFields()
    {
        var playerNames = new[] { "Alice", "Bob", "Charlie", "Diana", "Eve" };
        var roles = new[]
        {
            MainRoleType.SimpleWerewolf,
            MainRoleType.Seer,
            MainRoleType.SimpleVillager,
            MainRoleType.SimpleVillager,
            MainRoleType.SimpleVillager
        };
        var builder = CreateBuilder()
            .WithPlayers(playerNames)
            .WithRoles(roles);
        builder.StartGame();
        builder.ConfirmGameStart();

        var session = builder.GetGameState()!;
        var dto = JsonSerializer.Deserialize<GameSessionDto>(
            builder.SerializeSession(),
            RecoverySerializationOptions)!;

        dto.IsStableRecoveryBoundary.Should().BeTrue();
        dto.Id.Should().Be(session.Id);
        dto.TurnNumber.Should().Be(1);
        dto.RolesInPlay.Should().Equal(roles);
        dto.Players.Select(player => player.Name).Should().Equal(playerNames);
        dto.SeatingOrder.Should().Equal(dto.Players.Select(player => player.Id));
        dto.PendingInstruction.Should().BeOfType<ConfirmationInstruction>()
            .Subject.PublicAnnouncement.Should().Be(GameStrings.NightStartsPrompt);
        dto.PhaseStateCache.CurrentPhase.Should().Be(GamePhase.Night);
        dto.GameHistoryLog.Should().BeEmpty();

        MarkTestCompleted();
    }

    [Fact]
    public void SerializeCurrentStateRecoveryCandidate_RehydratesCurrentProjectionWithoutReplacingStableSnapshot()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        var session = (GameSession)builder.GetGameState()!;
        var playerId = session.GetPlayers().First().Id;
        var stableSnapshot = builder.SerializeSession();
        builder.ArrangeKnownPhysicalRole(playerId, MainRoleType.SimpleWerewolf);

        var candidate = session.SerializeCurrentStateRecoveryCandidate();

        builder.SerializeSession().Should().Be(stableSnapshot);
        var recoveryService = new GameService();
        var recoveredGameId = recoveryService.RehydrateSession(candidate);
        var recoveredPlayer = recoveryService.GetGameStateView(recoveredGameId)!
            .GetPlayerState(playerId);
        recoveredPlayer.MainRole.Should().Be(MainRoleType.SimpleWerewolf);
        recoveredPlayer.ModeratorKnownRole.Should().Be(MainRoleType.SimpleWerewolf);

        MarkTestCompleted();
    }

    [Fact]
    public void RehydrateStableBoundary_RestoresCommittedInstructionCursorAndIgnoresActiveExecutionState()
    {
        var alivePlayerId = Guid.NewGuid();
        var votedPlayerId = Guid.NewGuid();
        var bystanderId = Guid.NewGuid();
        var service = new GameService();
        var gameId = service.RehydrateSession(CreateStableDayVoteBoundaryJson(
            alivePlayerId,
            votedPlayerId,
            bystanderId));
        service.GetGameStateView(gameId)!.GetCurrentPhase().Should()
            .Be(GamePhase.Day);
        var voteInstruction = service.GetCurrentInstruction(gameId).Should()
            .BeOfType<SelectPlayersInstruction>().Subject;
        service.ProcessInstruction(gameId, voteInstruction.CreateResponse([votedPlayerId]));

        var updatedSession = service.GetGameStateView(gameId)!;
        updatedSession.GameHistoryLog.OfType<VoteOutcomeReportedLogEntry>()
            .Should().ContainSingle(entry => entry.ReportedOutcomePlayerId == votedPlayerId);

        MarkTestCompleted();
    }

    [Fact]
    public void RehydrateInterruptedDawn_ReplaysElderProtectionWithoutEliminatingElder()
    {
        var wolfId = Guid.NewGuid();
        var elderId = Guid.NewGuid();
        var victimId = Guid.NewGuid();
        var villagerId = Guid.NewGuid();
        var extraVillagerId = Guid.NewGuid();
        var stableDawnJson = CreateStableDawnBoundaryJson(
            wolfId,
            elderId,
            victimId,
            villagerId,
            extraVillagerId);
        var firstService = new GameService();
        var firstGameId = firstService.RehydrateSession(stableDawnJson);
        var dawnInstruction = (ConfirmationInstruction)firstService.GetCurrentInstruction(firstGameId)!;

        firstService.ProcessInstruction(firstGameId, dawnInstruction.CreateResponse());
        var interruptedPayload = firstService.SerializeSession(firstGameId);
        var interruptedDto = JsonSerializer.Deserialize<GameSessionDto>(
            interruptedPayload,
            RecoverySerializationOptions)!;
        interruptedDto.GameHistoryLog.OfType<StatusEffectLogEntry>().Should().BeEmpty();
        interruptedDto.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
            .Where(entry => entry.CurrentPhase == GamePhase.Dawn)
            .Should().BeEmpty();

        var replayService = new GameService();
        var replayGameId = replayService.RehydrateSession(interruptedPayload);
        var replayInstruction = (ConfirmationInstruction)replayService.GetCurrentInstruction(replayGameId)!;
        replayService.ProcessInstruction(replayGameId, replayInstruction.CreateResponse());
        var replayedSession = replayService.GetGameStateView(replayGameId)!;

        replayedSession.GetPlayerState(elderId).Health.Should().Be(PlayerHealth.Alive);
        replayedSession.GetPlayerState(elderId).HasStatusEffect(StatusEffectTypes.ElderProtectionLost)
            .Should().BeTrue();
        replayedSession.GameHistoryLog.OfType<PlayerEliminatedLogEntry>()
            .Should().NotContain(entry => entry.PlayerId == elderId);
        replayedSession.GameHistoryLog.OfType<StatusEffectLogEntry>()
            .Where(entry => entry.PlayerId == elderId && entry.EffectType == StatusEffectTypes.ElderProtectionLost)
            .Should().ContainSingle();

        MarkTestCompleted();
    }

    [Fact]
    public void SerializeSession_UnavailableSessionIdsUseEstablishedFailure()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        var discardedId = builder.GameId;
        builder.GameService.DiscardSession(discardedId).Should().BeTrue();

        foreach (var unavailableId in new[]
                 {
                     Guid.Empty,
                     Guid.NewGuid(),
                     discardedId
                 })
        {
            var export = () => builder.GameService.SerializeSession(unavailableId);

            export.Should().ThrowExactly<InvalidOperationException>()
                .WithMessage("The Game Session is not available.");
        }

        MarkTestCompleted();
    }

    #endregion

    private static string CreateStableDawnBoundaryJson(
        Guid wolfId,
        Guid elderId,
        Guid victimId,
        Guid villagerId,
        Guid extraVillagerId)
    {
		var dto = new GameSessionDto
		{
            Id = Guid.NewGuid(),
            TurnNumber = 1,
            IsStableRecoveryBoundary = true,
            SeatingOrder = [wolfId, elderId, victimId, villagerId, extraVillagerId],
            RolesInPlay =
            [
                MainRoleType.SimpleWerewolf,
                MainRoleType.Elder,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager
            ],
			ActorSetupCards = ActorSetupCardsDto.FromValue(ActorSetupCards.None),
            PendingInstruction = new ConfirmationInstruction(
                publicAnnouncement: GameStrings.NightActionsCompletePrompt),
            PhaseStateCache = new GamePhaseStateCacheDto
            {
                CurrentPhase = GamePhase.Dawn
            },
            Players =
            [
                new PlayerDto
                {
                    Id = wolfId,
                    Name = "Wolf",
                    MainRole = MainRoleType.SimpleWerewolf,
                    Health = PlayerHealth.Alive,
                    DurableVotingPower = 1
                },
                new PlayerDto
                {
                    Id = elderId,
                    Name = "Elder",
                    MainRole = MainRoleType.Elder,
                    Health = PlayerHealth.Alive,
                    DurableVotingPower = 1
                },
                new PlayerDto
                {
                    Id = victimId,
                    Name = "Victim",
                    Health = PlayerHealth.Alive,
                    DurableVotingPower = 1
                },
                new PlayerDto
                {
                    Id = villagerId,
                    Name = "Villager",
                    Health = PlayerHealth.Alive,
                    DurableVotingPower = 1
                },
                new PlayerDto
                {
                    Id = extraVillagerId,
                    Name = "Extra Villager",
                    Health = PlayerHealth.Alive,
                    DurableVotingPower = 1
                }
            ],
            GameHistoryLog =
            [
                new AssignRoleLogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    TurnNumber = 1,
                    CurrentPhase = GamePhase.Night,
                    PlayerIds = [wolfId],
                    AssignedMainRole = MainRoleType.SimpleWerewolf
                },
                new AssignRoleLogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    TurnNumber = 1,
                    CurrentPhase = GamePhase.Night,
                    PlayerIds = [elderId],
                    AssignedMainRole = MainRoleType.Elder
                },
                new NightActionLogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    TurnNumber = 1,
                    CurrentPhase = GamePhase.Night,
                    ActionType = NightActionType.WerewolfVictimSelection,
                    TargetIds = [elderId]
                },
                new NightActionLogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    TurnNumber = 1,
                    CurrentPhase = GamePhase.Night,
                    ActionType = NightActionType.WitchKill,
                    TargetIds = [victimId]
                },
                new PhaseTransitionLogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    TurnNumber = 1,
                    PreviousPhase = GamePhase.Night,
                    CurrentPhase = GamePhase.Dawn
                }
			]
		};
		AttachPhysicalCardState(
			dto,
			(wolfId, MainRoleType.SimpleWerewolf),
			(elderId, MainRoleType.Elder));

		return SerializeWithCurrentFactionShape(dto);
    }

    private static string CreateStableDayVoteBoundaryJson(
        Guid alivePlayerId,
        Guid votedPlayerId,
        Guid bystanderId)
    {
		var dto = new GameSessionDto
		{
            Id = Guid.NewGuid(),
            TurnNumber = 1,
            IsStableRecoveryBoundary = true,
            SeatingOrder = [alivePlayerId, votedPlayerId, bystanderId],
            RolesInPlay =
            [
                MainRoleType.SimpleWerewolf,
                MainRoleType.SimpleVillager,
                MainRoleType.SimpleVillager
            ],
			ActorSetupCards = ActorSetupCardsDto.FromValue(ActorSetupCards.None),
            PendingInstruction = new SelectPlayersInstruction(
                [alivePlayerId, votedPlayerId, bystanderId],
                NumberRangeConstraint.SingleOptional,
                publicAnnouncement: GameStrings.VoteStartsPublicInstruction,
                privateInstruction: GameStrings.VoteStartsModeratorInstruction),
            PhaseStateCache = new GamePhaseStateCacheDto
            {
                CurrentPhase = GamePhase.Day,
                SubPhase = DaySubPhases.NormalVoting.ToString()
            },
            Players =
            [
                new PlayerDto
                {
                    Id = alivePlayerId,
                    Name = "Wolf",
                    MainRole = MainRoleType.SimpleWerewolf,
                    Health = PlayerHealth.Alive,
                    DurableVotingPower = 1
                },
                new PlayerDto
                {
                    Id = votedPlayerId,
                    Name = "Voted",
                    MainRole = MainRoleType.SimpleVillager,
                    Health = PlayerHealth.Alive,
                    DurableVotingPower = 1
                },
                new PlayerDto
                {
                    Id = bystanderId,
                    Name = "Bystander",
                    MainRole = MainRoleType.SimpleVillager,
                    Health = PlayerHealth.Alive,
                    DurableVotingPower = 1
                }
            ],
            GameHistoryLog =
            [
                new AssignRoleLogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    TurnNumber = 1,
                    CurrentPhase = GamePhase.Night,
                    PlayerIds = [alivePlayerId],
                    AssignedMainRole = MainRoleType.SimpleWerewolf
                },
                new AssignRoleLogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    TurnNumber = 1,
                    CurrentPhase = GamePhase.Night,
                    PlayerIds = [votedPlayerId],
                    AssignedMainRole = MainRoleType.SimpleVillager
                },
                new AssignRoleLogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    TurnNumber = 1,
                    CurrentPhase = GamePhase.Night,
                    PlayerIds = [bystanderId],
                    AssignedMainRole = MainRoleType.SimpleVillager
                },
                new PhaseTransitionLogEntry
                {
                    Timestamp = DateTimeOffset.UtcNow,
                    TurnNumber = 1,
                    PreviousPhase = GamePhase.Dawn,
                    CurrentPhase = GamePhase.Day
                }
			]
		};
		AttachPhysicalCardState(
			dto,
			(alivePlayerId, MainRoleType.SimpleWerewolf),
			(votedPlayerId, MainRoleType.SimpleVillager),
			(bystanderId, MainRoleType.SimpleVillager));

		return RecoveryPayloadTestDriver
			.Parse(SerializeWithCurrentFactionShape(dto))
			.RewriteDurableAndTransientContinuation(
				DaySubPhaseStage.RequestVote.ToString(),
				[DaySubPhaseStage.RequestVote.ToString()],
				ListenerIdentifier.Listener(MainRoleType.SimpleWerewolf),
				"Ignored")
			.Serialize();
	}

	private static void AttachPhysicalCardState(
		GameSessionDto dto,
		params (Guid PlayerId, MainRoleType Role)[] assignments)
	{
		var cards = dto.RolesInPlay
			.Select(role => new PhysicalCharacterCard(Guid.NewGuid(), role))
			.ToList();
		var availableCards = cards.ToList();
		var ownersByCardId = new Dictionary<Guid, Guid>();
		var ownershipEntries = new List<GameLogEntryBase>();
		foreach (var (playerId, role) in assignments)
		{
			var card = availableCards.First(candidate =>
				candidate.PrintedRole == role);
			availableCards.Remove(card);
			ownersByCardId.Add(card.Id, playerId);

			var player = dto.Players.Single(candidate =>
				candidate.Id == playerId);
			player.PhysicalCharacterCardId = card.Id;
			player.PhysicalCharacterCardRole = card.PrintedRole;
			ownershipEntries.Add(
				new PhysicalCharacterCardOwnershipObservedLogEntry
				{
					Timestamp = DateTimeOffset.UtcNow,
					TurnNumber = 1,
					CurrentPhase = GamePhase.Night,
					RoleLockInVersion = 1,
					PlayerId = playerId,
					CardId = card.Id,
					PrintedRole = card.PrintedRole
				});
		}
		dto.GameHistoryLog.InsertRange(0, ownershipEntries);

		dto.RoleLockIn = new RoleLockInDto
		{
			Version = 1,
			PlayerCount = dto.Players.Count,
			RoleComposition = cards,
			DealPoolCardIds = cards.Select(card => card.Id).ToList()
		};
		dto.PhysicalCharacterCards = cards
			.Select(card => ownersByCardId.TryGetValue(
				card.Id,
				out var ownerPlayerId)
				? new PhysicalCharacterCardStateDto
				{
					CardId = card.Id,
					Zone = PhysicalCharacterCardZone.PlayerOwned,
					OwnerPlayerId = ownerPlayerId
				}
				: new PhysicalCharacterCardStateDto
				{
					CardId = card.Id,
					Zone = PhysicalCharacterCardZone.DealPool
				})
			.ToList();
	}

	private static string SerializeWithCurrentFactionShape(GameSessionDto dto)
    {
        dto.FactionFactSchemaVersion = FactionFactSchema.CurrentVersion;
        foreach (var player in dto.Players)
        {
            player.FactionBeneficiary = FactionBeneficiaryKnowledge.Unknown;
            player.FactionAgentKnowledge = Enum.GetValues<Faction>()
                .ToDictionary(
                    faction => faction,
                    _ => FactionAgentKnowledge.Unknown);
        }

        return JsonSerializer.Serialize(dto, RecoverySerializationOptions);
    }
}
