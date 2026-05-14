using FluentAssertions;
using System.Collections.Immutable;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.GameLogic.Strategies;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public class HeadlessSimulationTests
{
	[Fact]
	public void FirstValidOptionStrategy_SelectsPlayersInSeatingOrder()
	{
		var gameService = new GameService();
		var startInstruction = gameService.StartNewGame(new GameSessionConfig(
			["Alice", "Bruno", "Clara", "Dinis", "Eva"],
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]));
		var session = gameService.GetGameStateView(startInstruction.GameGuid)!;
		var players = session.GetPlayers().ToList();
		var instruction = new SelectPlayersInstruction(
			[players[3].Id, players[1].Id, players[2].Id],
			NumberRangeConstraint.Exact(2),
			privateInstruction: "Select two players.");
		var strategy = new FirstValidOptionStrategy();

		var response = strategy.CreateResponse(instruction, session);

		response.SelectedPlayerIds.Should().BeEquivalentTo([players[1].Id, players[2].Id]);
	}

	[Fact]
	public void FirstValidOptionStrategy_AssignsRolesToPlayersInSeatingOrder()
	{
		var gameService = new GameService();
		var startInstruction = gameService.StartNewGame(new GameSessionConfig(
			["Alice", "Bruno", "Clara", "Dinis", "Eva"],
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]));
		var session = gameService.GetGameStateView(startInstruction.GameGuid)!;
		var players = session.GetPlayers().ToList();
		var instruction = new AssignRolesInstruction(
			ImmutableHashSet.Create(players[4].Id, players[2].Id),
			[MainRoleType.SimpleVillager, MainRoleType.Seer],
			privateInstruction: "Assign roles.");
		var strategy = new FirstValidOptionStrategy();

		var response = strategy.CreateResponse(instruction, session);

		response.AssignedPlayerRoles.Should().ContainKey(players[2].Id)
			.WhoseValue.Should().Be(MainRoleType.SimpleVillager);
		response.AssignedPlayerRoles.Should().ContainKey(players[4].Id)
			.WhoseValue.Should().Be(MainRoleType.Seer);
	}

	[Fact]
	public void HeadlessGameDriver_CompletesApprovedSpikeComposition()
	{
		var config = GameBenchmarkHarness.CreateSpikeConfig();
		var driver = new HeadlessGameDriver(new FirstValidOptionStrategy());

		var result = driver.CompleteGame(config);

		result.IsFinished.Should().BeTrue();
		result.TurnCount.Should().BeGreaterThan(0);
		result.ProcessedInstructionCount.Should().BeGreaterThan(0);
		result.VictoryDescription.Should().NotBeNullOrWhiteSpace();
	}

	[Fact]
	public void CreateSpikeConfig_UsesApprovedSupportedComposition()
	{
		var config = GameBenchmarkHarness.CreateSpikeConfig();

		config.Players.Should().HaveCount(15);
		config.Roles.Should().HaveCount(15);
		config.Roles.Count(role => role == MainRoleType.SimpleWerewolf).Should().Be(3);
		config.Roles.Count(role => role == MainRoleType.Seer).Should().Be(1);
		config.Roles.Count(role => role == MainRoleType.WildChild).Should().Be(1);
		config.Roles.Count(role => role == MainRoleType.SimpleVillager).Should().Be(10);
	}

	[Fact]
	public void GameBenchmarkHarness_RunsRequestedGameCountAndReportsMetrics()
	{
		var harness = GameBenchmarkHarness.CreateDefault();

		var result = harness.Run(gameCount: 8, degreeOfParallelism: 2);

		result.GameCount.Should().Be(8);
		result.DegreeOfParallelism.Should().Be(2);
		result.TotalElapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
		result.AverageElapsedPerGame.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
		result.MinElapsedPerGame.Should().BeGreaterThanOrEqualTo(TimeSpan.Zero);
		result.MaxElapsedPerGame.Should().BeGreaterThanOrEqualTo(result.MinElapsedPerGame);
		result.TurnCounts.Should().HaveCount(8);
		result.TurnCounts.Should().OnlyContain(turnCount => turnCount > 0);
		result.GcCollections.Gen0.Should().BeGreaterThanOrEqualTo(0);
		result.GcCollections.Gen1.Should().BeGreaterThanOrEqualTo(0);
		result.GcCollections.Gen2.Should().BeGreaterThanOrEqualTo(0);
	}

	[Fact]
	public void GameBenchmarkHarness_DefaultsToOneThousandGamesAcrossTwoWorkers()
	{
		GameBenchmarkHarness.DefaultGameCount.Should().Be(1_000);
		GameBenchmarkHarness.DefaultDegreeOfParallelism.Should().Be(2);
	}
}
