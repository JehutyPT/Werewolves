using FluentAssertions;
using System.Collections.Immutable;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.GameLogic.Strategies;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.StateModels.Resources;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public class HeadlessSimulationTests : DiagnosticTestBase
{
	public HeadlessSimulationTests(ITestOutputHelper output) : base(output)
	{
	}

	[Fact]
	public void BaselineRandomDecisionStrategy_WithRoleIdentification_UsesSeededAssignmentAndAcknowledgesConfirmation()
	{
		var scenario = new StateModels.Models.Simulation.SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.WildChild,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var material = new RunSeedMaterial(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorProfile.Active.Identity),
			BaselineRandomDecisionStrategy.Identity,
			runNumber: 3);
		var startState = SimulationStartStateDeriver.Derive(material);
		var config = startState.CreateGameSessionConfig();
		var builder = CreateBuilder()
			.WithPlayers(config.Players.ToArray())
			.WithRoles(config.Roles.ToArray());
		var startInstruction = builder.StartGame();
		var session = builder.GetGameState()!;
		var strategy = new BaselineRandomDecisionStrategy(material, startState);
		var players = session.GetPlayers().ToList();
		var seerSeat = startState.RoleAssignments.Single(assignment => assignment.Role == MainRoleType.Seer).SeatNumber;
		var identifySeer = new SelectPlayersInstruction(
			players.Select(player => player.Id).ToHashSet(),
			NumberRangeConstraint.Single,
			publicAnnouncement: null,
			privateInstruction: GameStrings.RevealRolePromptSpecify,
			affectedPlayerIds: null,
			roleIdentification: MainRoleType.Seer);

		var confirmation = strategy.CreateResponse(startInstruction, session);
		var identification = strategy.CreateResponse(identifySeer, session);

		confirmation.Type.Should().Be(ExpectedInputType.Continue);
		confirmation.InstructionId.Should().Be(startInstruction.InstructionId);
		identification.SelectedPlayerIds.Should().Equal(players[seerSeat - 1].Id);
		MarkTestCompleted();
	}

	[Fact]
	public void BaselineRandomDecisionStrategy_WithChoiceInstructions_ReturnsCompleteValidDeterministicResponses()
	{
		var material = CreateRunSeedMaterial(runNumber: 11);
		var startState = SimulationStartStateDeriver.Derive(material);
		var config = startState.CreateGameSessionConfig();
		var builder = CreateBuilder()
			.WithPlayers(config.Players.ToArray())
			.WithRoles(config.Roles.ToArray());
		builder.StartGame();
		var session = builder.GetGameState()!;
		var firstStrategy = new BaselineRandomDecisionStrategy(material, startState);
		var replayStrategy = new BaselineRandomDecisionStrategy(material, startState);
		var players = session.GetPlayers().ToList();

		var firstPlayerSelection = new SelectPlayersInstruction(
			players.Select(player => player.Id).ToHashSet(),
			NumberRangeConstraint.Exact(2),
			privateInstruction: GameStrings.RevealRolePromptSpecify);
		var replayPlayerSelection = new SelectPlayersInstruction(
			players.Select(player => player.Id).ToHashSet(),
			NumberRangeConstraint.Exact(2),
			privateInstruction: GameStrings.RevealRolePromptSpecify);
		var firstAssignment = new AssignRolesInstruction(
			ImmutableHashSet.Create(players[1].Id, players[3].Id),
			[MainRoleType.Seer, MainRoleType.SimpleVillager],
			privateInstruction: GameStrings.RevealRolePromptSpecify);
		var replayAssignment = new AssignRolesInstruction(
			ImmutableHashSet.Create(players[1].Id, players[3].Id),
			[MainRoleType.Seer, MainRoleType.SimpleVillager],
			privateInstruction: GameStrings.RevealRolePromptSpecify);
		var firstOptions = new SelectOptionsInstruction(
			[
				new ModeratorOption("alpha", "Alpha"),
				new ModeratorOption("beta", "Beta"),
				new ModeratorOption("gamma", "Gamma")
			],
			NumberRangeConstraint.SingleOptional,
			privateInstruction: GameStrings.RevealRolePromptSpecify);
		var replayOptions = new SelectOptionsInstruction(
			[
				new ModeratorOption("alpha", "First"),
				new ModeratorOption("beta", "Second"),
				new ModeratorOption("gamma", "Third")
			],
			NumberRangeConstraint.SingleOptional,
			privateInstruction: GameStrings.RevealRolePromptSpecify);

		var selected = firstStrategy.CreateResponse(firstPlayerSelection, session);
		var replaySelected = replayStrategy.CreateResponse(replayPlayerSelection, session);
		var assigned = firstStrategy.CreateResponse(firstAssignment, session);
		var replayAssigned = replayStrategy.CreateResponse(replayAssignment, session);
		var options = firstStrategy.CreateResponse(firstOptions, session);
		var replayOptionResponse = replayStrategy.CreateResponse(replayOptions, session);

		selected.SelectedPlayerIds.Should().HaveCount(2)
			.And.BeSubsetOf(firstPlayerSelection.SelectablePlayerIds);
		selected.SelectedPlayerIds!.Select(id => players.FindIndex(player => player.Id == id))
			.Should().BeEquivalentTo(
				replaySelected.SelectedPlayerIds!.Select(id => players.FindIndex(player => player.Id == id)));
		assigned.AssignedPlayerRoles.Should().HaveCount(2);
		assigned.AssignedPlayerRoles!.Keys.Should().BeEquivalentTo(firstAssignment.PlayersForAssignment);
		assigned.AssignedPlayerRoles.Values.Should().BeEquivalentTo(firstAssignment.RolesForAssignment);
		assigned.AssignedPlayerRoles.OrderBy(pair => players.FindIndex(player => player.Id == pair.Key)).Select(pair => pair.Value)
			.Should().Equal(
				replayAssigned.AssignedPlayerRoles!.OrderBy(pair => players.FindIndex(player => player.Id == pair.Key)).Select(pair => pair.Value));
		options.SelectedOptionIds.Should().BeSubsetOf(firstOptions.Options.Select(option => option.Id));
		firstOptions.SelectionRange.IsValid(options.SelectedOptionIds!.ToList()).Should().BeTrue();
		options.SelectedOptionIds.Should().Equal(replayOptionResponse.SelectedOptionIds);
		MarkTestCompleted();
	}

	[Fact]
	public void BaselineRandomDecisionStrategy_WithKnownOptionalChoiceSeed_ReturnsEmptyValidResponse()
	{
		var material = CreateRunSeedMaterial(runNumber: 0);
		var startState = SimulationStartStateDeriver.Derive(material);
		var config = startState.CreateGameSessionConfig();
		var builder = CreateBuilder()
			.WithPlayers(config.Players.ToArray())
			.WithRoles(config.Roles.ToArray());
		builder.StartGame();
		var session = builder.GetGameState()!;
		var instruction = new SelectOptionsInstruction(
			[new ModeratorOption("alpha", "Alpha")],
			NumberRangeConstraint.SingleOptional,
			privateInstruction: GameStrings.RevealRolePromptSpecify);
		var strategy = new BaselineRandomDecisionStrategy(material, startState);

		var response = strategy.CreateResponse(instruction, session);

		response.SelectedOptionIds.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void FirstValidOptionStrategy_SelectsPlayersInSeatingOrder()
	{
		var builder = CreateBuilder()
			.WithPlayers("Alice", "Bruno", "Clara", "Dinis", "Eva")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToList();
		var instruction = new SelectPlayersInstruction(
			[players[3].Id, players[1].Id, players[2].Id],
			NumberRangeConstraint.Exact(2),
			privateInstruction: GameStrings.WerewolvesChooseVictimPrompt);
		var strategy = new FirstValidOptionStrategy();

		var response = strategy.CreateResponse(instruction, session);

		response.SelectedPlayerIds.Should().BeEquivalentTo(
			new[] { players[1].Id, players[2].Id });
		MarkTestCompleted();
	}

	[Fact]
	public void FirstValidOptionStrategy_AssignsRolesToPlayersInSeatingOrder()
	{
		var builder = CreateBuilder()
			.WithPlayers("Alice", "Bruno", "Clara", "Dinis", "Eva")
			.WithRoles(
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
		builder.StartGame();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToList();
		var instruction = new AssignRolesInstruction(
			ImmutableHashSet.Create(players[4].Id, players[2].Id),
			[MainRoleType.SimpleVillager, MainRoleType.Seer],
			privateInstruction: GameStrings.RevealRolePromptSpecify);
		var strategy = new FirstValidOptionStrategy();

		var response = strategy.CreateResponse(instruction, session);

		response.AssignedPlayerRoles.Should().ContainKey(players[2].Id)
			.WhoseValue.Should().Be(MainRoleType.SimpleVillager);
		response.AssignedPlayerRoles.Should().ContainKey(players[4].Id)
			.WhoseValue.Should().Be(MainRoleType.Seer);
		MarkTestCompleted();
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
		MarkTestCompleted();
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
		MarkTestCompleted();
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
		MarkTestCompleted();
	}

	[Fact]
	public void GameBenchmarkHarness_DefaultsToOneThousandGamesAcrossTwoWorkers()
	{
		GameBenchmarkHarness.DefaultGameCount.Should().Be(1_000);
		GameBenchmarkHarness.DefaultDegreeOfParallelism.Should().Be(2);
		MarkTestCompleted();
	}

	private static RunSeedMaterial CreateRunSeedMaterial(long runNumber)
	{
		var scenario = new StateModels.Models.Simulation.SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.WildChild,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

		return new RunSeedMaterial(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorProfile.Active.Identity),
			BaselineRandomDecisionStrategy.Identity,
			runNumber);
	}
}
