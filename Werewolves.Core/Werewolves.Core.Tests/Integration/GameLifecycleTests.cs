using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

/// <summary>
/// Tests for game lifecycle: creation, game start confirmation, and phase transitions.
/// Test IDs: GL-001 through GL-020
/// </summary>
public class GameLifecycleTests : DiagnosticTestBase
{
    public GameLifecycleTests(ITestOutputHelper output) : base(output) { }
    #region GL-001 to GL-003: Game Creation

    /// <summary>
    /// GL-001: StartNewGame with valid roles and players returns StartGameConfirmationInstruction.
    /// </summary>
    [Fact]
    public void StartNewGame_WithValidRolesAndPlayers_ReturnsStartGameConfirmationInstruction()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);

        // Act
        var instruction = builder.StartGame();

        // Assert
        instruction.Should().NotBeNull();
        instruction.Should().BeOfType<StartGameConfirmationInstruction>();
        instruction.GameGuid.Should().NotBeEmpty();

        MarkTestCompleted();
    }

    /// <summary>
    /// GL-002: StartNewGame with empty roles throws InvalidOperationException.
    /// GameSessionConfig.EnforceValidity throws InvalidOperationException for invalid configs.
    /// </summary>
    [Fact]
    public void StartNewGame_WithEmptyRoles_ThrowsInvalidOperationException()
    {
        // Arrange
        var gameService = new GameLogic.Services.GameService();
        var playerNames = new List<string> { "Alice", "Bob", "Charlie", "Diana", "Eve" };
        var emptyRoles = new List<MainRoleType>();

        // Act
        var act = () => gameService.StartNewGameWithObserver(playerNames, emptyRoles);

        // Assert - GameSessionConfig.EnforceValidity throws InvalidOperationException
        act.Should().Throw<InvalidOperationException>();

        MarkTestCompleted();
    }

    /// <summary>
    /// GL-003: StartNewGame creates session with correct player count.
    /// </summary>
    [Fact]
    public void StartNewGame_CreatesSessionWithCorrectPlayerCount()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);

        // Act
        builder.StartGame();
        var gameState = builder.GetGameState();

        // Assert
        gameState.Should().NotBeNull();
        gameState!.GetPlayers().Should().HaveCount(5);

        MarkTestCompleted();
    }

    /// <summary>
    /// GL-003b: Players are created in correct seating order.
    /// </summary>
    [Fact]
    public void StartNewGame_PlayersInCorrectSeatingOrder()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithPlayers("Alice", "Bob", "Charlie", "Diana", "Eve")
            .WithRoles(MainRoleType.SimpleWerewolf, MainRoleType.Seer, MainRoleType.SimpleVillager, MainRoleType.SimpleVillager, MainRoleType.SimpleVillager);

        // Act
        builder.StartGame();
        var gameState = builder.GetGameState();

        // Assert
        var playerNames = gameState!.GetPlayers().Select(p => p.Name).ToList();
        playerNames.Should().ContainInOrder("Alice", "Bob", "Charlie", "Diana", "Eve");

        MarkTestCompleted();
    }

    #endregion

    #region GL-010 to GL-011: Game Start Confirmation

    /// <summary>
    /// GL-010: Confirming game start triggers Night phase execution.
    /// </summary>
    [Fact]
    public void ConfirmGameStart_TransitionsToNightPhase()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();

        // Act
        var result = builder.ConfirmGameStart();

        // Assert
        result.IsSuccess.Should().BeTrue();
        var gameState = builder.GetGameState();
        gameState!.GetCurrentPhase().Should().Be(GamePhase.Night);

        MarkTestCompleted();
    }

    /// <summary>
    /// GL-011: After game start confirmation, TurnNumber is 1.
    /// </summary>
    [Fact]
    public void ConfirmGameStart_SetsCorrectTurnNumber()
    {
        // Arrange
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();

        // Act
        builder.ConfirmGameStart();
        var gameState = builder.GetGameState();

        // Assert
        gameState!.TurnNumber.Should().Be(1);

        MarkTestCompleted();
    }

    #endregion

    #region GL-020: Full Game Cycle

    /// <summary>
    /// GL-020: Complete game cycle transitions Night → Dawn → Day correctly.
    /// This is a more complex integration test that will be expanded as more
    /// functionality is implemented.
    /// </summary>
    [Fact]
    public void CompleteGameCycle_NightToDawnToDay_TransitionsCorrectly()
    {
        // Arrange - Simple 5 player game: 1 WW, 1 Seer, 3 Villagers
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Verify we're in Night phase
        var gameState = builder.GetGameState();
        gameState!.GetCurrentPhase().Should().Be(GamePhase.Night);

        // Note: Full cycle test requires completing night actions.
        // This will be expanded when night action flow is fully testable.
        // For now, we verify the initial Night phase transition.

        MarkTestCompleted();
    }

    #endregion

    #region GL-030 to GL-036: Configuration Validation

    /// <summary>
    /// GL-030: TryGetPlayerConfigIssues with duplicate names returns NonUniqueError.
    /// </summary>
    [Fact]
    public void TryGetPlayerConfigIssues_WithDuplicateNames_ReturnsNonUniqueError()
    {
        // Arrange
        var players = new List<string> { "Alice", "Bob", "Charlie", "Alice", "Eve" };

        // Act
        var hasIssues = Werewolves.Core.StateModels.Models.GameSessionConfig.TryGetPlayerConfigIssues(players, out var issues);

        // Assert
        hasIssues.Should().BeTrue();
        issues.Should().Contain(e => e.Type == GameConfigValidationErrorType.NonUniquePlayerNames);

        MarkTestCompleted();
    }

    /// <summary>
    /// GL-031: TryGetPlayerConfigIssues with fewer than 5 players returns TooFewError.
    /// </summary>
    [Fact]
    public void TryGetPlayerConfigIssues_WithFewerThan5Players_ReturnsTooFewError()
    {
        // Arrange
        var players = new List<string> { "Alice", "Bob", "Charlie", "Diana" };

        // Act
        var hasIssues = Werewolves.Core.StateModels.Models.GameSessionConfig.TryGetPlayerConfigIssues(players, out var issues);

        // Assert
        hasIssues.Should().BeTrue();
        issues.Should().Contain(e => e.Type == GameConfigValidationErrorType.TooFewPlayers);

        MarkTestCompleted();
    }

    /// <summary>
    /// GL-032: TryGetPlayerConfigIssues with 5 valid players returns no issues.
    /// </summary>
    [Fact]
    public void TryGetPlayerConfigIssues_With5ValidPlayers_ReturnsNoIssues()
    {
        // Arrange
        var players = new List<string> { "Alice", "Bob", "Charlie", "Diana", "Eve" };

        // Act
        var hasIssues = Werewolves.Core.StateModels.Models.GameSessionConfig.TryGetPlayerConfigIssues(players, out var issues);

        // Assert
        hasIssues.Should().BeFalse();
        issues.Should().BeEmpty();

        MarkTestCompleted();
    }

    /// <summary>
    /// GL-033: GetExpectedRoleCount without special roles returns player count.
    /// </summary>
    [Fact]
    public void GetExpectedRoleCount_WithoutSpecialRoles_ReturnsPlayerCount()
    {
        // Arrange
        var playerCount = 7;
        var roles = new List<MainRoleType>
        {
            MainRoleType.SimpleWerewolf,
            MainRoleType.Seer,
            MainRoleType.SimpleVillager,
            MainRoleType.SimpleVillager,
            MainRoleType.SimpleVillager,
            MainRoleType.SimpleVillager,
            MainRoleType.SimpleVillager
        };

        // Act
        var expectedCount = Werewolves.Core.StateModels.Models.GameSessionConfig.GetExpectedRoleCount(playerCount, roles);

        // Assert
        expectedCount.Should().Be(playerCount);

        MarkTestCompleted();
    }

    /// <summary>
    /// GL-034: GetExpectedRoleCount with Thief returns player count + 2.
    /// </summary>
    [Fact]
    public void GetExpectedRoleCount_WithThief_ReturnsPlayerCountPlus2()
    {
        // Arrange
        var playerCount = 6;
        var roles = new List<MainRoleType>
        {
            MainRoleType.SimpleWerewolf,
            MainRoleType.Seer,
            MainRoleType.Thief, // Requires 2 extra roles
            MainRoleType.SimpleVillager,
            MainRoleType.SimpleVillager,
            MainRoleType.SimpleVillager,
            MainRoleType.SimpleVillager, // Extra for Thief
            MainRoleType.SimpleVillager  // Extra for Thief
        };

        // Act
        var expectedCount = Werewolves.Core.StateModels.Models.GameSessionConfig.GetExpectedRoleCount(playerCount, roles);

        // Assert
        expectedCount.Should().Be(playerCount + 2);

        MarkTestCompleted();
    }

    /// <summary>
    /// GL-035: GetExpectedRoleCount with Actor returns player count + 3.
    /// </summary>
    [Fact]
    public void GetExpectedRoleCount_WithActor_ReturnsPlayerCountPlus3()
    {
        // Arrange
        var playerCount = 6;
        var roles = new List<MainRoleType>
        {
            MainRoleType.SimpleWerewolf,
            MainRoleType.Seer,
            MainRoleType.Actor, // Requires 3 extra roles
            MainRoleType.SimpleVillager,
            MainRoleType.SimpleVillager,
            MainRoleType.SimpleVillager,
            MainRoleType.SimpleVillager, // Extra for Actor
            MainRoleType.SimpleVillager, // Extra for Actor
            MainRoleType.SimpleVillager  // Extra for Actor
        };

        // Act
        var expectedCount = Werewolves.Core.StateModels.Models.GameSessionConfig.GetExpectedRoleCount(playerCount, roles);

        // Assert
        expectedCount.Should().Be(playerCount + 3);

        MarkTestCompleted();
    }

    /// <summary>
    /// GL-036: GetExpectedRoleCount with Thief and Actor returns player count + 5.
    /// </summary>
    [Fact]
    public void GetExpectedRoleCount_WithThiefAndActor_ReturnsPlayerCountPlus5()
    {
        // Arrange
        var playerCount = 7;
        var roles = new List<MainRoleType>
        {
            MainRoleType.SimpleWerewolf,
            MainRoleType.Seer,
            MainRoleType.Thief, // +2
            MainRoleType.Actor, // +3
            MainRoleType.SimpleVillager,
            MainRoleType.SimpleVillager,
            MainRoleType.SimpleVillager,
            // 5 extras for Thief + Actor
            MainRoleType.SimpleVillager,
            MainRoleType.SimpleVillager,
            MainRoleType.SimpleVillager,
            MainRoleType.SimpleVillager,
            MainRoleType.SimpleVillager
        };

        // Act
        var expectedCount = Werewolves.Core.StateModels.Models.GameSessionConfig.GetExpectedRoleCount(playerCount, roles);

        // Assert
        expectedCount.Should().Be(playerCount + 5);

        MarkTestCompleted();
    }

    #endregion
}
