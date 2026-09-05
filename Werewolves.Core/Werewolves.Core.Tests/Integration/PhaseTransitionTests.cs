using FluentAssertions;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

/// <summary>
/// Tests for phase and sub-phase transitions and state cache behavior.
/// Test IDs: PT-001 through PT-020
/// </summary>
public class PhaseTransitionTests : DiagnosticTestBase
{
    public PhaseTransitionTests(ITestOutputHelper output) : base(output) { }
    #region PT-001 to PT-004: Valid Transitions

    /// <summary>
    /// PT-001: New game starts in Night phase.
    /// </summary>
    [Fact]
    public void NewGame_StartsInNightPhase()
    {
        // Arrange & Act
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();

        // Assert
        var gameState = builder.GetGameState();
        gameState!.GetCurrentPhase().Should().Be(GamePhase.Night);

        MarkTestCompleted();
    }

    /// <summary>
    /// PT-002: Night.Start to Dawn.CalculateVictims is a valid transition.
    /// This test verifies that completing all night actions leads to Dawn phase.
    /// Note: Requires completing the full night action sequence.
    /// </summary>
    [Fact]
    public void NightStart_ToDawnCalculateVictims_IsValidTransition()
    {
        // Arrange: Simple game (5 players: 1 WW, 1 Seer, 3 Villagers)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Get player IDs: Player 0 = Werewolf, Player 1 = Seer, Players 2-4 = Villagers
        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var villager1 = players[2].Id;
        var villager2 = players[3].Id;

        // Act: Complete night phase
        builder.CompleteNightPhase(
            werewolfIds: new HashSet<Guid> { werewolfId },
            victimId: villager1,
            seerId: seerId,
            seerTargetId: villager2);

        // Assert: Should now be in Dawn phase
        var gameState = builder.GetGameState();
        gameState!.GetCurrentPhase().Should().Be(GamePhase.Dawn);

        MarkTestCompleted();
    }

    [Fact]
    public void NightToDawn_PreservesFinishInstructionAndDefersVictimWorkWithoutCheckingVictory()
    {
        // Werewolves already control the vote, so an early victory check would be observable.
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 3, includeSeer: false);
        builder.StartGame();
        builder.ConfirmGameStart();
        var session = builder.GetGameState()!;
        var players = session.GetPlayers().ToArray();
        var victim = players[3];
        var turn = session.TurnNumber;
        var observation = builder.ConfirmNightStart()
            .ModeratorInstruction.Should().BeOfType<SelectPlayersInstruction>().Subject;
        var targetSelection = builder.Process(observation.CreateResponse(
                players.Take(3).Select(player => player.Id).ToHashSet()))
            .ModeratorInstruction.Should().BeOfType<SelectPlayersInstruction>().Subject;
        var sleep = builder.Process(targetSelection.CreateResponse([victim.Id]))
            .ModeratorInstruction.Should().BeOfType<ConfirmationInstruction>().Subject;
        session.GetCurrentPhase().Should().Be(GamePhase.Night);

        var finishNight = builder.Process(sleep.CreateResponse())
            .ModeratorInstruction.Should().BeOfType<ConfirmationInstruction>().Subject;

        finishNight.Semantic.Should().Be(ModeratorInstructionSemantic.FinishNightActions);
        builder.GetCurrentInstruction()!.InstructionId.Should().Be(finishNight.InstructionId);
        session.GetCurrentPhase().Should().Be(GamePhase.Dawn);
        session.TurnNumber.Should().Be(turn);
        session.GameHistoryLog.OfType<PhaseTransitionLogEntry>()
            .Should().ContainSingle(entry => entry.CurrentPhase == GamePhase.Dawn);
        session.GameHistoryLog.OfType<VictoryConditionMetLogEntry>().Should().BeEmpty();
        session.GameHistoryLog.OfType<DawnVictimDeterminedLogEntry>().Should().BeEmpty();
        session.GameHistoryLog.OfType<RoleRevealLogEntry>().Should().BeEmpty();
        session.GameHistoryLog.OfType<PlayerEliminatedLogEntry>().Should().BeEmpty();
        session.GetPlayerState(victim.Id).Health.Should().Be(PlayerHealth.Alive);

        var reveal = builder.Process(finishNight.CreateResponse())
            .ModeratorInstruction.Should().BeOfType<AssignRolesInstruction>().Subject;

        reveal.SelectableRolesForPlayers.Keys.Should().Equal(victim.Id);
        session.GetCurrentPhase().Should().Be(GamePhase.Dawn);
        session.TurnNumber.Should().Be(turn);
        session.GameHistoryLog.OfType<DawnVictimDeterminedLogEntry>()
            .Should().ContainSingle(entry => entry.PlayerId == victim.Id);
        session.GameHistoryLog.OfType<VictoryConditionMetLogEntry>().Should().BeEmpty();
        session.GetPlayerState(victim.Id).Health.Should().Be(PlayerHealth.Alive);

        MarkTestCompleted();
    }

    /// <summary>
    /// PT-003: Dawn.Finalize to Day.Debate is a valid transition.
    /// </summary>
    [Fact]
    public void DawnFinalize_ToDayDebate_IsValidTransition()
    {
        // Arrange: Simple game (5 players: 1 WW, 1 Seer, 3 Villagers)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Get player IDs: Player 0 = Werewolf, Player 1 = Seer, Players 2-4 = Villagers
        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var villager1 = players[2].Id;
        var villager2 = players[3].Id;

        // Complete night phase
        builder.CompleteNightPhase(
            werewolfIds: new HashSet<Guid> { werewolfId },
            victimId: villager1,
            seerId: seerId,
            seerTargetId: villager2);

        // Act: Complete dawn phase
        builder.CompleteDawnPhase(new()
        {
            [villager1] = MainRoleType.SimpleVillager
        });

        // Assert: Should now be in Day phase
        var gameState = builder.GetGameState();
        gameState!.GetCurrentPhase().Should().Be(GamePhase.Day);

        MarkTestCompleted();
    }

    /// <summary>
    /// PT-004: Day.Finalize to Night.Start is a valid transition.
    /// TurnNumber should increment when transitioning from Day to Night.
    /// </summary>
    [Fact]
    public void DayFinalize_ToNightStart_IsValidTransition()
    {
        // Arrange: Simple game (5 players: 1 WW, 1 Seer, 3 Villagers)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Get player IDs: Player 0 = Werewolf, Player 1 = Seer, Players 2-4 = Villagers
        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var villager1 = players[2].Id;
        var villager2 = players[3].Id;

        // Complete night phase
        builder.CompleteNightPhase(
            werewolfIds: new HashSet<Guid> { werewolfId },
            victimId: villager1,
            seerId: seerId,
            seerTargetId: villager2);

        // Complete dawn phase
        builder.CompleteDawnPhase(new()
        {
            [villager1] = MainRoleType.SimpleVillager
        });

        // Act: Complete day phase with a lynch (lynch a villager who is still alive)
        builder.CompleteDayPhaseWithLynch(villager2, new()
        {
            [villager2] = MainRoleType.SimpleVillager
        });

        // Assert: Should now be in Night phase with turn number incremented
        var gameState = builder.GetGameState();
        gameState!.GetCurrentPhase().Should().Be(GamePhase.Night);
        gameState!.TurnNumber.Should().Be(2, CoreTestReferences.AssertionReasons.TurnNumberIncrementsAfterDayToNight);

        MarkTestCompleted();
    }

    #endregion

    #region PT-010 to PT-011: Public Phase Continuation

    /// <summary>
    /// PT-010: Completing Night exposes a usable Dawn continuation.
    /// </summary>
    [Fact]
    public void MainPhaseTransition_AdvancesPublicFlowToDawn()
    {
        // Arrange: Simple game
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var villager1 = players[2].Id;
        var villager2 = players[3].Id;

        // Verify we're in Night phase.
        var session = (GameSession)builder.GetGameState()!;
        session.GetCurrentPhase().Should().Be(GamePhase.Night);

        // Act: Complete night phase (transitions to Dawn)
        builder.CompleteNightPhase(
            werewolfIds: new HashSet<Guid> { werewolfId },
            victimId: villager1,
            seerId: seerId,
            seerTargetId: villager2);

        // Assert: the public phase and instruction both moved to Dawn work.
        session.GetCurrentPhase().Should().Be(GamePhase.Dawn);
        builder.GetCurrentInstruction().Should().NotBeNull();

        MarkTestCompleted();
    }

    /// <summary>
    /// PT-011: Completing Dawn exposes a usable Day continuation.
    /// </summary>
    [Fact]
    public void SubPhaseTransition_AdvancesPublicFlowToDay()
    {
        // Arrange: Simple game
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var players = builder.GetGameState()!.GetPlayers().ToList();
        var werewolfId = players[0].Id;
        var seerId = players[1].Id;
        var villager1 = players[2].Id;
        var villager2 = players[3].Id;

        // Complete night phase to get to Dawn
        builder.CompleteNightPhase(
            werewolfIds: new HashSet<Guid> { werewolfId },
            victimId: villager1,
            seerId: seerId,
            seerTargetId: villager2);

        var session = (GameSession)builder.GetGameState()!;
        session.GetCurrentPhase().Should().Be(GamePhase.Dawn);

        // Act: Complete dawn phase (transitions to Day)
        builder.CompleteDawnPhase(new()
        {
            [villager1] = MainRoleType.SimpleVillager
        });

        // Assert: the public phase and instruction both moved to Day work.
        session.GetCurrentPhase().Should().Be(GamePhase.Day);
        builder.GetCurrentInstruction().Should().NotBeNull();

        MarkTestCompleted();
    }

    #endregion


    #region Additional Phase Transition Tests

    /// <summary>
    /// Verify game starts in Night phase.
    /// </summary>
    [Fact]
    public void NewGame_HasNightAsInitialPhase()
    {
        // Arrange & Act
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();

        // Assert
        var gameState = builder.GetGameState();
        gameState!.GetCurrentPhase().Should().Be(GamePhase.Night);

        MarkTestCompleted();
    }

    /// <summary>
    /// Verify the initial instruction after game start is a confirmation.
    /// </summary>
    [Fact]
    public void NewGame_HasConfirmationInstruction()
    {
        // Arrange & Act
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();

        // Assert
        var instruction = builder.GetCurrentInstruction();
        instruction.Should().BeOfType<StartGameConfirmationInstruction>();

        MarkTestCompleted();
    }

    /// <summary>
    /// After transitioning to Night, pending instruction should not be null.
    /// </summary>
    [Fact]
    public void AfterNightTransition_HasPendingInstruction()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();

        // Act
        builder.ConfirmGameStart();

        // Assert
        var instruction = builder.GetCurrentInstruction();
        instruction.Should().NotBeNull(CoreTestReferences.AssertionReasons.NightPhaseHasActiveInstruction);

        MarkTestCompleted();
    }

    #endregion
}
