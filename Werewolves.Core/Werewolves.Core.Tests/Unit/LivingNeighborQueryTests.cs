using FluentAssertions;
using Werewolves.Core.GameLogic.Queries;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public sealed class LivingNeighborQueryTests
{
	[Fact]
	public void DirectionalLivingNeighbors_SkipsEliminatedPlayersAndWrapsOnce()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var reference = players[4];
		builder.ArrangeEliminatedPlayer(players[0].Id);
		builder.ArrangeEliminatedPlayer(players[3].Id);

		var neighbors = GameSessionQueries.GetDirectionalLivingNeighbors(
			session,
			reference.Id);

		neighbors.Clockwise.Should().Be(players[1]);
		neighbors.Counterclockwise.Should().Be(players[2]);
	}

	[Fact]
	public void DirectionalLivingNeighbors_OnlyLivingReferenceHasNoNeighbors()
	{
		var builder = CreateBuilder();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		foreach (var player in players.Skip(1))
		{
			builder.ArrangeEliminatedPlayer(player.Id);
		}

		var neighbors = GameSessionQueries.GetDirectionalLivingNeighbors(
			session,
			players[0].Id);

		neighbors.Clockwise.Should().BeNull();
		neighbors.Counterclockwise.Should().BeNull();
	}

	[Fact]
	public void DirectionalLivingNeighbors_OneOtherLivingPlayerOccupiesBothDirections()
	{
		var builder = CreateBuilder();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		foreach (var player in players.Skip(2))
		{
			builder.ArrangeEliminatedPlayer(player.Id);
		}

		var neighbors = GameSessionQueries.GetDirectionalLivingNeighbors(
			session,
			players[0].Id);

		neighbors.Clockwise.Should().Be(players[1]);
		neighbors.Counterclockwise.Should().Be(players[1]);
	}

	[Fact]
	public void DirectionalLivingNeighbors_ThreeLivingPlayersHaveDistinctDirections()
	{
		var builder = CreateBuilder();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		builder.ArrangeEliminatedPlayer(players[1].Id);
		builder.ArrangeEliminatedPlayer(players[3].Id);

		var neighbors = GameSessionQueries.GetDirectionalLivingNeighbors(
			session,
			players[2].Id);

		neighbors.Clockwise.Should().Be(players[4]);
		neighbors.Counterclockwise.Should().Be(players[0]);
	}

	[Fact]
	public void DirectionalLivingNeighbors_EliminatedReferenceStillResolvesLivingDirections()
	{
		var builder = CreateBuilder();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		builder.ArrangeEliminatedPlayer(players[0].Id);
		builder.ArrangeEliminatedPlayer(players[1].Id);

		var neighbors = GameSessionQueries.GetDirectionalLivingNeighbors(
			session,
			players[0].Id);

		neighbors.Clockwise.Should().Be(players[2]);
		neighbors.Counterclockwise.Should().Be(players[4]);
	}

	[Fact]
	public void DirectionalLivingNeighbors_RehydrationPreservesSeatingOrder()
	{
		var builder = CreateBuilder();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var expected = GameSessionQueries.GetDirectionalLivingNeighbors(
			session,
			players[4].Id);
		var freshService = new GameService();

		var recoveredGameId =
			freshService.RehydrateSession(session.Serialize());
		var recovered = freshService.GetGameStateView(recoveredGameId)!;
		var actual = GameSessionQueries.GetDirectionalLivingNeighbors(
			recovered,
			players[4].Id);

		actual.Clockwise!.Id.Should().Be(expected.Clockwise!.Id);
		actual.Counterclockwise!.Id.Should()
			.Be(expected.Counterclockwise!.Id);
	}

	private static GameTestBuilder CreateBuilder()
	{
		var builder = GameTestBuilder.Create()
			.WithPlayers(5)
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		return builder;
	}
}
