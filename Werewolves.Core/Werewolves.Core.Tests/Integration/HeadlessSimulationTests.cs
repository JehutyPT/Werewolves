using FluentAssertions;
using System.Collections.Immutable;
using System.Text.Json;
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
	public void BaselineRandomDecisionStrategy_WithSameShapeUnadmittedSemantic_RejectsInstruction()
	{
		var material = CreateRunSeedMaterial(runNumber: 7);
		var startState = SimulationStartStateDeriver.Derive(material);
		var config = startState.CreateGameSessionConfig();
		var builder = CreateBuilder()
			.WithPlayers(config.Players.ToArray())
			.WithRoles(config.Roles.ToArray());
		builder.StartGame();
		var session = builder.GetGameState()!;
		var policy = new HeadlessResponsePolicy(
			BaselineRandomDecisionStrategy.Identity,
			[ModeratorInstructionSemantic.StartNight]);
		var admitted = new ConfirmationInstruction(
			ModeratorInstructionSemantic.StartNight,
			privateInstruction: "Same response shape.");
		var unadmitted = new ConfirmationInstruction(
			ModeratorInstructionSemantic.FinishNightActions,
			privateInstruction: "Same response shape.");
		var strategy = new BaselineRandomDecisionStrategy(material, startState, policy);

		var admittedResponse = strategy.CreateResponse(admitted, session);
		var act = () => strategy.CreateResponse(unadmitted, session);

		admittedResponse.Type.Should().Be(ExpectedInputType.Continue);
		admittedResponse.InstructionId.Should().Be(admitted.InstructionId);
		act.Should().Throw<NotSupportedException>();
		MarkTestCompleted();
	}

	[Fact]
	public void BaselineRandomPolicy_DeclaresStableIdentityAndExactInstructionSemantics()
	{
		BaselineRandomDecisionStrategy.Policy.StrategyIdentity.ToString()
			.Should().Be("baseline-random@1-splitmix64");
		BaselineRandomDecisionStrategy.Policy.AdmittedSemantics.Should().BeEquivalentTo(
		[
			ModeratorInstructionSemantic.StartGame,
			ModeratorInstructionSemantic.FinishedGame,
			ModeratorInstructionSemantic.StartNight,
			ModeratorInstructionSemantic.FinishNightActions,
			ModeratorInstructionSemantic.WakeRole,
			ModeratorInstructionSemantic.IdentifyRoleHolders,
			ModeratorInstructionSemantic.PutRoleToSleep,
			ModeratorInstructionSemantic.SelectWerewolfVictim,
			ModeratorInstructionSemantic.SelectSeerTarget,
			ModeratorInstructionSemantic.RevealSeerResult,
			ModeratorInstructionSemantic.SelectWildChildModel,
			ModeratorInstructionSemantic.AnnounceDawnVictims,
			ModeratorInstructionSemantic.AssignDawnVictimRoles,
			ModeratorInstructionSemantic.StartDayDebate,
			ModeratorInstructionSemantic.RecordDayVote,
			ModeratorInstructionSemantic.AssignDayVoteTargetRole,
			ModeratorInstructionSemantic.AnnounceLynchingImmunity,
			ModeratorInstructionSemantic.AnnounceDayElimination
		]);
	}

	[Fact]
	public void HeadlessResponsePolicy_SnapshotsItsAdmittedSemantics()
	{
		var source = new HashSet<ModeratorInstructionSemantic>
		{
			ModeratorInstructionSemantic.StartGame
		};
		var policy = new HeadlessResponsePolicy(
			BaselineRandomDecisionStrategy.Identity,
			source);

		source.Clear();

		policy.Admits(ModeratorInstructionSemantic.StartGame).Should().BeTrue();
		policy.AdmittedSemantics.Should().ContainSingle();
	}

	[Fact]
	public void ModeratorInstructionSemantic_IsObservableWithoutChangingJsonWireShape()
	{
		var instruction = new StartGameConfirmationInstruction(Guid.NewGuid());

		var json = JsonSerializer.Serialize(instruction);
		using var document = JsonDocument.Parse(json);

		instruction.Semantic.Should().Be(ModeratorInstructionSemantic.StartGame);
		document.RootElement.TryGetProperty(nameof(ModeratorInstruction.Semantic), out _)
			.Should().BeFalse();
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
				SimulatorProfile.LegacyCore.Identity),
			BaselineRandomDecisionStrategy.Identity,
			runNumber: 3);
		var startState = SimulationStartStateDeriver.Derive(material);
		var config = startState.CreateGameSessionConfig();
		var builder = CreateBuilder()
			.WithPlayers(config.Players.ToArray())
			.WithRoles(config.Roles.ToArray());
		var startInstruction = builder.StartGame();
		var session = builder.GetGameState()!;
		var strategy = new BaselineRandomDecisionStrategy(
			material,
			startState,
			BaselineRandomDecisionStrategy.Policy);
		var players = session.GetPlayers().ToList();
		var seerSeat = startState.RoleAssignments.Single(assignment => assignment.Role == MainRoleType.Seer).SeatNumber;
		var identifySeer = new SelectPlayersInstruction(
			ModeratorInstructionSemantic.IdentifyRoleHolders,
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
		var firstStrategy = new BaselineRandomDecisionStrategy(
			material,
			startState,
			BaselineRandomDecisionStrategy.Policy);
		var replayStrategy = new BaselineRandomDecisionStrategy(
			material,
			startState,
			BaselineRandomDecisionStrategy.Policy);
		var players = session.GetPlayers().ToList();

		var firstPlayerSelection = new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectWerewolfVictim,
			players.Select(player => player.Id).ToHashSet(),
			NumberRangeConstraint.Exact(2),
			privateInstruction: GameStrings.RevealRolePromptSpecify);
		var replayPlayerSelection = new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectWerewolfVictim,
			players.Select(player => player.Id).ToHashSet(),
			NumberRangeConstraint.Exact(2),
			privateInstruction: GameStrings.RevealRolePromptSpecify);
		var firstAssignment = new AssignRolesInstruction(
			ModeratorInstructionSemantic.AssignDawnVictimRoles,
			ImmutableHashSet.Create(players[1].Id, players[3].Id),
			[MainRoleType.Seer, MainRoleType.SimpleVillager],
			privateInstruction: GameStrings.RevealRolePromptSpecify);
		var replayAssignment = new AssignRolesInstruction(
			ModeratorInstructionSemantic.AssignDawnVictimRoles,
			ImmutableHashSet.Create(players[1].Id, players[3].Id),
			[MainRoleType.Seer, MainRoleType.SimpleVillager],
			privateInstruction: GameStrings.RevealRolePromptSpecify);
		var selected = firstStrategy.CreateResponse(firstPlayerSelection, session);
		var replaySelected = replayStrategy.CreateResponse(replayPlayerSelection, session);
		var assigned = firstStrategy.CreateResponse(firstAssignment, session);
		var replayAssigned = replayStrategy.CreateResponse(replayAssignment, session);

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
		var players = session.GetPlayers().ToList();
		var instruction = new SelectPlayersInstruction(
			ModeratorInstructionSemantic.RecordDayVote,
			[players[0].Id],
			NumberRangeConstraint.SingleOptional,
			privateInstruction: GameStrings.RevealRolePromptSpecify);
		var strategy = new BaselineRandomDecisionStrategy(
			material,
			startState,
			BaselineRandomDecisionStrategy.Policy);

		var response = strategy.CreateResponse(instruction, session);

		response.SelectedPlayerIds.Should().BeEmpty();
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
				SimulatorProfile.LegacyCore.Identity),
			BaselineRandomDecisionStrategy.Identity,
			runNumber);
	}
}
