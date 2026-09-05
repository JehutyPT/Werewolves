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
			ModeratorInstructionSemantic.IdentifyRoleHolders,
			players.Select(player => player.Id).ToHashSet(),
			NumberRangeConstraint.Single,
			publicAnnouncement: null,
			privateInstruction: nameof(BaselineRandom_ProductionCursor_PreservesLiteralUpstreamSemanticSequence),
			affectedPlayerIds: null,
			roleIdentification: MainRoleType.Seer);
		var exactTwo = new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectWerewolfVictim,
			players.Select(player => player.Id).ToHashSet(),
			NumberRangeConstraint.Exact(2),
			privateInstruction: nameof(BaselineRandom_ProductionCursor_PreservesLiteralUpstreamSemanticSequence));
		var assignRoles = new AssignRolesInstruction(
			ModeratorInstructionSemantic.AssignDawnVictimRoles,
			ImmutableHashSet.Create(players[1].Id, players[3].Id),
			[MainRoleType.WildChild, MainRoleType.SimpleVillager],
			privateInstruction: nameof(BaselineRandom_ProductionCursor_PreservesLiteralUpstreamSemanticSequence));

		var identificationResponse = fixture.Strategy.CreateResponse(identifySeer, fixture.Session);
		var selectionResponse = fixture.Strategy.CreateResponse(exactTwo, fixture.Session);
		var assignmentResponse = fixture.Strategy.CreateResponse(assignRoles, fixture.Session);

		fixture.StartState.RoleAssignments.Select(assignment => assignment.Role).Should().Equal(
			MainRoleType.SimpleVillager,
			MainRoleType.WildChild,
			MainRoleType.Seer,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleWerewolf);
		ToSeatNumbers(identificationResponse.SelectedPlayerIds!, players).Should().Equal(3);
		ToSeatNumbers(selectionResponse.SelectedPlayerIds!, players).Should().Equal(2, 4);
		assignmentResponse.AssignedPlayerRoles.Should().ContainKey(players[1].Id)
			.WhoseValue.Should().Be(MainRoleType.WildChild);
		assignmentResponse.AssignedPlayerRoles.Should().ContainKey(players[3].Id)
			.WhoseValue.Should().Be(MainRoleType.SimpleVillager);
	}

	[Fact]
	public void BaselineRandom_ProductionCursor_WithOptionalVote_PreservesLiteralSemanticTrace()
	{
		var fixture = CreateFixture(runNumber: 4);
		var players = fixture.Session.GetPlayers().ToArray();
		var vote = new SelectPlayersInstruction(
			ModeratorInstructionSemantic.RecordDayVote,
			players.Select(player => player.Id).ToHashSet(),
			NumberRangeConstraint.SingleOptional,
			privateInstruction: nameof(BaselineRandom_ProductionCursor_WithOptionalVote_PreservesLiteralSemanticTrace));

		var continueResponse = fixture.Strategy.CreateResponse(fixture.StartInstruction, fixture.Session);
		var voteResponse = fixture.Strategy.CreateResponse(vote, fixture.Session);

		var trace = new[]
		{
			continueResponse.Type.ToString(),
			voteResponse.SelectedPlayerIds!.Count == 0 ? "Vote:Tie" : "Vote:Target"
		};
		trace.Should().Equal(
			ExpectedInputType.Continue.ToString(),
			"Vote:Target");
		continueResponse.InstructionId.Should().Be(fixture.StartInstruction.InstructionId);
		voteResponse.InstructionId.Should().Be(vote.InstructionId);
	}

	[Fact]
	public void BaselineRandom_WerewolfFactionAgentGroupObservation_UsesHiddenCurrentLivingSelectableAgentsWithoutAdvancingCursor()
	{
		MainRoleType[] roles =
		[
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		];
		var observedFixture = CreateFixture(runNumber: 11, roles);
		var replayFixture = CreateFixture(runNumber: 11, roles);
		var players = observedFixture.Session.GetPlayers().ToArray();
		var replayPlayers = replayFixture.Session.GetPlayers().ToArray();
		observedFixture.Builder.ConfirmGameStart().IsSuccess.Should().BeTrue();
		var observation =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				observedFixture.Builder.ConfirmNightStart());
		var expectedAgentIds = observedFixture.StartState.FactionFacts
			.Where(facts =>
				facts.GetAgentKnowledge(Faction.Werewolf) ==
				FactionAgentKnowledge.KnownAgent)
			.Select(facts => players[facts.SeatNumber - 1])
			.Where(player => player.State.Health == PlayerHealth.Alive)
			.Select(player => player.Id)
			.Where(observation.SelectablePlayerIds.Contains)
			.ToHashSet();
		var choiceAfterObservation = new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectWerewolfVictim,
			players.Skip(1).Select(player => player.Id).ToHashSet(),
			NumberRangeConstraint.Exact(2),
			privateInstruction:
				nameof(BaselineRandom_WerewolfFactionAgentGroupObservation_UsesHiddenCurrentLivingSelectableAgentsWithoutAdvancingCursor));
		var replayChoice = new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectWerewolfVictim,
			replayPlayers.Skip(1).Select(player => player.Id).ToHashSet(),
			NumberRangeConstraint.Exact(2),
			privateInstruction:
				nameof(BaselineRandom_WerewolfFactionAgentGroupObservation_UsesHiddenCurrentLivingSelectableAgentsWithoutAdvancingCursor));

		observedFixture.Session.TryGetKnownFactionAgents(
				Faction.Werewolf,
				out var partialAgents)
			.Should().BeFalse();
		partialAgents.Should().BeEmpty();
		observation.Semantic.Should().Be(
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup);
		observation.CountConstraint.Should().Be(
			NumberRangeConstraint.Exact(expectedAgentIds.Count));
		observedFixture.Session.GetFactionAgentKnowledge(
				expectedAgentIds.Single(),
				Faction.Werewolf)
			.Should().Be(FactionAgentKnowledge.Unknown);

		var observationResponse = observedFixture.Strategy.CreateResponse(
			observation,
			observedFixture.Session);
		observationResponse.InstructionId.Should().Be(observation.InstructionId);
		observationResponse.SelectedPlayerIds.Should().BeEquivalentTo(expectedAgentIds);
		observedFixture.Builder.Process(observationResponse).IsSuccess.Should().BeTrue();
		var responseAfterObservation = observedFixture.Strategy.CreateResponse(
			choiceAfterObservation,
			observedFixture.Session);
		var replayResponse = replayFixture.Strategy.CreateResponse(
			replayChoice,
			replayFixture.Session);

		ToSeatNumbers(responseAfterObservation.SelectedPlayerIds!, players).Should()
			.Equal(ToSeatNumbers(replayResponse.SelectedPlayerIds!, replayPlayers));
	}

	[Fact]
	public void BaselineRandom_UnsupportedInstruction_FailsWithoutGuessingOrAdvancingCursor()
	{
		var attemptedFixture = CreateFixture(runNumber: 11);
		var replayFixture = CreateFixture(runNumber: 11);
		var attemptedPlayers = attemptedFixture.Session.GetPlayers().ToArray();
		var replayPlayers = replayFixture.Session.GetPlayers().ToArray();
		var attemptedInstruction = new SelectPlayersInstruction(
			ModeratorInstructionSemantic.RecordDayVote,
			attemptedPlayers.Select(player => player.Id).ToHashSet(),
			NumberRangeConstraint.SingleOptional,
			privateInstruction: nameof(BaselineRandom_UnsupportedInstruction_FailsWithoutGuessingOrAdvancingCursor));
		var replayInstruction = new SelectPlayersInstruction(
			ModeratorInstructionSemantic.RecordDayVote,
			replayPlayers.Select(player => player.Id).ToHashSet(),
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
		ToSeatNumbers(responseAfterFailure.SelectedPlayerIds!, attemptedPlayers)
			.Should().Equal(ToSeatNumbers(replayResponse.SelectedPlayerIds!, replayPlayers));
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

	private static IReadOnlyList<int> ToSeatNumbers(
		IReadOnlySet<Guid> selectedPlayerIds,
		IReadOnlyList<IPlayer> players) =>
		selectedPlayerIds
			.Select(id => Enumerable.Range(0, players.Count)
				.Single(index => players[index].Id == id) + 1)
			.Order()
			.ToArray();

	private static StrategyFixture CreateFixture(
		long runNumber,
		IReadOnlyList<MainRoleType>? roles = null)
	{
		roles ??=
		[
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.WildChild,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		];
		var scenario = new SimulationScenario(
			roles.Count,
			roles);
		var material = new RunSeedMaterial(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorCapability.FullProbability.Identity),
			BaselineRandomDecisionStrategy.Identity,
			runNumber);
		var random = new DeterministicRandomSource(material);
		var startState = SimulationStartStateDeriver.Derive(
			material,
			SimulatorCapability.FullProbability,
			random);
		var config = startState.CreateGameSessionConfig();
		var builder = GameTestBuilder.Create()
			.WithPlayers(config.Players.ToArray())
			.WithRoles(config.Roles.ToArray());
		var startInstruction = builder.StartGame();

		return new StrategyFixture(
			new BaselineRandomDecisionStrategy(
				material,
				startState,
				SimulatorCapability.FullProbability.HeadlessResponsePolicy,
				random),
			builder.GetGameState()!,
			startInstruction,
			startState,
			builder);
	}

	private sealed record StrategyFixture(
		BaselineRandomDecisionStrategy Strategy,
		IGameSession Session,
		StartGameConfirmationInstruction StartInstruction,
		SimulationStartState StartState,
		GameTestBuilder Builder);

	private sealed record UnsupportedInstruction()
		: ModeratorInstruction(
			privateInstruction: nameof(UnsupportedInstruction),
			semantic: ModeratorInstructionSemantic.RecordDayVote);
}
