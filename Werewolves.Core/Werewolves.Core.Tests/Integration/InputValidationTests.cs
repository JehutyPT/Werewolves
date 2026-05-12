using FluentAssertions;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

/// <summary>
/// Tests for input validation: wrong response types, null responses, etc.
/// Test IDs: IV-001 through IV-002
/// </summary>
public class InputValidationTests : DiagnosticTestBase
{
    public InputValidationTests(ITestOutputHelper output) : base(output) { }

    #region IV-001 to IV-002: Response Type Validation

    /// <summary>
    /// IV-001: ProcessInstruction with wrong response type returns failure.
    /// Given: Pending SelectPlayersInstruction (e.g., werewolf victim selection)
    /// When: Moderator provides a ConfirmationResponse instead of SelectPlayersResponse
    /// Then: ProcessResult.IsSuccess is false, game state unchanged
    /// </summary>
    [Fact]
    public void ProcessInstruction_WrongResponseType_ReturnsFailure()
    {
        // Arrange - 5 players: 1 WW, 1 Seer, 3 Villagers
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Confirm night starts
        var nightStartInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            "Night start confirmation");
        var nightStartResponse = nightStartInstruction.CreateResponse(true);
        builder.Process(nightStartResponse);

        // Get the SelectPlayersInstruction (werewolf identification)
        var selectInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction(),
            "Werewolf identification instruction");

        // Capture game state before the invalid response
        var gameStateBefore = builder.GetGameState()!;
        var logCountBefore = gameStateBefore.GameHistoryLog.Count();

        // Create a wrong response type - ConfirmationResponse instead of SelectPlayersResponse
        // We need to provide at least one announcement string to create the instruction
        var wrongResponse = new ConfirmationInstruction(privateInstruction: "Test").CreateResponse(true);

        // Act - Attempt to process with wrong response type
        // The current implementation throws an InvalidOperationException when response type doesn't match
        Action act = () => builder.Process(wrongResponse);

        // Assert - Should throw an exception indicating type mismatch
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Confirmation*");

        // Verify game state is unchanged
        var gameStateAfter = builder.GetGameState()!;
        gameStateAfter.GameHistoryLog.Count().Should().Be(logCountBefore);

        MarkTestCompleted();
    }

    /// <summary>
    /// IV-002: ProcessInstruction with null response returns failure.
    /// Given: Any pending instruction
    /// When: Moderator provides null response
    /// Then: ProcessResult.IsSuccess is false, game state unchanged
    /// </summary>
    [Fact]
    public void ProcessInstruction_NullResponse_ReturnsFailure()
    {
        // Arrange - 5 players: 1 WW, 1 Seer, 3 Villagers
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Get any pending instruction (the StartGameConfirmation or next)
        var currentInstruction = builder.GetCurrentInstruction();
        currentInstruction.Should().NotBeNull("Game should have a pending instruction after start");

        // Capture game state before the null response
        var gameStateBefore = builder.GetGameState()!;
        var logCountBefore = gameStateBefore.GameHistoryLog.Count();

        // Act - Attempt to process with null response
        Action act = () => builder.Process(null!);

        // Assert - Should throw an exception or return failure
        // Null response should be rejected
        act.Should().Throw<Exception>();

        // Verify game state is unchanged
        var gameStateAfter = builder.GetGameState()!;
        gameStateAfter.GameHistoryLog.Count().Should().Be(logCountBefore);

        MarkTestCompleted();
    }

    #endregion

    #region IV-010 to IV-013: Player Selection Validation

    /// <summary>
    /// IV-010: SelectPlayers with empty selection when Single required returns failure.
    /// Given: SelectPlayersInstruction with NumberRangeConstraint.Single
    /// When: Moderator provides empty player selection
    /// Then: ProcessResult.IsSuccess is false (exception thrown)
    /// </summary>
    [Fact]
    public void SelectPlayers_EmptySelection_WhenSingleRequired_ReturnsFailure()
    {
        // Arrange - 5 players: 1 WW, 1 Seer, 3 Villagers
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Confirm night starts
        ConfirmNightStart(builder);

        // Get the SelectPlayersInstruction (werewolf identification) with Single constraint
        var selectInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction(),
            "Werewolf identification instruction");

        // Verify the instruction requires exactly one selection
        selectInstruction.CountConstraint.Should().Be(NumberRangeConstraint.Single);

        // Act - Attempt to create response with empty selection
        Action act = () => selectInstruction.CreateResponse([]);

        // Assert - Should throw an exception for constraint violation
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Minimum*1*required*0*provided*");

        MarkTestCompleted();
    }

    /// <summary>
    /// IV-011: SelectPlayers with too many players returns failure.
    /// Given: SelectPlayersInstruction with NumberRangeConstraint.Single
    /// When: Moderator provides two player IDs
    /// Then: ProcessResult.IsSuccess is false (exception thrown)
    /// </summary>
    [Fact]
    public void SelectPlayers_TooManyPlayers_ReturnsFailure()
    {
        // Arrange - 5 players: 1 WW, 1 Seer, 3 Villagers
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Confirm night starts
        ConfirmNightStart(builder);

        // Get the SelectPlayersInstruction (werewolf identification) with Single constraint
        var selectInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction(),
            "Werewolf identification instruction");

        // Verify the instruction requires exactly one selection
        selectInstruction.CountConstraint.Should().Be(NumberRangeConstraint.Single);

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();

        // Act - Attempt to create response with two players when only one is expected
        Action act = () => selectInstruction.CreateResponse([players[0].Id, players[1].Id]);

        // Assert - Should throw an exception for constraint violation
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Maximum*1*allowed*2*provided*");

        MarkTestCompleted();
    }

    /// <summary>
    /// IV-012: SelectPlayers with invalid player ID returns failure.
    /// Given: SelectPlayersInstruction with valid selectable player list
    /// When: Moderator provides a random GUID not in the game
    /// Then: ProcessResult.IsSuccess is false (exception thrown)
    /// </summary>
    [Fact]
    public void SelectPlayers_InvalidPlayerId_ReturnsFailure()
    {
        // Arrange - 5 players: 1 WW, 1 Seer, 3 Villagers
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Confirm night starts
        ConfirmNightStart(builder);

        // Get the SelectPlayersInstruction (werewolf identification)
        var selectInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction(),
            "Werewolf identification instruction");

        // Generate a random GUID that is not in the game
        var invalidPlayerId = Guid.NewGuid();

        // Act - Attempt to create response with invalid player ID
        Action act = () => selectInstruction.CreateResponse([invalidPlayerId]);

        // Assert - Should throw an exception for invalid player ID
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Selected player IDs are not valid*");

        MarkTestCompleted();
    }

    /// <summary>
    /// IV-013: SelectPlayers with non-selectable player returns failure.
    /// Given: SelectPlayersInstruction excluding werewolves from selectable targets (werewolf victim selection)
    /// When: Moderator provides a werewolf player ID (which is in the game but not selectable)
    /// Then: ProcessResult.IsSuccess is false (exception thrown)
    /// </summary>
    [Fact]
    public void SelectPlayers_NonSelectablePlayer_ReturnsFailure()
    {
        // Arrange - 5 players: 1 WW, 1 Seer, 3 Villagers
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        // Confirm night starts
        ConfirmNightStart(builder);

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();

        // Get werewolf identification instruction and identify the werewolf
        var identifyInstruction = InstructionAssert.ExpectType<SelectPlayersInstruction>(
            builder.GetCurrentInstruction(),
            "Werewolf identification instruction");

        var werewolfPlayer = players[0]; // First player is the werewolf
        var identifyResponse = identifyInstruction.CreateResponse([werewolfPlayer.Id]);
        var afterIdentify = builder.Process(identifyResponse);

        // Get victim selection instruction - werewolves should NOT be selectable
        var victimInstruction = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
            afterIdentify,
            "Werewolf victim selection");

        // Verify the werewolf is NOT in the selectable list
        victimInstruction.SelectablePlayerIds.Should().NotContain(werewolfPlayer.Id);

        // Act - Attempt to select the werewolf as victim (non-selectable player)
        Action act = () => victimInstruction.CreateResponse([werewolfPlayer.Id]);

        // Assert - Should throw an exception for non-selectable player
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Selected player IDs are not valid*");

        MarkTestCompleted();
    }

    #region Helper Methods

    /// <summary>
    /// Confirms the "night starts" instruction that precedes the hook loop.
    /// </summary>
    private static void ConfirmNightStart(GameTestBuilder builder)
    {
        var nightStartInstruction = InstructionAssert.ExpectType<ConfirmationInstruction>(
            builder.GetCurrentInstruction(),
            "Night start confirmation");
        var response = nightStartInstruction.CreateResponse(true);
        builder.Process(response);
    }

    #endregion

    #endregion

    #region IV-020 to IV-021: Role Assignment Validation

    /// <summary>
    /// IV-020: AssignRole with invalid role returns failure.
    /// Given: AssignRolesInstruction requesting role for eliminated player
    /// When: Moderator provides a role not in RolesInPlay (e.g., assign Witch when only SimpleWerewolf, Seer, SimpleVillager are in play)
    /// Then: ProcessResult.IsSuccess is false (exception thrown)
    /// </summary>
    [Fact]
    public void AssignRole_InvalidRole_ReturnsFailure()
    {
        // Arrange - 5 players: 1 WW, 1 Seer, 3 Villagers (no Witch in play)
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolf = players[0]; // Index 0 is werewolf per WithSimpleGame
        var victim = players[2];   // Index 2 is first villager

        // Complete night phase - werewolf attacks victim
        builder.CompleteNightPhase(
            werewolfIds: [werewolf.Id],
            victimId: victim.Id,
            seerId: players[1].Id,
            seerTargetId: werewolf.Id);

        // Get the AssignRolesInstruction for the eliminated victim
        var assignInstruction = InstructionAssert.ExpectType<AssignRolesInstruction>(
            builder.GetCurrentInstruction(),
            "Role assignment instruction for eliminated victim");

        // Verify victim is in the assignment request
        assignInstruction.PlayersForAssignment.Should().Contain(victim.Id);

        // Act - Attempt to assign a role that's NOT in the game (Witch is not in play)
        var invalidRoleAssignment = new Dictionary<Guid, MainRoleType>
        {
            { victim.Id, MainRoleType.Witch }
        };

        Action act = () => assignInstruction.CreateResponse(invalidRoleAssignment);

        // Assert - Should throw an exception for invalid role
        act.Should().Throw<ArgumentException>()
            .WithMessage($"*{MainRoleType.Witch}*not in the list of assignable roles*");

        MarkTestCompleted();
    }

    /// <summary>
    /// IV-021: AssignRole with wrong player returns failure.
    /// Given: AssignRolesInstruction for specific player (e.g., eliminated victim)
    /// When: Moderator provides a different player's ID than the one specified in the instruction
    /// Then: ProcessResult.IsSuccess is false (exception thrown)
    /// </summary>
    [Fact]
    public void AssignRole_WrongPlayer_ReturnsFailure()
    {
        // Arrange - 5 players: 1 WW, 1 Seer, 3 Villagers
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolf = players[0]; // Index 0 is werewolf per WithSimpleGame
        var victim = players[2];   // Index 2 is first villager
        var otherVillager = players[3]; // Index 3 is second villager (NOT eliminated)

        // Complete night phase - werewolf attacks victim
        builder.CompleteNightPhase(
            werewolfIds: [werewolf.Id],
            victimId: victim.Id,
            seerId: players[1].Id,
            seerTargetId: werewolf.Id);

        // Get the AssignRolesInstruction for the eliminated victim
        var assignInstruction = InstructionAssert.ExpectType<AssignRolesInstruction>(
            builder.GetCurrentInstruction(),
            "Role assignment instruction for eliminated victim");

        // Verify victim is in the assignment request, but the other villager is NOT
        assignInstruction.PlayersForAssignment.Should().Contain(victim.Id);
        assignInstruction.PlayersForAssignment.Should().NotContain(otherVillager.Id);

        // Act - Attempt to assign a role to the WRONG player (other villager instead of victim)
        var wrongPlayerAssignment = new Dictionary<Guid, MainRoleType>
        {
            { otherVillager.Id, MainRoleType.SimpleVillager }
        };

        Action act = () => assignInstruction.CreateResponse(wrongPlayerAssignment);

        // Assert - Should throw an exception for wrong player
        act.Should().Throw<ArgumentException>()
            .WithMessage($"*{otherVillager.Id}*not in the list of players that can be assigned roles*");

        MarkTestCompleted();
    }

    #endregion
}
