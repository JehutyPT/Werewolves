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
}
