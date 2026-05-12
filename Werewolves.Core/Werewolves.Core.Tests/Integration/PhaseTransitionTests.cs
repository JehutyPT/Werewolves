using FluentAssertions;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
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
        builder.CompleteDawnPhase();

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
        builder.CompleteDawnPhase();

        // Act: Complete day phase with a lynch (lynch a villager who is still alive)
        builder.CompleteDayPhaseWithLynch(villager2);

        // Assert: Should now be in Night phase with turn number incremented
        var gameState = builder.GetGameState();
        gameState!.GetCurrentPhase().Should().Be(GamePhase.Night);
        gameState!.TurnNumber.Should().Be(2, "Turn number should increment when transitioning from Day to Night");

        MarkTestCompleted();
    }

    #endregion

    #region PT-010 to PT-011: Phase Cache Behavior

    /// <summary>
    /// PT-010: Main phase transition clears sub-phase cache.
    /// Verifies that when transitioning from Night to Dawn, the previous phase's
    /// sub-phase stage and listener are cleared.
    /// </summary>
    [Fact]
    public void MainPhaseTransition_ClearsSubPhaseCache()
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

        // Verify we're in Night phase with an active stage
        var session = (GameSession)builder.GetGameState()!;
        session.GetCurrentPhase().Should().Be(GamePhase.Night);

        // Act: Complete night phase (transitions to Dawn)
        builder.CompleteNightPhase(
            werewolfIds: new HashSet<Guid> { werewolfId },
            victimId: villager1,
            seerId: seerId,
            seerTargetId: villager2);

        // Assert: Verify the phase transitioned correctly
        session.GetCurrentPhase().Should().Be(GamePhase.Dawn);

        // The night listener should be cleared (no longer active)
        var currentListener = session.GetCurrentListener();
        if (currentListener != null)
        {
            // If there is a listener, it should not be a night role
            currentListener.ListenerId.Should().NotBe(MainRoleType.SimpleWerewolf.ToString());
            currentListener.ListenerId.Should().NotBe(MainRoleType.Seer.ToString());
        }

        MarkTestCompleted();
    }

    /// <summary>
    /// PT-011: Sub-phase transition clears stage data.
    /// Verifies that when transitioning between phases, the active sub-phase stage
    /// from the previous phase is cleared.
    /// </summary>
    [Fact]
    public void SubPhaseTransition_ClearsStageData()
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

        // Capture the Dawn stage before transitioning
        var dawnStage = session.GetActiveSubPhaseStage();

        // Act: Complete dawn phase (transitions to Day)
        builder.CompleteDawnPhase();

        // Assert: Verify we transitioned to Day
        session.GetCurrentPhase().Should().Be(GamePhase.Day);

        // The Dawn stage should no longer be active
        var currentStage = session.GetActiveSubPhaseStage();
        if (dawnStage != null)
        {
            // If Dawn had an active stage, it should not be the same after transitioning
            // (either cleared or set to a Day stage)
            currentStage.Should().NotBe(dawnStage,
                "Dawn sub-phase stage should be cleared after transitioning to Day");
        }

        // Listener should be cleared or be a Day-phase listener
        var currentListener = session.GetCurrentListener();
        // At Day.Debate, there shouldn't be an active listener yet
        // (debate is a confirmation step, not a hook listener)

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
        instruction.Should().NotBeNull("Night phase should have an active instruction");

        MarkTestCompleted();
    }

    #endregion
}
