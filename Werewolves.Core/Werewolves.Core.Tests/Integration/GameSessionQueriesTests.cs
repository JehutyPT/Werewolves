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

    #region TryGetOnlyPossibleUnassignedRole

    [Fact]
    public void TryGetOnlyPossibleUnassignedRole_RequiredCountZero_ReturnsFalse()
    {
        // Collective observation leaves exact Roles unassigned.
        // requiredAssignmentCount = 0 must still short-circuit to false.
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
        // Collective observation leaves all five exact Roles unassigned across
        // multiple Role types, so the query cannot infer one possible Role.
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
        // 5 players (1 WW, 1 Seer, 3 V). The Werewolf collective observation
        // does not identify an exact Role, so arrange that independent fact
        // explicitly; the Seer is still identified through ordinary Night flow,
        // then its independent physical-card ownership is arranged explicitly.
        // That leaves only SimpleVillager among the unassigned Role copies.
        // requiredAssignmentCount = 2 matches the exact unassigned count.
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: true);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolf = players[0];
        var seer = players[1];
        builder.ArrangeKnownPhysicalRole(
            werewolf.Id,
            MainRoleType.SimpleWerewolf);

        builder.CompleteNightPhase(
            werewolfIds: [werewolf.Id],
            victimId: players[2].Id,
            seerId: seer.Id,
            seerTargetId: werewolf.Id);
        builder.ArrangeKnownPhysicalRole(seer.Id, MainRoleType.Seer);

        var result = GameSessionQueries.TryGetOnlyPossibleUnassignedRole(
            gameState, requiredAssignmentCount: 2, out var role);

        result.Should().BeTrue();
        role.Should().Be(MainRoleType.SimpleVillager);

        MarkTestCompleted();
    }

    [Fact]
    public void TryGetOnlyPossibleUnassignedRole_SingleTypeMoreCopiesThanRequired_ReturnsTrueWithRole()
    {
        // 5 players (1 WW, 4 V). The collective observation establishes
        // Faction Agent membership only, so arrange the independent exact Role
        // before exercising the unassigned-Role query.
        // requiredAssignmentCount = 1 is fewer than the 4 available copies.
        var builder = CreateBuilder()
            .WithSimpleGame(playerCount: 5, werewolfCount: 1, includeSeer: false);
        builder.StartGame();
        builder.ConfirmGameStart();

        var gameState = builder.GetGameState()!;
        var players = gameState.GetPlayers().ToList();
        var werewolf = players[0];
        builder.ArrangeKnownPhysicalRole(
            werewolf.Id,
            MainRoleType.SimpleWerewolf);

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
