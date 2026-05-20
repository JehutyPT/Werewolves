using FluentAssertions;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

/// <summary>
/// Tests for rule-specific queries over game session logs.
/// </summary>
public class GameSessionQueriesTests : DiagnosticTestBase
{
    public GameSessionQueriesTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public void GetPlayersTargetedLastNight_ReturnsPlayersFromCurrentNightActionLog()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolf = players[0];
        var victim = players[2];

        builder.CompleteNightPhase(
            werewolfIds: [werewolf.Id],
            victimId: victim.Id,
            seerId: players[1].Id,
            seerTargetId: werewolf.Id);

        var targetedPlayers = GameSessionQueries.GetPlayersTargetedLastNight(
            gameState,
            NightActionType.WerewolfVictimSelection,
            NumberRangeConstraint.Single);

        targetedPlayers.Should().ContainSingle()
            .Which.Id.Should().Be(victim.Id);

        MarkTestCompleted();
    }

    [Fact]
    public void GetNightActionMap_GroupsCurrentNightActionsByTargetPlayer()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolf = players[0];
        var seer = players[1];
        var victim = players[2];

        builder.CompleteNightPhase(
            werewolfIds: [werewolf.Id],
            victimId: victim.Id,
            seerId: seer.Id,
            seerTargetId: werewolf.Id);

        var actionMap = GameSessionQueries.GetNightActionMap(
            gameState,
            [
                NightActionType.WerewolfVictimSelection,
                NightActionType.SeerCheck
            ]);

        actionMap[victim.Id].Should().Contain(NightActionType.WerewolfVictimSelection);
        actionMap[werewolf.Id].Should().Contain(NightActionType.SeerCheck);

        MarkTestCompleted();
    }

    #region TryGetOnlyPossibleUnassignedRole

    [Fact]
    public void TryGetOnlyPossibleUnassignedRole_RequiredCountZero_ReturnsFalse()
    {
        // After night identification, werewolf role is assigned.
        // Unassigned = 4x SimpleVillager (single type, sufficient copies).
        // Even so, requiredAssignmentCount = 0 should short-circuit to false.
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: false);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();

        builder.CompleteNightPhase(
            werewolfIds: [players[0].Id],
            victimId: players[1].Id);

        var result = GameSessionQueries.TryGetOnlyPossibleUnassignedRole(
            gameState, requiredAssignmentCount: 0, out var role);

        result.Should().BeFalse();
        role.Should().Be(default(MainRoleType));

        MarkTestCompleted();
    }

    [Fact]
    public void TryGetOnlyPossibleUnassignedRole_RequiredCountNegative_ReturnsFalse()
    {
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: false);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();

        builder.CompleteNightPhase(
            werewolfIds: [players[0].Id],
            victimId: players[1].Id);

        var result = GameSessionQueries.TryGetOnlyPossibleUnassignedRole(
            gameState, requiredAssignmentCount: -1, out var role);

        result.Should().BeFalse();
        role.Should().Be(default(MainRoleType));

        MarkTestCompleted();
    }

    [Fact]
    public void TryGetOnlyPossibleUnassignedRole_FewerUnassignedThanRequired_ReturnsFalse()
    {
        // 5 players (1 WW, 4 V). After night identification, WW is assigned.
        // Unassigned = 4x SimpleVillager. Asking for 5 exceeds the count.
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: false);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();

        builder.CompleteNightPhase(
            werewolfIds: [players[0].Id],
            victimId: players[1].Id);

        var result = GameSessionQueries.TryGetOnlyPossibleUnassignedRole(
            gameState, requiredAssignmentCount: 5, out var role);

        result.Should().BeFalse();

        MarkTestCompleted();
    }

    [Fact]
    public void TryGetOnlyPossibleUnassignedRole_MultipleDistinctTypes_ReturnsFalse()
    {
        // 5 players (1 WW, 1 Seer, 3 V). Before night identification, all 5 roles
        // are unassigned with 3 distinct types: WW, Seer, V.
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;

        var result = GameSessionQueries.TryGetOnlyPossibleUnassignedRole(
            gameState, requiredAssignmentCount: 1, out var role);

        result.Should().BeFalse();

        MarkTestCompleted();
    }

    [Fact]
    public void TryGetOnlyPossibleUnassignedRole_SingleTypeExactCount_ReturnsTrueWithRole()
    {
        // 5 players (1 WW, 1 Seer, 3 V). After the night phase the WW and Seer
        // roles are assigned via identification, and the dawn victim is auto-assigned
        // SimpleVillager. That leaves unassigned = 2x SimpleVillager (single type).
        // requiredAssignmentCount = 2 matches the exact unassigned count.
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolf = players[0];
        var seer = players[1];

        builder.CompleteNightPhase(
            werewolfIds: [werewolf.Id],
            victimId: players[2].Id,
            seerId: seer.Id,
            seerTargetId: werewolf.Id);

        var result = GameSessionQueries.TryGetOnlyPossibleUnassignedRole(
            gameState, requiredAssignmentCount: 2, out var role);

        result.Should().BeTrue();
        role.Should().Be(MainRoleType.SimpleVillager);

        MarkTestCompleted();
    }

    [Fact]
    public void TryGetOnlyPossibleUnassignedRole_SingleTypeMoreCopiesThanRequired_ReturnsTrueWithRole()
    {
        // 5 players (1 WW, 4 V). After night identification the WW role is assigned,
        // leaving unassigned = 4x SimpleVillager (single type).
        // requiredAssignmentCount = 1 is fewer than the 4 available copies.
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: false);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolf = players[0];

        builder.CompleteNightPhase(
            werewolfIds: [werewolf.Id],
            victimId: players[1].Id);

        var result = GameSessionQueries.TryGetOnlyPossibleUnassignedRole(
            gameState, requiredAssignmentCount: 1, out var role);

        result.Should().BeTrue();
        role.Should().Be(MainRoleType.SimpleVillager);

        MarkTestCompleted();
    }

    #endregion
}
