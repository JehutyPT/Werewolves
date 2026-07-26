using System.Collections.Immutable;
using FluentAssertions;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.GameLogic.Strategies;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.Tests.Helpers;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public class ModeratorDecisionStrategySemanticTraceTests
{
	[Fact]
	public void BaselineRandom_ProductionCursor_PreservesLiteralUpstreamSemanticSequence()
	{
		var fixture = CreateFixture(runNumber: 11);
		var players = fixture.Session.GetPlayers().ToArray();
		var identifySeer = new SelectPlayersInstruction(
			players.Select(player => player.Id).ToHashSet(),
			NumberRangeConstraint.Single,
			publicAnnouncement: null,
			privateInstruction: nameof(BaselineRandom_ProductionCursor_PreservesLiteralUpstreamSemanticSequence),
			affectedPlayerIds: null,
			roleIdentification: MainRoleType.Seer);
		var exactTwo = new SelectPlayersInstruction(
			players.Select(player => player.Id).ToHashSet(),
			NumberRangeConstraint.Exact(2),
			privateInstruction: nameof(BaselineRandom_ProductionCursor_PreservesLiteralUpstreamSemanticSequence));
		var assignRoles = new AssignRolesInstruction(
			ImmutableHashSet.Create(players[1].Id, players[3].Id),
			[MainRoleType.Seer, MainRoleType.SimpleVillager],
			privateInstruction: nameof(BaselineRandom_ProductionCursor_PreservesLiteralUpstreamSemanticSequence));

		var identificationResponse = fixture.Strategy.CreateResponse(identifySeer, fixture.Session);
		var selectionResponse = fixture.Strategy.CreateResponse(exactTwo, fixture.Session);
		var assignmentResponse = fixture.Strategy.CreateResponse(assignRoles, fixture.Session);

		fixture.StartState.RoleAssignments.Select(assignment => assignment.Role).Should().Equal(
			MainRoleType.WildChild,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.Seer);
		ToSeatNumbers(identificationResponse.SelectedPlayerIds!, players).Should().Equal(5);
		ToSeatNumbers(selectionResponse.SelectedPlayerIds!, players).Should().Equal(2, 5);
		assignmentResponse.AssignedPlayerRoles.Should().ContainKey(players[1].Id)
			.WhoseValue.Should().Be(MainRoleType.Seer);
		assignmentResponse.AssignedPlayerRoles.Should().ContainKey(players[3].Id)
			.WhoseValue.Should().Be(MainRoleType.SimpleVillager);
	}

	[Fact]
	public void BaselineRandom_ProductionCursor_WithTieThenOption_PreservesLiteralSemanticTrace()
	{
		var fixture = CreateFixture(runNumber: 4);
		var players = fixture.Session.GetPlayers().ToArray();
		var vote = new SelectPlayersInstruction(
			players.Select(player => player.Id).ToHashSet(),
			NumberRangeConstraint.SingleOptional,
			privateInstruction: nameof(BaselineRandom_ProductionCursor_WithTieThenOption_PreservesLiteralSemanticTrace));
		var options = new SelectOptionsInstruction(
			[
				new ModeratorOption("alpha", "Primeira"),
				new ModeratorOption("beta", "Segunda"),
				new ModeratorOption("gamma", "Terceira")
			],
			NumberRangeConstraint.SingleOptional,
			privateInstruction: nameof(BaselineRandom_ProductionCursor_WithTieThenOption_PreservesLiteralSemanticTrace));

		var continueResponse = fixture.Strategy.CreateResponse(fixture.StartInstruction, fixture.Session);
		var voteResponse = fixture.Strategy.CreateResponse(vote, fixture.Session);
		var optionResponse = fixture.Strategy.CreateResponse(options, fixture.Session);

		var trace = new[]
		{
			continueResponse.Type.ToString(),
			voteResponse.SelectedPlayerIds!.Count == 0 ? "Vote:Tie" : "Vote:Target",
			$"Option:{string.Join(",", optionResponse.SelectedOptionIds!)}"
		};
		trace.Should().Equal(
			ExpectedInputType.Continue.ToString(),
			"Vote:Tie",
			"Option:alpha");
		continueResponse.InstructionId.Should().Be(fixture.StartInstruction.InstructionId);
		voteResponse.InstructionId.Should().Be(vote.InstructionId);
		optionResponse.InstructionId.Should().Be(options.InstructionId);
	}

	[Fact]
	public void BaselineRandom_ExplicitSemanticOrder_DrivesTargetAndOptionTraceIndependentOfLabels()
	{
		var duplicateLabelTrace = ExecuteTrace(
			CreateFixture(runNumber: 11),
			[
				new ModeratorOption("alpha", "Mesmo rótulo"),
				new ModeratorOption("beta", "Mesmo rótulo"),
				new ModeratorOption("gamma", "Outro rótulo")
			]);
		var relabeledTrace = ExecuteTrace(
			CreateFixture(runNumber: 11),
			[
				new ModeratorOption("alpha", "A"),
				new ModeratorOption("beta", "B"),
				new ModeratorOption("gamma", "C")
			]);
		var reorderedTrace = ExecuteTrace(
			CreateFixture(runNumber: 11),
			[
				new ModeratorOption("gamma", "Outro rótulo"),
				new ModeratorOption("alpha", "Mesmo rótulo"),
				new ModeratorOption("beta", "Mesmo rótulo")
			]);

		duplicateLabelTrace.Should().Equal(
			ExpectedInputType.Continue.ToString(),
			"Vote:Seat2",
			"Option:beta");
		relabeledTrace.Should().Equal(duplicateLabelTrace);
		reorderedTrace.Should().Equal(
			ExpectedInputType.Continue.ToString(),
			"Vote:Seat2",
			"Option:alpha");
	}

	[Fact]
	public void BaselineRandom_UnsupportedInstruction_FailsWithoutGuessingOrAdvancingCursor()
	{
		var attemptedFixture = CreateFixture(runNumber: 11);
		var replayFixture = CreateFixture(runNumber: 11);
		var options = new[]
		{
			new ModeratorOption("alpha", "A"),
			new ModeratorOption("beta", "B"),
			new ModeratorOption("gamma", "C")
		};
		var attemptedInstruction = new SelectOptionsInstruction(
			options,
			NumberRangeConstraint.SingleOptional,
			privateInstruction: nameof(BaselineRandom_UnsupportedInstruction_FailsWithoutGuessingOrAdvancingCursor));
		var replayInstruction = new SelectOptionsInstruction(
			options,
			NumberRangeConstraint.SingleOptional,
			privateInstruction: nameof(BaselineRandom_UnsupportedInstruction_FailsWithoutGuessingOrAdvancingCursor));

		Action respondToUnsupported = () => attemptedFixture.Strategy.CreateResponse(
			new UnsupportedInstruction(),
			attemptedFixture.Session);

		respondToUnsupported.Should().Throw<NotSupportedException>()
			.WithMessage("*UnsupportedInstruction*");
		var responseAfterFailure = attemptedFixture.Strategy.CreateResponse(
			attemptedInstruction,
			attemptedFixture.Session);
		var replayResponse = replayFixture.Strategy.CreateResponse(
			replayInstruction,
			replayFixture.Session);
		responseAfterFailure.SelectedOptionIds.Should().Equal("beta");
		responseAfterFailure.SelectedOptionIds.Should().Equal(replayResponse.SelectedOptionIds);
	}

	[Fact]
	public void FirstValidOption_UsesOneWayContinueAndFirstSemanticOptionInExplicitOrder()
	{
		var fixture = CreateFixture(runNumber: 0);
		var strategy = new FirstValidOptionStrategy();
		var options = new SelectOptionsInstruction(
			[
				new ModeratorOption("gamma", "Mesmo rótulo"),
				new ModeratorOption("alpha", "Mesmo rótulo"),
				new ModeratorOption("beta", "Outro rótulo")
			],
			NumberRangeConstraint.SingleOptional,
			privateInstruction: nameof(FirstValidOption_UsesOneWayContinueAndFirstSemanticOptionInExplicitOrder));

		var continueResponse = strategy.CreateResponse(fixture.StartInstruction, fixture.Session);
		var optionResponse = strategy.CreateResponse(options, fixture.Session);

		continueResponse.Type.Should().Be(ExpectedInputType.Continue);
		continueResponse.InstructionId.Should().Be(fixture.StartInstruction.InstructionId);
		optionResponse.SelectedOptionIds.Should().Equal("gamma");
		optionResponse.InstructionId.Should().Be(options.InstructionId);
	}

	private static IReadOnlyList<string> ExecuteTrace(
		StrategyFixture fixture,
		IReadOnlyList<ModeratorOption> options)
	{
		var players = fixture.Session.GetPlayers().ToArray();
		var vote = new SelectPlayersInstruction(
			players.Select(player => player.Id).ToHashSet(),
			NumberRangeConstraint.SingleOptional,
			privateInstruction: nameof(ExecuteTrace));
		var optionInstruction = new SelectOptionsInstruction(
			options,
			NumberRangeConstraint.SingleOptional,
			privateInstruction: nameof(ExecuteTrace));

		var continueResponse = fixture.Strategy.CreateResponse(fixture.StartInstruction, fixture.Session);
		var voteResponse = fixture.Strategy.CreateResponse(vote, fixture.Session);
		var optionResponse = fixture.Strategy.CreateResponse(optionInstruction, fixture.Session);
		var selectedSeat = voteResponse.SelectedPlayerIds!.Count == 0
			? "Tie"
			: $"Seat{Array.FindIndex(
				players,
				player => voteResponse.SelectedPlayerIds.Contains(player.Id)) + 1}";

		return
		[
			continueResponse.Type.ToString(),
			$"Vote:{selectedSeat}",
			$"Option:{string.Join(",", optionResponse.SelectedOptionIds!)}"
		];
	}

	private static IReadOnlyList<int> ToSeatNumbers(
		IReadOnlySet<Guid> selectedPlayerIds,
		IReadOnlyList<IPlayer> players) =>
		selectedPlayerIds
			.Select(id => Enumerable.Range(0, players.Count)
				.Single(index => players[index].Id == id) + 1)
			.Order()
			.ToArray();

	private static StrategyFixture CreateFixture(long runNumber)
	{
		var scenario = new SimulationScenario(
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
			runNumber);
		var random = new DeterministicRandomSource(material);
		var startState = SimulationStartStateDeriver.Derive(material, random);
		var config = startState.CreateGameSessionConfig();
		var builder = GameTestBuilder.Create()
			.WithPlayers(config.Players.ToArray())
			.WithRoles(config.Roles.ToArray());
		var startInstruction = builder.StartGame();

		return new StrategyFixture(
			new BaselineRandomDecisionStrategy(material, startState, random),
			builder.GetGameState()!,
			startInstruction,
			startState);
	}

	private sealed record StrategyFixture(
		BaselineRandomDecisionStrategy Strategy,
		IGameSession Session,
		StartGameConfirmationInstruction StartInstruction,
		SimulationStartState StartState);

	private sealed record UnsupportedInstruction()
		: ModeratorInstruction(privateInstruction: nameof(UnsupportedInstruction));
}
