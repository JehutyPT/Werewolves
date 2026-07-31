using FluentAssertions;
using System.Collections.Immutable;
using System.Text.Json;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.GameLogic.Strategies;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
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
			.Should().Be("baseline-random@3-splitmix64");
		BaselineRandomDecisionStrategy.Policy.AdmittedSemantics.Should().BeEquivalentTo(
		[
			ModeratorInstructionSemantic.StartGame,
			ModeratorInstructionSemantic.FinishedGame,
			ModeratorInstructionSemantic.StartNight,
			ModeratorInstructionSemantic.FinishNightActions,
			ModeratorInstructionSemantic.WakeRole,
			ModeratorInstructionSemantic.IdentifyRoleHolders,
			ModeratorInstructionSemantic.ObserveWerewolfFactionAgentGroup,
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
	    ModeratorInstructionSemantic.AnnounceVillageIdiotPardon,
			ModeratorInstructionSemantic.AnnounceDayElimination,
			ModeratorInstructionSemantic.ObserveVillagerVillagerFromDeal
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
				SimulatorCapability.FullProbability.Identity),
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
	public void BaselineRandomDecisionStrategy_WithActualLittleGirlIdentificationSlot_UsesSeededHolder()
	{
		var scenario = new StateModels.Models.Simulation.SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.LittleGirl,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var material = new RunSeedMaterial(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorCapability.SafetyScreening.Identity),
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			runNumber: 17);
		var startState = SimulationStartStateDeriver.Derive(
			material,
			SimulatorCapability.SafetyScreening);
		var config = startState.CreateGameSessionConfig();
		var builder = CreateBuilder()
			.WithPlayers(config.Players.ToArray())
			.WithRoles(config.Roles.ToArray());
		builder.StartGame();
		builder.ConfirmGameStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var seededHolderSeat = startState.RoleAssignments
			.Single(assignment => assignment.Role == MainRoleType.LittleGirl)
			.SeatNumber;
		var strategy = new BaselineRandomDecisionStrategy(
			material,
			startState,
			SimulatorCapability.SafetyScreening.HeadlessResponsePolicy);

		var response = strategy.CreateResponse(identification, session);
		var accepted = builder.Process(response);

		identification.Semantic.Should()
			.Be(ModeratorInstructionSemantic.IdentifyRoleHolders);
		identification.RoleIdentification.Should().Be(MainRoleType.LittleGirl);
		identification.CountConstraint.Should().BeEquivalentTo(
			NumberRangeConstraint.Single);
		response.SelectedPlayerIds.Should().Equal(players[seededHolderSeat - 1].Id);
		accepted.IsSuccess.Should().BeTrue();
		MarkTestCompleted();
	}

	[Fact]
	public void BaselineRandomDecisionStrategy_WithVillagerVillagerDealObservation_UsesSeededHolder()
	{
		var scenario = new StateModels.Models.Simulation.SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.VillagerVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var material = new RunSeedMaterial(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorCapability.SafetyScreening.Identity),
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			runNumber: 7);
		var startState = SimulationStartStateDeriver.Derive(
			material,
			SimulatorCapability.SafetyScreening);
		var config = startState.CreateGameSessionConfig();
		var builder = CreateBuilder()
			.WithPlayers(config.Players.ToArray())
			.WithRoles(config.Roles.ToArray());
		builder.StartGame();
		var observation = InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
			builder.ConfirmGameStart());
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var seededHolderSeat = startState.RoleAssignments
			.Single(assignment => assignment.Role == MainRoleType.VillagerVillager)
			.SeatNumber;
		var strategy = new BaselineRandomDecisionStrategy(
			material,
			startState,
			SimulatorCapability.SafetyScreening.HeadlessResponsePolicy);

		var response = strategy.CreateResponse(observation, session);
		var accepted = builder.Process(response);

		response.SelectedPlayerIds.Should().Equal(players[seededHolderSeat - 1].Id);
		accepted.IsSuccess.Should().BeTrue();
		players[seededHolderSeat - 1].State.PubliclyRevealedRole.Should()
			.Be(MainRoleType.VillagerVillager);
		accepted.ModeratorInstruction!.Semantic.Should().Be(ModeratorInstructionSemantic.StartNight);
		MarkTestCompleted();
	}

	[Fact]
	public void BaselineRandomDecisionStrategy_WithScapegoatHolderObservation_UsesLivingEffectiveHolderOrLegalEmpty()
	{
		var scenario = new StateModels.Models.Simulation.SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Scapegoat,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var material = new RunSeedMaterial(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorCapability.SafetyScreening.Identity),
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			runNumber: 29);
		var startState = SimulationStartStateDeriver.Derive(
			material,
			SimulatorCapability.SafetyScreening);
		var config = startState.CreateGameSessionConfig();
		var builder = CreateBuilder()
			.WithPlayers(config.Players.ToArray())
			.WithRoles(config.Roles.ToArray());
		builder.StartGame();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var seededHolderSeat = startState.RoleAssignments
			.Single(assignment => assignment.Role == MainRoleType.Scapegoat)
			.SeatNumber;
		var seededHolder = players[seededHolderSeat - 1];
		var replacementHolder = players.First(player => player.Id != seededHolder.Id);
		var observation = new SelectPlayersInstruction(
			ModeratorInstructionSemantic.ObserveScapegoatHolderForTie,
			players.Select(player => player.Id).ToHashSet(),
			NumberRangeConstraint.SingleOptional,
			privateInstruction: GameStrings.ScapegoatHolderObservationInstruction);
		var strategy = new BaselineRandomDecisionStrategy(
			material,
			startState,
			SimulatorCapability.SafetyScreening.HeadlessResponsePolicy);

		var seededResponse = strategy.CreateResponse(observation, session);
		builder.ArrangeKnownRole(seededHolder.Id, MainRoleType.SimpleVillager);
		builder.ArrangeKnownRole(replacementHolder.Id, MainRoleType.Scapegoat);
		var currentResponse = strategy.CreateResponse(observation, session);
		builder.ArrangeEliminatedPlayer(replacementHolder.Id);
		var deadResponse = strategy.CreateResponse(observation, session);

		seededResponse.SelectedPlayerIds.Should().Equal(seededHolder.Id);
		currentResponse.SelectedPlayerIds.Should().Equal(replacementHolder.Id);
		deadResponse.SelectedPlayerIds.Should().BeEmpty();
		MarkTestCompleted();
	}

	[Fact]
	public void BaselineRandomDecisionStrategy_WithThreeBrothersIdentification_UsesExactSeededTrio()
	{
		var scenario = new StateModels.Models.Simulation.SimulationScenario(
			9,
			[
				MainRoleType.ThreeBrothers,
				MainRoleType.ThreeBrothers,
				MainRoleType.ThreeBrothers,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var material = new RunSeedMaterial(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorCapability.SafetyScreening.Identity),
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			runNumber: 11);
		var startState = SimulationStartStateDeriver.Derive(
			material,
			SimulatorCapability.SafetyScreening);
		var config = startState.CreateGameSessionConfig();
		var builder = CreateBuilder()
			.WithPlayers(config.Players.ToArray())
			.WithRoles(config.Roles.ToArray());
		builder.StartGame();
		builder.ConfirmGameStart();
		var identification =
			InstructionAssert.ExpectSuccessWithType<SelectPlayersInstruction>(
				builder.ConfirmNightStart());
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var seededBrotherIds = startState.RoleAssignments
			.Select((assignment, index) => (assignment, index))
			.Where(pair => pair.assignment.Role == MainRoleType.ThreeBrothers)
			.Select(pair => players[pair.index].Id)
			.ToHashSet();
		var strategy = new BaselineRandomDecisionStrategy(
			material,
			startState,
			SimulatorCapability.SafetyScreening.HeadlessResponsePolicy);

		var response = strategy.CreateResponse(identification, session);
		var accepted = builder.Process(response);

		identification.RoleIdentification.Should().Be(MainRoleType.ThreeBrothers);
		identification.CountConstraint.Should().BeEquivalentTo(
			NumberRangeConstraint.Exact(3));
		response.SelectedPlayerIds.Should().BeEquivalentTo(seededBrotherIds);
		accepted.IsSuccess.Should().BeTrue();
		MarkTestCompleted();
	}

	[Fact]
	public void BaselineRandomDecisionStrategy_WithRoleIdentification_UsesCommittedCurrentRoleWithinSelectionContract()
	{
		var material = CreateRunSeedMaterial(runNumber: 13);
		var startState = SimulationStartStateDeriver.Derive(material);
		var config = startState.CreateGameSessionConfig();
		var builder = CreateBuilder()
			.WithPlayers(config.Players.ToArray())
			.WithRoles(config.Roles.ToArray());
		builder.StartGame();
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var committedIdentification = builder.GetCurrentInstruction()
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		committedIdentification.RoleIdentification.Should().HaveValue();
		var identifiedRole = committedIdentification.RoleIdentification!.Value;
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var transformedHolder = players
			.Select((player, index) => (Player: player, SeededRole: startState.RoleAssignments[index].Role))
			.First(pair => pair.SeededRole != identifiedRole);
		var accepted = builder.Process(
			committedIdentification.CreateResponse([transformedHolder.Player.Id]));
		accepted.IsSuccess.Should().BeTrue();
		transformedHolder.Player.State.CurrentRole.Should().Be(identifiedRole);
		var selectableNonHolder = players
			.Select((player, index) => (Player: player, SeededRole: startState.RoleAssignments[index].Role))
			.First(pair =>
				pair.Player.Id != transformedHolder.Player.Id &&
				pair.SeededRole != identifiedRole);
		var identifyCommittedRole = new SelectPlayersInstruction(
			ModeratorInstructionSemantic.IdentifyRoleHolders,
			[transformedHolder.Player.Id, selectableNonHolder.Player.Id],
			NumberRangeConstraint.Single,
			publicAnnouncement: null,
			privateInstruction: GameStrings.RevealRolePromptSpecify,
			affectedPlayerIds: null,
			roleIdentification: identifiedRole);
		var strategy = new BaselineRandomDecisionStrategy(
			material,
			startState,
			BaselineRandomDecisionStrategy.Policy);

		var response = strategy.CreateResponse(identifyCommittedRole, session);

		response.SelectedPlayerIds.Should().Equal(transformedHolder.Player.Id);
		response.SelectedPlayerIds.Should().HaveCount(identifyCommittedRole.CountConstraint.Minimum)
			.And.BeSubsetOf(identifyCommittedRole.SelectablePlayerIds);
		MarkTestCompleted();
	}

	[Fact]
	public void BaselineRandomDecisionStrategy_WithRoleReveal_UsesCurrentRoleThenSeededTruth()
	{
		var material = CreateRunSeedMaterial(runNumber: 13);
		var startState = SimulationStartStateDeriver.Derive(material);
		var config = startState.CreateGameSessionConfig();
		var builder = CreateBuilder()
			.WithPlayers(config.Players.ToArray())
			.WithRoles(config.Roles.ToArray());
		builder.StartGame();
		builder.ConfirmGameStart();
		builder.ConfirmNightStart();
		var identification = builder.GetCurrentInstruction()
			.Should().BeOfType<SelectPlayersInstruction>().Subject;
		identification.RoleIdentification.Should().HaveValue();
		var identifiedRole = identification.RoleIdentification!.Value;
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var playerWithChangedTruth = players
			.Select((player, index) => (Player: player, Truth: startState.RoleAssignments[index].Role))
			.First(pair => pair.Truth != identifiedRole);
		var identified = builder.Process(
			identification.CreateResponse([playerWithChangedTruth.Player.Id]));
		identified.IsSuccess.Should().BeTrue();
		playerWithChangedTruth.Player.State.CurrentRole.Should().Be(identifiedRole);
		var unknown = players
			.Select((player, index) => (Player: player, Truth: startState.RoleAssignments[index].Role))
			.First(pair =>
				pair.Player.Id != playerWithChangedTruth.Player.Id &&
				pair.Player.State.CurrentRole is null);
		var reveal = new AssignRolesInstruction(
			ModeratorInstructionSemantic.AssignDawnVictimRoles,
			ImmutableHashSet.Create(playerWithChangedTruth.Player.Id, unknown.Player.Id),
			[identifiedRole, unknown.Truth],
			privateInstruction: GameStrings.RevealRolePromptSpecify);
		var strategy = new BaselineRandomDecisionStrategy(
			material,
			startState,
			BaselineRandomDecisionStrategy.Policy);

		var response = strategy.CreateResponse(reveal, session);

		response.AssignedPlayerRoles.Should()
			.Contain(playerWithChangedTruth.Player.Id, identifiedRole)
			.And.Contain(unknown.Player.Id, unknown.Truth);
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
		var playersForAssignment = ImmutableHashSet.Create(players[1].Id, players[3].Id);
		var rolesForAssignment = new[]
		{
			startState.RoleAssignments[1].Role,
			startState.RoleAssignments[3].Role
		};
		var firstAssignment = new AssignRolesInstruction(
			ModeratorInstructionSemantic.AssignDawnVictimRoles,
			playersForAssignment,
			rolesForAssignment,
			privateInstruction: GameStrings.RevealRolePromptSpecify);
		var replayAssignment = new AssignRolesInstruction(
			ModeratorInstructionSemantic.AssignDawnVictimRoles,
			playersForAssignment,
			rolesForAssignment,
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
		assigned.AssignedPlayerRoles.Should()
			.Contain(players[1].Id, startState.RoleAssignments[1].Role)
			.And.Contain(players[3].Id, startState.RoleAssignments[3].Role);
		assigned.AssignedPlayerRoles.OrderBy(pair => players.FindIndex(player => player.Id == pair.Key)).Select(pair => pair.Value)
			.Should().Equal(
				replayAssigned.AssignedPlayerRoles!.OrderBy(pair => players.FindIndex(player => player.Id == pair.Key)).Select(pair => pair.Value));
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(0L, "wolf-hound-werewolves")]
	[InlineData(1L, "wolf-hound-werewolves")]
	public void BaselineRandomDecisionStrategy_WithWolfHoundAlignment_UsesGlobalDeterministicStreamWithoutHiddenTruth(
		long runNumber,
		string expectedOptionId)
	{
		var scenario = new StateModels.Models.Simulation.SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.WolfHound,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var material = new RunSeedMaterial(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorCapability.SafetyScreening.Identity),
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			runNumber);
		var optionInstruction = new SelectOptionsInstruction(
			ModeratorInstructionSemantic.ChooseWolfHoundAlignment,
			[
				new ModeratorOption(
					WolfHoundAlignmentOptionIds.Villagers,
					GameStrings.VillagersGroupName),
				new ModeratorOption(
					WolfHoundAlignmentOptionIds.Werewolves,
					GameStrings.WerewolvesGroupName)
			],
			NumberRangeConstraint.Single,
			privateInstruction: GameStrings.WolfHoundAlignmentInstruction);
		var acknowledgment = new ConfirmationInstruction(
			ModeratorInstructionSemantic.WakeRole,
			privateInstruction: GameStrings.WolfHoundAlignmentInstruction);

		ModeratorResponse Respond(bool acknowledgeFirst)
		{
			var random = new DeterministicRandomSource(material);
			var startState = SimulationStartStateDeriver.Derive(
				material,
				SimulatorCapability.SafetyScreening,
				random);
			var config = startState.CreateGameSessionConfig();
			var builder = CreateBuilder()
				.WithPlayers(config.Players.ToArray())
				.WithRoles(config.Roles.ToArray());
			builder.StartGame();
			var session = builder.GetGameState()!;
			var strategy = new BaselineRandomDecisionStrategy(
				material,
				startState,
				SimulatorCapability.SafetyScreening.HeadlessResponsePolicy,
				random);
			if (acknowledgeFirst)
			{
				strategy.CreateResponse(acknowledgment, session)
					.Type.Should().Be(ExpectedInputType.Continue);
			}

			return strategy.CreateResponse(optionInstruction, session);
		}

		var selected = Respond(acknowledgeFirst: false);
		var replay = Respond(acknowledgeFirst: false);
		var selectedAfterAcknowledgment = Respond(acknowledgeFirst: true);

		selected.SelectedOptionIds.Should().Equal(expectedOptionId);
		replay.SelectedOptionIds.Should().Equal(expectedOptionId);
		selectedAfterAcknowledgment.SelectedOptionIds.Should().Equal(expectedOptionId);
		selected.SelectedOptionIds.Should().BeSubsetOf(
			optionInstruction.Options.Select(option => option.Id));
		MarkTestCompleted();
		}

		[Theory]
		[InlineData(
			0L,
			AccursedWolfFatherInfectionOptionIds.Decline)]
		[InlineData(
			1L,
			AccursedWolfFatherInfectionOptionIds.Infect)]
		public void BaselineRandomDecisionStrategy_WithAccursedWolfFatherInfection_CoversBothBranchesDeterministically(
			long runNumber,
			string expectedOptionId)
		{
			var scenario =
				new StateModels.Models.Simulation.SimulationScenario(
					5,
					[
						MainRoleType.SimpleWerewolf,
						MainRoleType.AccursedWolfFather,
						MainRoleType.SimpleVillager,
						MainRoleType.SimpleVillager,
						MainRoleType.SimpleVillager
					]);
			var material = new RunSeedMaterial(
				new SimulationCompatibilityIdentity(
					scenario.ToCanonical(),
					SimulatorCapability.SafetyScreening.Identity),
				BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
				runNumber);
			var optionInstruction = new SelectOptionsInstruction(
				ModeratorInstructionSemantic
					.ChooseAccursedWolfFatherInfection,
				[
					new ModeratorOption(
						AccursedWolfFatherInfectionOptionIds.Infect,
						GameStrings.AccursedWolfFatherInfectOption),
					new ModeratorOption(
						AccursedWolfFatherInfectionOptionIds.Decline,
						GameStrings.DeclineOption)
				],
				NumberRangeConstraint.Single,
				privateInstruction:
					GameStrings.AccursedWolfFatherInfectionInstruction);
			var random = new DeterministicRandomSource(material);
			var startState = SimulationStartStateDeriver.Derive(
				material,
				SimulatorCapability.SafetyScreening,
				random);
			var config = startState.CreateGameSessionConfig();
			var builder = CreateBuilder()
				.WithPlayers(config.Players.ToArray())
				.WithRoles(config.Roles.ToArray());
			builder.StartGame();
			var strategy = new BaselineRandomDecisionStrategy(
				material,
				startState,
				SimulatorCapability.SafetyScreening.HeadlessResponsePolicy,
				random);

			var selected = strategy.CreateResponse(
				optionInstruction,
				builder.GetGameState()!);

			selected.SelectedOptionIds.Should().Equal(expectedOptionId);
			selected.InstructionId.Should().Be(
				optionInstruction.InstructionId);
			MarkTestCompleted();
		}

		[Fact]
	public void BaselineRandomDecisionStrategy_WithStutteringJudgeInstructions_ReturnsLegalDeterministicResponses()
	{
		var scenario = new StateModels.Models.Simulation.SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.StutteringJudge,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var material = new RunSeedMaterial(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorCapability.SafetyScreening.Identity),
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			runNumber: 23);
		var startState = SimulationStartStateDeriver.Derive(
			material,
			SimulatorCapability.SafetyScreening);
		var config = startState.CreateGameSessionConfig();
		var builder = CreateBuilder()
			.WithPlayers(config.Players.ToArray())
			.WithRoles(config.Roles.ToArray());
		builder.StartGame();
		var session = builder.GetGameState()!;
		var setup = new ConfirmationInstruction(
			ModeratorInstructionSemantic.EstablishStutteringJudgeSignal,
			privateInstruction: GameStrings.StutteringJudgeSignalSetupInstruction);
		var observation = new SelectOptionsInstruction(
			ModeratorInstructionSemantic.ObserveStutteringJudgeSignal,
			[
				new ModeratorOption(
					StutteringJudgeSignalOptionIds.Occurred,
					GameStrings.StutteringJudgeSignalOccurredOption),
				new ModeratorOption(
					StutteringJudgeSignalOptionIds.DidNotOccur,
					GameStrings.StutteringJudgeSignalDidNotOccurOption)
			],
			NumberRangeConstraint.Single,
			privateInstruction: GameStrings.StutteringJudgeSignalObservationInstruction);
		var firstStrategy = new BaselineRandomDecisionStrategy(
			material,
			startState,
			SimulatorCapability.SafetyScreening.HeadlessResponsePolicy);
		var replayStrategy = new BaselineRandomDecisionStrategy(
			material,
			startState,
			SimulatorCapability.SafetyScreening.HeadlessResponsePolicy);

		var setupResponse = firstStrategy.CreateResponse(setup, session);
		var observationResponse = firstStrategy.CreateResponse(observation, session);
		var replayObservationResponse = replayStrategy.CreateResponse(observation, session);

		setupResponse.InstructionId.Should().Be(setup.InstructionId);
		setupResponse.Type.Should().Be(ExpectedInputType.Continue);
		observationResponse.InstructionId.Should().Be(observation.InstructionId);
		observationResponse.Type.Should().Be(ExpectedInputType.OptionSelection);
		observationResponse.SelectedOptionIds.Should().ContainSingle()
			.Which.Should().BeOneOf(
				StutteringJudgeSignalOptionIds.Occurred,
				StutteringJudgeSignalOptionIds.DidNotOccur);
		replayObservationResponse.SelectedOptionIds.Should()
			.Equal(observationResponse.SelectedOptionIds);
		MarkTestCompleted();
	}

	[Fact]
	public void BaselineRandomDecisionStrategy_WithScapegoatPermittedVoters_SelectsNonEmptyLegalSubsetDeterministically()
	{
		var scenario = new StateModels.Models.Simulation.SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Scapegoat,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var material = new RunSeedMaterial(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorCapability.SafetyScreening.Identity),
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			runNumber: 31);
		var startState = SimulationStartStateDeriver.Derive(
			material,
			SimulatorCapability.SafetyScreening);
		var config = startState.CreateGameSessionConfig();
		var builder = CreateBuilder()
			.WithPlayers(config.Players.ToArray())
			.WithRoles(config.Roles.ToArray());
		builder.StartGame();
		var session = builder.GetGameState()!;
		var candidates = session.GetPlayers()
			.Select(player => player.Id)
			.ToHashSet();
		var instruction = new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectScapegoatPermittedVoters,
			candidates,
			NumberRangeConstraint.AtLeast(1),
			privateInstruction: GameStrings.ScapegoatPermittedVotersSelectionInstruction);
		var firstStrategy = new BaselineRandomDecisionStrategy(
			material,
			startState,
			SimulatorCapability.SafetyScreening.HeadlessResponsePolicy);
		var replayStrategy = new BaselineRandomDecisionStrategy(
			material,
			startState,
			SimulatorCapability.SafetyScreening.HeadlessResponsePolicy);

		var first = firstStrategy.CreateResponse(instruction, session);
		var replay = replayStrategy.CreateResponse(instruction, session);

		first.SelectedPlayerIds.Should().NotBeEmpty()
			.And.BeSubsetOf(candidates);
		first.SelectedPlayerIds.Should().Equal(replay.SelectedPlayerIds);
		MarkTestCompleted();
	}

	[Fact]
	public void BaselineRandomDecisionStrategy_WithHunterFinalShot_SelectsOneLegalTargetDeterministically()
	{
		var scenario = new StateModels.Models.Simulation.SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Hunter,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var material = new RunSeedMaterial(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorCapability.SafetyScreening.Identity),
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			runNumber: 17);
		var startState = SimulationStartStateDeriver.Derive(
			material,
			SimulatorCapability.SafetyScreening);
		var config = startState.CreateGameSessionConfig();
		var builder = CreateBuilder()
			.WithPlayers(config.Players.ToArray())
			.WithRoles(config.Roles.ToArray());
		builder.StartGame();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var hunterSeat = startState.RoleAssignments
			.Single(assignment => assignment.Role == MainRoleType.Hunter)
			.SeatNumber;
		var hunterId = players[hunterSeat - 1].Id;
		var legalTargetIds = players
			.Where(player => player.Id != hunterId)
			.Take(3)
			.Select(player => player.Id)
			.ToHashSet();
		var instruction = new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectHunterFinalShotTarget,
			legalTargetIds,
			NumberRangeConstraint.Single,
			publicAnnouncement:
				GameStrings.HunterFinalShotSelectionInstruction,
			affectedPlayerIds: [hunterId]);
		var firstStrategy = new BaselineRandomDecisionStrategy(
			material,
			startState,
			SimulatorCapability.SafetyScreening.HeadlessResponsePolicy);
		var replayStrategy = new BaselineRandomDecisionStrategy(
			material,
			startState,
			SimulatorCapability.SafetyScreening.HeadlessResponsePolicy);

		var first = firstStrategy.CreateResponse(instruction, session);
		var replay = replayStrategy.CreateResponse(instruction, session);

		first.InstructionId.Should().Be(instruction.InstructionId);
		first.Type.Should().Be(ExpectedInputType.PlayerSelection);
		first.SelectedPlayerIds.Should().ContainSingle();
		legalTargetIds.Should().Contain(
			first.SelectedPlayerIds!.Single());
		first.SelectedPlayerIds.Should().Equal(replay.SelectedPlayerIds);
		first.SelectedPlayerIds.Should().NotContain(hunterId);
		MarkTestCompleted();
	}

	[Fact]
	public void BaselineRandomDecisionStrategy_WithWhiteWerewolfOptionalTarget_UsesOnlyVisibleLegalityDeterministically()
	{
		var scenario = new StateModels.Models.Simulation.SimulationScenario(
			5,
			[
				MainRoleType.WhiteWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var material = new RunSeedMaterial(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorCapability.SafetyScreening.Identity),
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			runNumber: 17);
		var startState = SimulationStartStateDeriver.Derive(
			material,
			SimulatorCapability.SafetyScreening);
		var config = startState.CreateGameSessionConfig();
		var builder = CreateBuilder()
			.WithPlayers(config.Players.ToArray())
			.WithRoles(config.Roles.ToArray());
		builder.StartGame();
		var session = builder.GetGameState()!;
		var players = session.GetPlayers().ToArray();
		var holderSeat = startState.RoleAssignments
			.Single(assignment => assignment.Role == MainRoleType.WhiteWerewolf)
			.SeatNumber;
		var holderId = players[holderSeat - 1].Id;
		var legalTargetIds = players
			.Where(player => player.Id != holderId)
			.Take(3)
			.Select(player => player.Id)
			.ToHashSet();
		var instruction = new SelectPlayersInstruction(
			ModeratorInstructionSemantic.SelectWhiteWerewolfTarget,
			legalTargetIds,
			NumberRangeConstraint.SingleOptional,
			privateInstruction: GameStrings.WhiteWerewolfTargetSelectionInstruction,
			affectedPlayerIds: [holderId]);
		var firstStrategy = new BaselineRandomDecisionStrategy(
			material,
			startState,
			SimulatorCapability.SafetyScreening.HeadlessResponsePolicy);
		var replayStrategy = new BaselineRandomDecisionStrategy(
			material,
			startState,
			SimulatorCapability.SafetyScreening.HeadlessResponsePolicy);

		var first = firstStrategy.CreateResponse(instruction, session);
		var replay = replayStrategy.CreateResponse(instruction, session);

		first.InstructionId.Should().Be(instruction.InstructionId);
		first.Type.Should().Be(ExpectedInputType.PlayerSelection);
		first.SelectedPlayerIds.Should().HaveCountLessThanOrEqualTo(1);
		first.SelectedPlayerIds.Should().BeSubsetOf(legalTargetIds);
		first.SelectedPlayerIds.Should().Equal(replay.SelectedPlayerIds);
		first.SelectedPlayerIds.Should().NotContain(holderId);
		MarkTestCompleted();
	}

	[Fact]
	public void BaselineRandomDecisionStrategy_WithKnownOptionalChoiceSeed_ReturnsEmptyValidResponse()
	{
		var material = CreateRunSeedMaterial(runNumber: 3);
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
	public void HeadlessGameDriver_WithPreKnownWhiteBeneficiaryAndBigBadWolf_CompletesComposition()
	{
		var scenario = new StateModels.Models.Simulation.SimulationScenario(
			9,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.BigBadWolf,
				MainRoleType.WhiteWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var material = new RunSeedMaterial(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorCapability.SafetyScreening.Identity),
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			runNumber: 37);
		var startState = SimulationStartStateDeriver.Derive(
			material,
			SimulatorCapability.SafetyScreening);
		var whiteSeat = startState.RoleAssignments
			.Single(assignment =>
				assignment.Role == MainRoleType.WhiteWerewolf)
			.SeatNumber;
		var driver = new HeadlessGameDriver(
			new BaselineRandomDecisionStrategy(
				material,
				startState,
				SimulatorCapability.SafetyScreening.HeadlessResponsePolicy));

		var execution = driver.CompleteGameSession(
			startState,
			CancellationToken.None);

		var players = execution.Session.GetPlayers().ToArray();
		var closure = execution.Session.GameHistoryLog
			.OfType<FactionFactsCommittedLogEntry>()
			.Single(entry =>
				entry.Source.Kind ==
				FactionFactSourceKind.InitialBeneficiaryClosure);
		var identifiedRoles = execution.Session.GameHistoryLog
			.OfType<RoleIdentificationLogEntry>()
			.Select(entry => entry.Role)
			.ToArray();
		execution.FinalInstruction.Should()
			.BeOfType<FinishedGameConfirmationInstruction>();
		execution.ProcessedInstructionCount.Should().BeGreaterThan(0);
		closure.Facts.Should().BeEmpty();
		startState.FactionFacts[whiteSeat - 1].Beneficiary.Faction.Should()
			.Be(Faction.WhiteWerewolf);
		execution.Session
			.GetFactionBeneficiaryKnowledge(players[whiteSeat - 1].Id)
			.Faction.Should().Be(Faction.WhiteWerewolf);
		identifiedRoles.Should().Contain(MainRoleType.WhiteWerewolf)
			.And.Contain(MainRoleType.BigBadWolf);
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
		result.GameResult.Should().Be(
			new SingleFactionGameResult(Faction.Villager));
		result.VictoryCheckWindow.Should().Be(VictoryCheckWindow.PreNight);
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
				SimulatorCapability.FullProbability.Identity),
			BaselineRandomDecisionStrategy.Identity,
			runNumber);
	}
}
