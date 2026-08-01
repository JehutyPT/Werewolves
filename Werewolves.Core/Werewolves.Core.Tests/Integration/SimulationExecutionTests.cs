using FluentAssertions;
using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public class SimulationExecutionTests : DiagnosticTestBase
{
	public SimulationExecutionTests(ITestOutputHelper output) : base(output)
	{
	}

	[Theory]
	[InlineData(
		MainRoleType.TwoSisters,
		2,
		ModeratorInstructionSemantic.RecognizeRoleHolders)]
	[InlineData(
		MainRoleType.TwoSisters,
		2,
		ModeratorInstructionSemantic.CommunicateAsRoleHolders)]
	[InlineData(
		MainRoleType.ThreeBrothers,
		3,
		ModeratorInstructionSemantic.RecognizeRoleHolders)]
	[InlineData(
		MainRoleType.ThreeBrothers,
		3,
		ModeratorInstructionSemantic.CommunicateAsRoleHolders)]
	[InlineData(
		MainRoleType.WhiteWerewolf,
		1,
		ModeratorInstructionSemantic.SelectWhiteWerewolfTarget)]
	[InlineData(
		MainRoleType.Piper,
		1,
		ModeratorInstructionSemantic.SelectPiperTargets)]
	[InlineData(
		MainRoleType.Piper,
		1,
		ModeratorInstructionSemantic.RecognizeCharmedPlayers)]
	public void Execute_WithRoleHolderSemanticMissingFromPolicy_ReturnsIncompleteEvidence(
		MainRoleType role,
		int roleHolderCardinality,
		ModeratorInstructionSemantic missingSemantic)
	{
		var roles = Enumerable
			.Repeat(role, roleHolderCardinality)
			.Append(MainRoleType.SimpleWerewolf)
			.Concat(Enumerable.Repeat(MainRoleType.SimpleVillager, 5))
			.ToArray();
		var scenario = new SimulationScenario(
			roles.Length,
			roles);
		var roleDescriptor = role switch
		{
			MainRoleType.WhiteWerewolf => new SimulatorProfileRoleDescriptor(
				role,
				Faction.WhiteWerewolf,
				Faction.Werewolf),
			MainRoleType.Piper => new SimulatorProfileRoleDescriptor(
				role,
				Faction.Piper),
			_ => new SimulatorProfileRoleDescriptor(role, Faction.Villager)
		};
		var capability = new SimulatorCapability(
			new SimulatorProfileIdentity(
				$"test-{role}-missing-{missingSemantic}",
				"1"),
			[
				roleDescriptor,
				new(MainRoleType.SimpleWerewolf, Faction.Werewolf, Faction.Werewolf),
				new(MainRoleType.SimpleVillager, Faction.Villager)
			],
			headlessResponsePolicy: new HeadlessResponsePolicy(
				BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
				SimulatorCapability.SafetyScreening.HeadlessResponsePolicy
					.AdmittedSemantics
					.Where(semantic => semantic != missingSemantic)));
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			capability.Identity);
		var decorators = new List<PreserveRoleHoldersUntilNightThreeStrategy>();
		var executor = new SimulationExecutor(
			SimulationStartStateDeriver.Derive,
			strategy =>
			{
				var decorator = new PreserveRoleHoldersUntilNightThreeStrategy(
					strategy,
					role);
				decorators.Add(decorator);
				return new HeadlessGameDriver(decorator);
			},
			SimulationExecutor.AdaptTerminalEvidence);

		foreach (var runNumber in Enumerable.Range(0, 16).Select(value => (long)value))
		{
			var run = executor.Execute(
				scenario,
				capability,
				identity,
				runNumber);

			run.Should().Be(new IncompleteSimulationRun(
				new RunSeedMaterial(
					identity,
					BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
					runNumber)));
			decorators.Should().HaveCount((int)runNumber + 1);
			decorators[^1].ObservedSemantics.Should().Contain(missingSemantic);
			if (missingSemantic == ModeratorInstructionSemantic.CommunicateAsRoleHolders)
			{
				decorators[^1].LivingRoleHolderCountAtCommunication.Should()
					.Be(roleHolderCardinality);
			}
		}
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(MainRoleType.Hunter, 1)]
	[InlineData(MainRoleType.Witch, 1)]
	[InlineData(MainRoleType.StutteringJudge, 1)]
	[InlineData(MainRoleType.Scapegoat, 1)]
	[InlineData(MainRoleType.VillageIdiot, 1)]
	[InlineData(MainRoleType.WolfHound, 1)]
	[InlineData(MainRoleType.AccursedWolfFather, 1)]
	[InlineData(MainRoleType.BigBadWolf, 1)]
	[InlineData(MainRoleType.LittleGirl, 1)]
	[InlineData(MainRoleType.Defender, 1)]
	[InlineData(MainRoleType.TwoSisters, 2)]
	[InlineData(MainRoleType.ThreeBrothers, 3)]
	[InlineData(MainRoleType.WhiteWerewolf, 1)]
	[InlineData(MainRoleType.Piper, 1)]
	[InlineData(MainRoleType.BearTamer, 1)]
	[InlineData(MainRoleType.Fox, 1)]
	[InlineData(MainRoleType.KnightWithRustySword, 1)]
	[InlineData(MainRoleType.Cupid, 1)]
	[InlineData(MainRoleType.Angel, 1)]
	public void ExecuteBatch_WithCardinalityRoleHolders_SafetyRepresentativeCompletesAllOneThousandAttempts(
		MainRoleType role,
		int roleHolderCardinality)
	{
		var roles = Enumerable
			.Repeat(role, roleHolderCardinality)
			.Append(MainRoleType.SimpleWerewolf)
			.Concat(Enumerable.Repeat(MainRoleType.SimpleVillager, 5))
			.ToArray();
		var scenario = new SimulationScenario(
			roles.Length,
			roles);
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);

		var batch = new SimulationExecutor().ExecuteBatch(
			scenario,
			SimulatorCapability.SafetyScreening,
			identity,
			runCount: 1_000);

		batch.Records.Should().HaveCount(1_000);
		batch.Records
			.OfType<IncompleteSimulationRun>()
			.Select(run => run.RunSeedMaterial.RunNumber)
			.Should().BeEmpty();
		batch.CompletedRunCount.Should().Be(1_000);
		batch.IncompleteRunCount.Should().Be(0);
		batch.Records.Should().OnlyContain(run =>
			run is CompletedSimulationRun);
		MarkTestCompleted();
	}

	[Fact]
	public void ExecuteBatch_WithOfferedAngel_CompletesAllOneThousandAttemptsAcrossOrderedBranches()
	{
		MainRoleType[] dealPool =
		[
			MainRoleType.Thief,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		];
		var scenario = new SimulationScenario(
			playerCount: 5,
			roleCompositionCards: dealPool.Concat(
				[MainRoleType.Angel, MainRoleType.Seer]),
			dealPoolCards: dealPool,
			offer1Role: MainRoleType.Angel,
			offer2Role: MainRoleType.Seer);
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);

		var batch = new SimulationExecutor().ExecuteBatch(
			scenario,
			SimulatorCapability.SafetyScreening,
			identity,
			runCount: 1_000);

		scenario.ToCanonical().Offer1Role.Should().Be(MainRoleType.Angel);
		scenario.ToCanonical().Offer2Role.Should().Be(MainRoleType.Seer);
		scenario.ThiefOfferBranchPolicy!.Branches.Should().Equal(
			ThiefOfferBranch.Offer1,
			ThiefOfferBranch.Offer2,
			ThiefOfferBranch.Decline);
		batch.Records.Should().HaveCount(1_000);
		batch.Records
			.OfType<IncompleteSimulationRun>()
			.Select(run => run.RunSeedMaterial.RunNumber)
			.Should().BeEmpty();
		batch.CompletedRunCount.Should().Be(1_000);
		batch.IncompleteRunCount.Should().Be(0);
		batch.Records.Should().OnlyContain(run => run is CompletedSimulationRun);
		MarkTestCompleted();
	}

	[Fact]
	public void HeadlessSafety_OfferBearingThiefScenario_ExecutesGenuineChoice()
	{
		MainRoleType[] dealPool =
		[
			MainRoleType.Thief,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		];
		var scenario = new SimulationScenario(
			playerCount: 5,
			roleCompositionCards: dealPool.Concat(
				[MainRoleType.Seer, MainRoleType.Defender]),
			dealPoolCards: dealPool,
			offer1Role: MainRoleType.Seer,
			offer2Role: MainRoleType.Defender);
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var material = new RunSeedMaterial(
			identity,
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			runNumber: 0);
		var random = new DeterministicRandomSource(material);
		var startState = SimulationStartStateDeriver.Derive(
			material,
			SimulatorCapability.SafetyScreening,
			random);
		var recorder = new RecordingDecisionStrategy(
			new BaselineRandomDecisionStrategy(
				material,
				startState,
				SimulatorCapability.SafetyScreening.HeadlessResponsePolicy,
				random));

		var execution = new HeadlessGameDriver(recorder).CompleteGameSession(
			startState,
			CancellationToken.None);

		startState.RoleAssignments.Select(assignment => assignment.Role)
			.Should().BeEquivalentTo(dealPool);
		startState.CanonicalScenario.Offer1Role.Should().Be(MainRoleType.Seer);
		startState.CanonicalScenario.Offer2Role.Should().Be(MainRoleType.Defender);
		var choice = recorder.Observations.Should()
			.ContainSingle(observation =>
				observation.Instruction.Semantic ==
				ModeratorInstructionSemantic.ChooseThiefOffer)
			.Subject;
		choice.Response.SelectedOptionIds.Should().ContainSingle();
		execution.Session.RoleLockIn.Offer1!.PrintedRole.Should().Be(MainRoleType.Seer);
		execution.Session.RoleLockIn.Offer2!.PrintedRole.Should().Be(MainRoleType.Defender);
		(execution.Session.GameHistoryLog.OfType<PermanentRoleSwapCommittedLogEntry>().Count() +
		 execution.Session.GameHistoryLog.OfType<ThiefOfferDeclinedLogEntry>().Count())
			.Should().Be(1);
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(
		0,
		ThiefOfferOptionIds.Offer1,
		MainRoleType.Seer,
		ModeratorInstructionSemantic.SelectSeerTarget,
		NightActionType.SeerCheck)]
	[InlineData(
		1,
		ThiefOfferOptionIds.Offer2,
		MainRoleType.Defender,
		ModeratorInstructionSemantic.SelectDefenderTarget,
		NightActionType.DefenderProtect)]
	[InlineData(2, ThiefOfferOptionIds.Decline, null, null, null)]
	public void HeadlessSafety_OfferBearingThiefScenario_ForcesEveryLegalBranch(
		long runNumber,
		string expectedOptionId,
		MainRoleType? expectedAcquiredRole,
		ModeratorInstructionSemantic? expectedNightOneActionSemantic,
		NightActionType? expectedNightOneActionType)
	{
		var scenario = CreateOfferBearingThiefScenario();
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var material = new RunSeedMaterial(
			identity,
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			runNumber);
		var random = new DeterministicRandomSource(material);
		var startState = SimulationStartStateDeriver.Derive(
			material,
			SimulatorCapability.SafetyScreening,
			random);
		var recorder = new RecordingDecisionStrategy(
			new BaselineRandomDecisionStrategy(
				material,
				startState,
				SimulatorCapability.SafetyScreening.HeadlessResponsePolicy,
				random));

		var execution = new HeadlessGameDriver(recorder).CompleteGameSession(
			startState,
			CancellationToken.None);

		var choice = recorder.Observations.Should()
			.ContainSingle(observation =>
				observation.Instruction.Semantic ==
				ModeratorInstructionSemantic.ChooseThiefOffer)
			.Subject;
		choice.Response.SelectedOptionIds.Should().Equal(expectedOptionId);
		var swaps = execution.Session.GameHistoryLog
			.OfType<PermanentRoleSwapCommittedLogEntry>()
			.ToArray();
		var declines = execution.Session.GameHistoryLog
			.OfType<ThiefOfferDeclinedLogEntry>()
			.ToArray();
		if (expectedOptionId == ThiefOfferOptionIds.Decline)
		{
			swaps.Should().BeEmpty();
			declines.Should().ContainSingle();
		}
		else
		{
			declines.Should().BeEmpty();
			swaps.Should().ContainSingle().Which.NewCurrentRole.Should().Be(
				expectedAcquiredRole);

			var nightOneObservations = recorder.Observations
				.Where(observation => observation.TurnNumber == 1)
				.ToArray();
			nightOneObservations.Count(observation =>
				observation.Instruction.Semantic ==
				ModeratorInstructionSemantic.StartNight).Should().Be(1);
			var nightOneChoiceIndex = Array.FindIndex(
				nightOneObservations,
				observation => observation.Instruction.Semantic ==
					ModeratorInstructionSemantic.ChooseThiefOffer);
			var acquiredRoleActionIndex = Array.FindIndex(
				nightOneObservations,
				observation => observation.Instruction.Semantic ==
					expectedNightOneActionSemantic);
			acquiredRoleActionIndex.Should().BeGreaterThan(nightOneChoiceIndex);

			var acquiredRoleAction = nightOneObservations[acquiredRoleActionIndex];
			acquiredRoleAction.Instruction.AffectedPlayerIds.Should().Equal(
				choice.Instruction.AffectedPlayerIds);
			nightOneObservations
				.Select(observation => observation.Instruction)
				.OfType<SelectPlayersInstruction>()
				.Where(instruction => instruction.Semantic ==
					ModeratorInstructionSemantic.IdentifyRoleHolders)
				.Select(instruction => instruction.RoleIdentification)
				.Should().Equal(MainRoleType.Thief);
			execution.Session.GameHistoryLog
				.OfType<NightActionLogEntry>()
				.Should().ContainSingle(entry =>
					entry.TurnNumber == 1 &&
					entry.ActionType == expectedNightOneActionType);
		}
		MarkTestCompleted();
	}

	[Fact]
	public void SafetyRunZero_Piper_RecordsSeededExactTargetAndRecognitionTrace()
	{
		MainRoleType[] roles =
		[
			MainRoleType.Piper,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		];
		var scenario = new SimulationScenario(roles.Length, roles);
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var material = new RunSeedMaterial(
			identity,
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			runNumber: 0);
		var random = new DeterministicRandomSource(material);
		var startState = SimulationStartStateDeriver.Derive(
			material,
			SimulatorCapability.SafetyScreening,
			random);
		var piperAssignment = startState.RoleAssignments.Single(assignment =>
			assignment.Role == MainRoleType.Piper);
		var piperFacts = startState.FactionFacts[piperAssignment.SeatNumber - 1];
		piperFacts.Beneficiary.Should().Be(
			FactionBeneficiaryKnowledge.Known(Faction.Piper));
		piperFacts.Agents.Values.Should().OnlyContain(knowledge =>
			knowledge == FactionAgentKnowledge.KnownNonAgent);
		var recorder = new RecordingDecisionStrategy(
			new BaselineRandomDecisionStrategy(
				material,
				startState,
				SimulatorCapability.SafetyScreening.HeadlessResponsePolicy,
				random));

		var execution = new HeadlessGameDriver(recorder).CompleteGameSession(
			startState,
			CancellationToken.None);

		var players = execution.Session.GetPlayers().ToArray();
		var piper = players[piperAssignment.SeatNumber - 1];
		var selectionTrace = recorder.Observations.First(observation =>
			observation.Instruction.Semantic ==
			ModeratorInstructionSemantic.SelectPiperTargets);
		var selection = selectionTrace.Instruction.Should()
			.BeOfType<SelectPlayersInstruction>()
			.Subject;
		selection.CountConstraint.Should().Be(NumberRangeConstraint.Exact(2));
		selection.AffectedPlayerIds.Should().Equal(piper.Id);
		selectionTrace.Response.Type.Should().Be(
			ExpectedInputType.PlayerSelection);
		selectionTrace.Response.SelectedPlayerIds.Should().HaveCount(2);
		var selectedTargets = selectionTrace.Response.SelectedPlayerIds!;
		selectedTargets.Should().BeSubsetOf(selection.SelectablePlayerIds);
		selectedTargets.Should().OnlyContain(playerId =>
			playerId != piper.Id);
		execution.Session.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().Contain(entry =>
				entry.ActionType == NightActionType.PiperCharm &&
				entry.ActingPlayerId == piper.Id &&
				entry.TargetIds != null &&
				entry.TargetIds.ToHashSet().SetEquals(selectedTargets));
		selectedTargets.Should().OnlyContain(playerId =>
			execution.Session.GetPlayerState(playerId)
				.HasStatusEffect(StatusEffectTypes.Charmed));
		var recognitionTrace = recorder.Observations.First(observation =>
			observation.Instruction.Semantic ==
			ModeratorInstructionSemantic.RecognizeCharmedPlayers);
		recognitionTrace.Instruction.AffectedPlayerIds.Should()
			.Contain(selectedTargets);
		recognitionTrace.Response.Type.Should().Be(ExpectedInputType.Continue);
		execution.FinalInstruction.Should()
			.BeOfType<FinishedGameConfirmationInstruction>();
		MarkTestCompleted();
	}

	[Fact]
	public void Execute_WhiteWerewolfRepresentativeRunTwo_Completes()
	{
		MainRoleType[] roles =
		[
			MainRoleType.WhiteWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		];
		var scenario = new SimulationScenario(roles.Length, roles);
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);

		var run = new SimulationExecutor().Execute(
			scenario,
			SimulatorCapability.SafetyScreening,
			identity,
			runNumber: 2);

		run.Should().BeOfType<CompletedSimulationRun>();
		MarkTestCompleted();
	}

	[Fact]
	public void SafetyRunZero_BigBadWolfAvailableWithTarget_RecordsMandatorySelectionTrace()
	{
		var fixture = CreateSafetyRunZeroBigBadWolfTrace();
		RespondToCurrentInstruction(fixture.Builder, fixture.Recorder);
		RespondToCurrentInstruction(fixture.Builder, fixture.Recorder);
		RespondToCurrentInstruction(fixture.Builder, fixture.Recorder);
		RespondToCurrentInstruction(fixture.Builder, fixture.Recorder);
		RespondToCurrentInstruction(fixture.Builder, fixture.Recorder);
		RespondToCurrentInstruction(fixture.Builder, fixture.Recorder);
		RespondToCurrentInstruction(fixture.Builder, fixture.Recorder);
		var finishNight = RespondToCurrentInstruction(
			fixture.Builder,
			fixture.Recorder);

		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		var selectionTrace = fixture.Recorder.Observations.Should()
			.ContainSingle(observation =>
				observation.Instruction.Semantic ==
				ModeratorInstructionSemantic.SelectBigBadWolfTarget)
			.Subject;
		var selection = selectionTrace.Instruction.Should()
			.BeOfType<SelectPlayersInstruction>()
			.Subject;
		selection.CountConstraint.Should().Be(NumberRangeConstraint.Single);
		selection.AffectedPlayerIds.Should().Equal(fixture.BigBadWolf.Id);
		selection.SelectablePlayerIds.Should().NotBeEmpty();
		selectionTrace.Response.InstructionId.Should().Be(
			selection.InstructionId);
		selectionTrace.Response.Type.Should().Be(
			ExpectedInputType.PlayerSelection);
		selectionTrace.Response.SelectedPlayerIds.Should().ContainSingle();
		var selectedTarget = selectionTrace.Response.SelectedPlayerIds!.Single();
		selection.SelectablePlayerIds.Should().Contain(selectedTarget);
		fixture.Builder.GetGameState()!.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType ==
				NightActionType.BigBadWolfVictimSelection &&
				entry.TargetIds!.SequenceEqual(new[] { selectedTarget }));
		MarkTestCompleted();
	}

	[Fact]
	public void SafetyRunZero_Defender_RecordsMandatorySelectionTrace()
	{
		MainRoleType[] roles =
		[
			MainRoleType.Defender,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		];
		var scenario = new SimulationScenario(roles.Length, roles);
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var material = new RunSeedMaterial(
			identity,
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			runNumber: 0);
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
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var defender = players[
			startState.RoleAssignments.Single(assignment =>
				assignment.Role == MainRoleType.Defender).SeatNumber - 1];
		var recorder = new RecordingDecisionStrategy(
			new BaselineRandomDecisionStrategy(
				material,
				startState,
				SimulatorCapability.SafetyScreening.HeadlessResponsePolicy,
				random));

		RespondToCurrentInstruction(builder, recorder);
		RespondToCurrentInstruction(builder, recorder);
		RespondToCurrentInstruction(builder, recorder);
		RespondToCurrentInstruction(builder, recorder);

		var selectionTrace = recorder.Observations.Should()
			.ContainSingle(observation =>
				observation.Instruction.Semantic ==
				ModeratorInstructionSemantic.SelectDefenderTarget)
			.Subject;
		var selection = selectionTrace.Instruction.Should()
			.BeOfType<SelectPlayersInstruction>()
			.Subject;
		selection.CountConstraint.Should().Be(NumberRangeConstraint.Single);
		selection.AffectedPlayerIds.Should().Equal(defender.Id);
		selectionTrace.Response.SelectedPlayerIds.Should().ContainSingle();
		var selectedTarget = selectionTrace.Response.SelectedPlayerIds!.Single();
		selection.SelectablePlayerIds.Should().Contain(selectedTarget);
		builder.GetGameState()!.GameHistoryLog
			.OfType<RecurringRolePowerCommittedLogEntry>()
			.Should().ContainSingle(entry =>
				entry.ActionType == NightActionType.DefenderProtect &&
				entry.ActingPlayerId == defender.Id &&
				entry.TargetIds!.SequenceEqual(new[] { selectedTarget }));
		MarkTestCompleted();
	}

	[Fact]
	public void SafetyRunZero_BigBadWolfWithoutLegalTarget_OmitsSelectorAndHandlesSleepContinue()
	{
		var fixture = CreateSafetyRunZeroBigBadWolfTrace();
		var collectiveVictim = fixture.KnownNonAgents[0];
		foreach (var eliminatedPlayer in fixture.KnownNonAgents.Skip(1))
		{
			fixture.Builder.ArrangeEliminatedPlayer(eliminatedPlayer.Id);
		}

		RespondToCurrentInstruction(fixture.Builder, fixture.Recorder);
		RespondToCurrentInstruction(fixture.Builder, fixture.Recorder);
		RespondToCurrentInstruction(fixture.Builder, fixture.Recorder);
		RespondToCurrentInstruction(fixture.Builder, fixture.Recorder);
		RespondToCurrentInstruction(fixture.Builder, fixture.Recorder);
		var sleep = RespondToCurrentInstruction(
			fixture.Builder,
			fixture.Recorder);
		var finishNight = RespondToCurrentInstruction(
			fixture.Builder,
			fixture.Recorder);

		sleep.Semantic.Should().Be(
			ModeratorInstructionSemantic.PutRoleToSleep);
		sleep.AffectedPlayerIds.Should().Equal(fixture.BigBadWolf.Id);
		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		fixture.Recorder.Observations.Should().NotContain(observation =>
			observation.Instruction.Semantic ==
			ModeratorInstructionSemantic.SelectBigBadWolfTarget);
		var sleepTrace = fixture.Recorder.Observations.Should()
			.ContainSingle(observation =>
				observation.Instruction.Semantic ==
					ModeratorInstructionSemantic.PutRoleToSleep &&
				observation.Instruction.AffectedPlayerIds != null &&
				observation.Instruction.AffectedPlayerIds.Count == 1 &&
				observation.Instruction.AffectedPlayerIds[0] ==
					fixture.BigBadWolf.Id)
			.Subject;
		sleepTrace.Response.InstructionId.Should().Be(
			sleepTrace.Instruction.InstructionId);
		sleepTrace.Response.Type.Should().Be(ExpectedInputType.Continue);
		fixture.Recorder.Observations.Should().Contain(observation =>
			observation.Instruction.Semantic ==
				ModeratorInstructionSemantic.SelectWerewolfVictim &&
			observation.Response.SelectedPlayerIds!.SetEquals(
				new[] { collectiveVictim.Id }));
		fixture.Builder.GetGameState()!.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType ==
				NightActionType.BigBadWolfVictimSelection);
		MarkTestCompleted();
	}

	[Fact]
	public void SafetyRunZero_EliminatedKnownAgent_OmitsEntireBigBadWolfCall()
	{
		var fixture = CreateSafetyRunZeroBigBadWolfTrace();
		var eliminatedKnownAgent = fixture.KnownNonAgents[0];
		fixture.Builder.ArrangeKnownWerewolfFactionAgentGroup(
			fixture.SimpleWerewolf.Id,
			fixture.BigBadWolf.Id,
			eliminatedKnownAgent.Id);
		fixture.Builder.ArrangeEliminatedPlayer(eliminatedKnownAgent.Id);

		RespondToCurrentInstruction(fixture.Builder, fixture.Recorder);
		RespondToCurrentInstruction(fixture.Builder, fixture.Recorder);
		RespondToCurrentInstruction(fixture.Builder, fixture.Recorder);
		RespondToCurrentInstruction(fixture.Builder, fixture.Recorder);
		var finishNight = RespondToCurrentInstruction(
			fixture.Builder,
			fixture.Recorder);

		finishNight.Semantic.Should().Be(
			ModeratorInstructionSemantic.FinishNightActions);
		fixture.Recorder.Observations.Should().NotContain(observation =>
			observation.Instruction.Semantic ==
			ModeratorInstructionSemantic.SelectBigBadWolfTarget);
		fixture.Recorder.Observations.Should().NotContain(observation =>
			(observation.Instruction.Semantic ==
				ModeratorInstructionSemantic.WakeRole ||
			 observation.Instruction.Semantic ==
				ModeratorInstructionSemantic.PutRoleToSleep) &&
			observation.Instruction.AffectedPlayerIds != null &&
			observation.Instruction.AffectedPlayerIds.Count == 1 &&
			observation.Instruction.AffectedPlayerIds[0] ==
				fixture.BigBadWolf.Id);
		fixture.Builder.GetGameState()!.GameHistoryLog
			.OfType<NightActionLogEntry>()
			.Should().NotContain(entry =>
				entry.ActionType ==
				NightActionType.BigBadWolfVictimSelection);
		MarkTestCompleted();
	}

	[Fact]
	public void ExecuteBatch_WithFrozenFullProbabilityFourRoleScenario_CompletesWithoutSafetyOnlySemantics()
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
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.FullProbability.Identity);
		var recorders = new List<RecordingDecisionStrategy>();
		var executor = new SimulationExecutor(
			SimulationStartStateDeriver.Derive,
			strategy =>
			{
				var recorder = new RecordingDecisionStrategy(strategy);
				recorders.Add(recorder);
				return new HeadlessGameDriver(recorder);
			},
			SimulationExecutor.AdaptTerminalEvidence);

		var batch = executor.ExecuteBatch(
			scenario,
			SimulatorCapability.FullProbability,
			identity,
			runCount: 1_000);

		batch.Records.Should().HaveCount(1_000);
		batch.CompletedRunCount.Should().Be(1_000);
		batch.IncompleteRunCount.Should().Be(0);
		recorders.Should().HaveCount(1_000);
		var safetyOnlySemantics = new[]
		{
			ModeratorInstructionSemantic.ConductDayVote,
			ModeratorInstructionSemantic.EstablishStutteringJudgeSignal,
			ModeratorInstructionSemantic.ObserveStutteringJudgeSignal,
			ModeratorInstructionSemantic.SelectDefenderTarget
		};
		recorders.SelectMany(recorder => recorder.ObservedSemantics)
			.Should().NotContain(semantic => safetyOnlySemantics.Contains(semantic));
		MarkTestCompleted();
	}

	[Fact]
	public void ExecuteBatch_UsesSelectedCapabilityIdentityAndNumbersEachBatchFromZero()
	{
		var scenario = CreateKnownDawnOracle();
		var safetyIdentity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var probabilityIdentity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.FullProbability.Identity);
		var executor = new SimulationExecutor();

		var safety = executor.ExecuteBatch(
			scenario,
			SimulatorCapability.SafetyScreening,
			safetyIdentity,
			runCount: 2);
		var probability = executor.ExecuteBatch(
			scenario,
			SimulatorCapability.FullProbability,
			probabilityIdentity,
			runCount: 2);

		safety.SimulatorProfile.Should().Be(SimulatorCapability.SafetyScreening.Identity);
		probability.SimulatorProfile.Should().Be(SimulatorCapability.FullProbability.Identity);
		safety.Records.Select(run => run.RunSeedMaterial.RunNumber).Should().Equal(0, 1);
		probability.Records.Select(run => run.RunSeedMaterial.RunNumber).Should().Equal(0, 1);
		safety.Records[0].RunSeedMaterial.Should().NotBe(probability.Records[0].RunSeedMaterial);
		MarkTestCompleted();
	}

	[Fact]
	public void Execute_WithKnownDawnOracle_ReturnsCompletedSemanticEvidence()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.FullProbability.Identity);
		var executor = new SimulationExecutor();

		var run = executor.Execute(
			scenario,
			SimulatorCapability.FullProbability,
			identity,
			runNumber: 0);

		var completed = run.Should().BeOfType<CompletedSimulationRun>().Subject;
		completed.RunSeedMaterial.Should().Be(
			new RunSeedMaterial(identity, BaselineRandomDecisionStrategy.Identity, 0));
		completed.GameResult.Should().Be(new SingleFactionGameResult(Faction.Werewolf));
		completed.EndingTurn.Should().Be(1);
		completed.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);
		MarkTestCompleted();
	}

	[Fact]
	public void Execute_WithSeerScenario_CompletesReachablePrivateFeedbackConfirmation()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var identity = CreateIdentity(scenario);

		var first = new SimulationExecutor().Execute(
			scenario,
			SimulatorCapability.FullProbability,
			identity,
			runNumber: 0);
		var replay = new SimulationExecutor().Execute(
			scenario,
			SimulatorCapability.FullProbability,
			identity,
			runNumber: 0);

		first.Should().BeOfType<CompletedSimulationRun>();
		replay.Should().Be(first);
		MarkTestCompleted();
	}

	[Fact]
	public void Execute_WhenDerivationRequiresUnknownLiveFactionFacts_ReturnsExactIncompleteAndEvaluationCannotComplete()
	{
		const long runNumber = 37;
		MainRoleType[] roles =
		[
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		];
		var scenario = new SimulationScenario(roles.Length, roles);
		var identity = CreateIdentity(scenario);
		var service = new GameService();
		var start = service.StartNewGame(new GameSessionConfig(
			["Ana", "Bruno", "Carla", "Diana", "Eva"],
			roles.ToList()));
		var liveSession = service.GetGameStateView(start.GameGuid)!;
		var livePlayer = liveSession.GetPlayers().First();
		var driverFactoryCalls = 0;
		var executor = new SimulationExecutor(
			(material, capability, _) =>
			{
				liveSession.RequireKnownFactionBeneficiary(livePlayer.Id);
				return SimulationStartStateDeriver.Derive(material, capability);
			},
			strategy =>
			{
				driverFactoryCalls++;
				return new HeadlessGameDriver(strategy);
			},
			SimulationExecutor.AdaptTerminalEvidence);
		var expectedMaterial = new RunSeedMaterial(
			identity,
			BaselineRandomDecisionStrategy.Identity,
			runNumber);

		liveSession.GetFactionBeneficiaryKnowledge(livePlayer.Id).Should()
			.Be(FactionBeneficiaryKnowledge.Unknown);
		var requireLiveFact = () =>
			liveSession.RequireKnownFactionBeneficiary(livePlayer.Id);
		requireLiveFact.Should()
			.ThrowExactly<InvalidOperationException>()
			.WithMessage("Required Faction facts are not ready.");
		new SimulationExecutor().Execute(
				scenario,
				SimulatorCapability.FullProbability,
				identity,
				runNumber: 0)
			.Should().BeOfType<CompletedSimulationRun>();

		var run = executor.Execute(
			scenario,
			SimulatorCapability.FullProbability,
			identity,
			runNumber);

		run.Should().Be(new IncompleteSimulationRun(expectedMaterial));
		driverFactoryCalls.Should().Be(0);

		var evaluator = new TerminalLobbyEvaluator(
			(batchScenario, batchCapability, batchIdentity, count, cancellationToken) =>
				executor.ExecuteBatch(
					batchScenario,
					batchCapability,
					batchIdentity,
					count,
					cancellationToken));

		var evaluation = evaluator.Evaluate(
			scenario,
			SimulatorCapability.FullProbability,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

		evaluation.Should().BeOfType<CouldNotEvaluateLobbyEvaluation>();
		driverFactoryCalls.Should().Be(0);
		MarkTestCompleted();
	}

	[Fact]
	public void Execute_WithVillagerVillager_UsesSafetyCapabilityAndCompletesDeterministicReplay()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.VillagerVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var executor = new SimulationExecutor();

		var first = executor.Execute(
			scenario,
			SimulatorCapability.SafetyScreening,
			identity,
			runNumber: 17);
		var replay = executor.Execute(
			scenario,
			SimulatorCapability.SafetyScreening,
			identity,
			runNumber: 17);

		first.Should().BeOfType<CompletedSimulationRun>();
		first.RunSeedMaterial.CompatibilityIdentity.Profile.Should()
			.Be(SimulatorCapability.SafetyScreening.Identity);
		replay.Should().Be(first);
		MarkTestCompleted();
	}

	[Fact]
	public void ExecuteBatch_WithDifferentScheduling_ReturnsAscendingStableSourceEvidenceAndCounts()
	{
		var scenario = CreateKnownDawnOracle();
		var identity = CreateIdentity(scenario);
		var executor = new SimulationExecutor();

		SimulationBatchSourceEvidence sequential = executor.ExecuteBatch(
			scenario,
			SimulatorCapability.FullProbability,
			identity,
			runCount: 8,
			degreeOfParallelism: 1);
		SimulationBatchSourceEvidence parallel = executor.ExecuteBatch(
			scenario,
			SimulatorCapability.FullProbability,
			identity,
			runCount: 8,
			degreeOfParallelism: 4);

		sequential.CanonicalScenario.Should().Be(scenario.ToCanonical());
		sequential.SimulatorProfile.Should().Be(SimulatorCapability.FullProbability.Identity);
		sequential.DecisionStrategy.Should().Be(BaselineRandomDecisionStrategy.Identity);
		sequential.Records.Select(record => record.RunSeedMaterial.RunNumber)
			.Should().Equal(0, 1, 2, 3, 4, 5, 6, 7);
		sequential.Records.Should().Equal(parallel.Records);
		sequential.CompletedRunCount.Should().Be(8);
		sequential.IncompleteRunCount.Should().Be(0);
		(sequential.CompletedRunCount + sequential.IncompleteRunCount)
			.Should().Be(sequential.Records.Count);
		MarkTestCompleted();
	}

	[Fact]
	public void ExecuteBatch_WithControlledIncompleteRuns_ReportsCountsMatchingEveryRecord()
	{
		var scenario = CreateKnownDawnOracle();
		var identity = CreateIdentity(scenario);
		var executor = new SimulationExecutor(
			SimulationStartStateDeriver.Derive,
			strategy => new HeadlessGameDriver(strategy),
			(material, history) => material.RunNumber % 2 == 0
				? SimulationExecutor.AdaptTerminalEvidence(material, history)
				: new IncompleteSimulationRun(material));

		var batch = executor.ExecuteBatch(
			scenario,
			SimulatorCapability.FullProbability,
			identity,
			runCount: 4);

		batch.Records.Should().HaveCount(4);
		batch.Records.Should().SatisfyRespectively(
			record => record.Should().BeOfType<CompletedSimulationRun>(),
			record => record.Should().BeOfType<IncompleteSimulationRun>(),
			record => record.Should().BeOfType<CompletedSimulationRun>(),
			record => record.Should().BeOfType<IncompleteSimulationRun>());
		batch.CompletedRunCount.Should().Be(2);
		batch.IncompleteRunCount.Should().Be(2);
		MarkTestCompleted();
	}

	[Fact]
	public void Execute_WithPreCancelledToken_PropagatesBeforeDerivationWithoutEvidence()
	{
		var scenario = CreateKnownDawnOracle();
		var identity = CreateIdentity(scenario);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var derivationCount = 0;
		var executor = new SimulationExecutor(
			(material, capability, _) =>
			{
				derivationCount++;
				return SimulationStartStateDeriver.Derive(material, capability);
			},
			strategy => new HeadlessGameDriver(strategy),
			SimulationExecutor.AdaptTerminalEvidence);
		SimulationRun? runEvidence = null;

		Action executeRun = () => runEvidence = executor.Execute(
			scenario,
			SimulatorCapability.FullProbability,
			identity,
			runNumber: 0,
			cancellation.Token);

		executeRun.Should().Throw<OperationCanceledException>();
		derivationCount.Should().Be(0);
		runEvidence.Should().BeNull();
		MarkTestCompleted();
	}

	[Fact]
	public void ExecuteBatch_WithPreCancelledToken_PropagatesBeforeDerivationWithoutEvidence()
	{
		var scenario = CreateKnownDawnOracle();
		var identity = CreateIdentity(scenario);
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var derivationCount = 0;
		var executor = new SimulationExecutor(
			(material, capability, _) =>
			{
				derivationCount++;
				return SimulationStartStateDeriver.Derive(material, capability);
			},
			strategy => new HeadlessGameDriver(strategy),
			SimulationExecutor.AdaptTerminalEvidence);
		SimulationBatchSourceEvidence? batchEvidence = null;

		Action executeBatch = () => batchEvidence = executor.ExecuteBatch(
			scenario,
			SimulatorCapability.FullProbability,
			identity,
			runCount: 2,
			cancellation.Token);

		executeBatch.Should().Throw<OperationCanceledException>();
		derivationCount.Should().Be(0);
		batchEvidence.Should().BeNull();
		MarkTestCompleted();
	}

	[Fact]
	public void Execute_WhenCancelledBetweenInstructions_PropagatesCancellationWithoutRunEvidence()
	{
		var scenario = CreateKnownDawnOracle();
		var identity = CreateIdentity(scenario);
		using var cancellation = new CancellationTokenSource();
		var executor = CreateExecutor((checkpoint, _) =>
		{
			if (checkpoint == SimulationExecutionCheckpoint.BetweenModeratorInstructions)
			{
				cancellation.Cancel();
			}
		});
		SimulationRun? evidence = null;

		Action execute = () => evidence = executor.Execute(
			scenario,
			SimulatorCapability.FullProbability,
			identity,
			runNumber: 0,
			cancellation.Token);

		execute.Should().Throw<OperationCanceledException>();
		evidence.Should().BeNull();
		MarkTestCompleted();
	}

	[Fact]
	public void ExecuteBatch_WhenCancelledBetweenAttempts_PropagatesCancellationWithoutBatchEvidence()
	{
		var scenario = CreateKnownDawnOracle();
		var identity = CreateIdentity(scenario);
		using var cancellation = new CancellationTokenSource();
		var completedAttemptBoundaryReached = false;
		var executor = CreateExecutor((checkpoint, runNumber) =>
		{
			if (checkpoint == SimulationExecutionCheckpoint.BetweenBatchAttempts && runNumber == 1)
			{
				completedAttemptBoundaryReached = true;
				cancellation.Cancel();
			}
		});
		SimulationBatchSourceEvidence? evidence = null;

		Action execute = () => evidence = executor.ExecuteBatch(
			scenario,
			SimulatorCapability.FullProbability,
			identity,
			runCount: 3,
			degreeOfParallelism: 1,
			cancellation.Token);

		execute.Should().Throw<OperationCanceledException>();
		completedAttemptBoundaryReached.Should().BeTrue();
		evidence.Should().BeNull();
		MarkTestCompleted();
	}

	[Fact]
	public void Execute_WithUnadmittedOrIdentityMismatchedInput_RejectsBeforeStartStateDerivation()
	{
		var derivationCount = 0;
		var executor = new SimulationExecutor(
			(material, capability, _) =>
			{
				derivationCount++;
				return SimulationStartStateDeriver.Derive(material, capability);
			},
			strategy => new HeadlessGameDriver(strategy),
			SimulationExecutor.AdaptTerminalEvidence);
		var rulesInvalid = new SimulationScenario(
			5,
			[
				MainRoleType.Seer,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var appUnsupported = new SimulationScenario(
			5,
			[
				MainRoleType.Cupid,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var simulatorUnsupported = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.Seer,
				MainRoleType.WildChild,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			],
			new ActorSetupCards(
				[MainRoleType.Cupid, MainRoleType.Defender, MainRoleType.Elder]));
		var supported = CreateKnownDawnOracle();
		var mismatchedIdentity = new SimulationCompatibilityIdentity(
			supported.ToCanonical(),
			new SimulatorProfileIdentity("core-simulator", "2"));
		var angelSupported = new SimulationScenario(
			5,
			[
				MainRoleType.Angel,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var legacyAngelIdentity = new SimulationCompatibilityIdentity(
			angelSupported.ToCanonical(),
			new SimulatorProfileIdentity("core-simulator", "1"));
		var attempts = new Action[]
		{
			() => executor.Execute(
				rulesInvalid,
				SimulatorCapability.FullProbability,
				CreateIdentity(rulesInvalid),
				0),
			() => executor.Execute(
				appUnsupported,
				SimulatorCapability.FullProbability,
				CreateIdentity(appUnsupported),
				0),
			() => executor.Execute(
				simulatorUnsupported,
				SimulatorCapability.FullProbability,
				CreateIdentity(simulatorUnsupported),
				0),
			() => executor.Execute(
				supported,
				SimulatorCapability.FullProbability,
				mismatchedIdentity,
				0),
			() => executor.Execute(
				angelSupported,
				SimulatorCapability.SafetyScreening,
				legacyAngelIdentity,
				0)
		};

		foreach (var attempt in attempts)
		{
			attempt.Should().Throw<ArgumentException>();
		}

		derivationCount.Should().Be(0);
		MarkTestCompleted();
	}

	[Fact]
	public void Execute_WithControlledExecutionFailures_ReturnsReplayableIncompleteEvidence()
	{
		var scenario = CreateKnownDawnOracle();
		var identity = CreateIdentity(scenario);
		var expectedMaterial = new RunSeedMaterial(
			identity,
			BaselineRandomDecisionStrategy.Identity,
			runNumber: 23);
		var executors = new[]
		{
			new SimulationExecutor(
				(_, _, _) => throw new InvalidOperationException(),
				strategy => new HeadlessGameDriver(strategy),
				SimulationExecutor.AdaptTerminalEvidence),
			new SimulationExecutor(
				(_, _, _) => throw new OperationCanceledException(),
				strategy => new HeadlessGameDriver(strategy),
				SimulationExecutor.AdaptTerminalEvidence),
			new SimulationExecutor(
				SimulationStartStateDeriver.Derive,
				_ => throw new InvalidOperationException(),
				SimulationExecutor.AdaptTerminalEvidence),
			new SimulationExecutor(
				SimulationStartStateDeriver.Derive,
				strategy => new HeadlessGameDriver(strategy, maxProcessedInstructionCount: 0),
				SimulationExecutor.AdaptTerminalEvidence),
			new SimulationExecutor(
				SimulationStartStateDeriver.Derive,
				strategy => new HeadlessGameDriver(strategy),
				(_, _) => throw new InvalidOperationException())
		};

		foreach (var executor in executors)
		{
			var run = executor.Execute(
				scenario,
				SimulatorCapability.FullProbability,
				identity,
				runNumber: 23);

			run.Should().Be(new IncompleteSimulationRun(expectedMaterial));
		}

		MarkTestCompleted();
	}

	[Fact]
	public void Execute_WithDiagnosedWildChildReplay_CompletesOnTurnTwoOrLater()
	{
		var scenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.WildChild,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var identity = CreateIdentity(scenario);
		var run = new SimulationExecutor().Execute(
			scenario,
			SimulatorCapability.FullProbability,
			identity,
			runNumber: 11);

		var completed = run.Should().BeOfType<CompletedSimulationRun>().Subject;
		completed.EndingTurn.Should().BeGreaterThanOrEqualTo(2);
		MarkTestCompleted();
	}

	[Fact]
	public void AdaptTerminalEvidence_WithDawnOracle_UsesCurrentTurn()
	{
		var material = new RunSeedMaterial(
			CreateIdentity(CreateKnownDawnOracle()),
			BaselineRandomDecisionStrategy.Identity,
			runNumber: 29);
		GameLogEntryBase[] history =
		[
			CreateTransition(GamePhase.Dawn, GamePhase.Day, turnNumber: 2),
			CreateVictory(new SingleFactionGameResult(Faction.Werewolf), VictoryCheckWindow.Dawn, GamePhase.Day, turnNumber: 2)
		];

		var run = SimulationExecutor.AdaptTerminalEvidence(material, history);

		var completed = run.Should().BeOfType<CompletedSimulationRun>().Subject;
		completed.GameResult.Should().Be(new SingleFactionGameResult(Faction.Werewolf));
		completed.EndingTurn.Should().Be(2);
		completed.VictoryCheckWindow.Should().Be(VictoryCheckWindow.Dawn);
		MarkTestCompleted();
	}

	[Fact]
	public void AdaptTerminalEvidence_WithPreNightOracle_UsesResolvedPriorTurn()
	{
		var material = new RunSeedMaterial(
			CreateIdentity(CreateKnownDawnOracle()),
			BaselineRandomDecisionStrategy.Identity,
			runNumber: 31);
		GameLogEntryBase[] history =
		[
			CreateTransition(GamePhase.Day, GamePhase.Night, turnNumber: 2),
			CreateVictory(new SingleFactionGameResult(Faction.Villager), VictoryCheckWindow.PreNight, GamePhase.Night, turnNumber: 2)
		];

		var run = SimulationExecutor.AdaptTerminalEvidence(material, history);

		var completed = run.Should().BeOfType<CompletedSimulationRun>().Subject;
		completed.GameResult.Should().Be(new SingleFactionGameResult(Faction.Villager));
		completed.EndingTurn.Should().Be(1);
		completed.VictoryCheckWindow.Should().Be(VictoryCheckWindow.PreNight);
		MarkTestCompleted();
	}

	[Fact]
	public void AdaptTerminalEvidence_WithMissingDuplicateUnsupportedOrImpossibleSignals_ReturnsIncompleteEvidence()
	{
		var material = new RunSeedMaterial(
			CreateIdentity(CreateKnownDawnOracle()),
			BaselineRandomDecisionStrategy.Identity,
			runNumber: 37);
		var validTransition = CreateTransition(GamePhase.Dawn, GamePhase.Day, turnNumber: 1);
		var validVictory = CreateVictory(new SingleFactionGameResult(Faction.Werewolf), VictoryCheckWindow.Dawn, GamePhase.Day, turnNumber: 1);
		GameLogEntryBase[][] histories =
		[
			[],
			[validTransition, validVictory, validVictory],
			[
				validTransition,
				CreateVictory(new SingleFactionGameResult(Faction.Werewolf), (VictoryCheckWindow)42, GamePhase.Day, turnNumber: 1)
			],
			[validVictory],
			[
				validTransition,
				CreateVictory(new SingleFactionGameResult(Faction.Werewolf), VictoryCheckWindow.Dawn, GamePhase.Night, turnNumber: 1)
			],
			[
				CreateTransition(GamePhase.Day, GamePhase.Day, turnNumber: 1),
				validVictory
			],
			[
				CreateTransition(GamePhase.Night, GamePhase.Day, turnNumber: 1),
				validVictory
			],
			[
				CreateTransition(GamePhase.Night, GamePhase.Night, turnNumber: 2),
				CreateVictory(new SingleFactionGameResult(Faction.Villager), VictoryCheckWindow.PreNight, GamePhase.Night, turnNumber: 2)
			],
			[
				CreateTransition(GamePhase.Dawn, GamePhase.Night, turnNumber: 2),
				CreateVictory(new SingleFactionGameResult(Faction.Villager), VictoryCheckWindow.PreNight, GamePhase.Night, turnNumber: 2)
			],
			[
				CreateTransition(GamePhase.Day, GamePhase.Night, turnNumber: 1),
				CreateVictory(new SingleFactionGameResult(Faction.Villager), VictoryCheckWindow.PreNight, GamePhase.Night, turnNumber: 1)
			]
		];

		foreach (var history in histories)
		{
			SimulationExecutor.AdaptTerminalEvidence(material, history)
				.Should().Be(new IncompleteSimulationRun(material));
		}

		MarkTestCompleted();
	}

	private static SimulationScenario CreateKnownDawnOracle() =>
		new(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);

	private sealed class PreserveRoleHoldersUntilNightThreeStrategy
		: IModeratorDecisionStrategy
	{
		private readonly IModeratorDecisionStrategy _inner;
		private readonly MainRoleType _role;

		internal PreserveRoleHoldersUntilNightThreeStrategy(
			IModeratorDecisionStrategy inner,
			MainRoleType role)
		{
			ArgumentNullException.ThrowIfNull(inner);
			_inner = inner;
			_role = role;
		}

		internal List<ModeratorInstructionSemantic> ObservedSemantics { get; } = [];
		internal int? LivingRoleHolderCountAtCommunication { get; private set; }

		public ModeratorResponse CreateResponse(
			ModeratorInstruction instruction,
			IGameSession session)
		{
			ObservedSemantics.Add(instruction.Semantic);
			if (instruction.Semantic ==
			    ModeratorInstructionSemantic.CommunicateAsRoleHolders)
			{
				LivingRoleHolderCountAtCommunication = session.GetPlayers().Count(player =>
					player.State.Health == PlayerHealth.Alive &&
					player.State.CurrentRole == _role);
			}

			return instruction switch
			{
				SelectPlayersInstruction
				{
					Semantic: ModeratorInstructionSemantic.SelectWerewolfVictim
				} victim => victim.CreateResponse(
					session.GetPlayers()
						.Where(player =>
							victim.SelectablePlayerIds.Contains(player.Id) &&
							player.State.CurrentRole != _role &&
							player.State.ModeratorKnownRole != _role)
						.OrderBy(player => player.Id)
						.Take(1)
						.Select(player => player.Id)
						.ToHashSet()),
				SelectPlayersInstruction
				{
					Semantic: ModeratorInstructionSemantic.RecordDayVote
				} dayVote => dayVote.CreateResponse([]),
				_ => _inner.CreateResponse(instruction, session)
			};
		}
	}

	private (
		GameTestBuilder Builder,
		RecordingDecisionStrategy Recorder,
		IPlayer SimpleWerewolf,
		IPlayer BigBadWolf,
		IPlayer[] KnownNonAgents)
		CreateSafetyRunZeroBigBadWolfTrace()
	{
		MainRoleType[] roles =
		[
			MainRoleType.SimpleWerewolf,
			MainRoleType.BigBadWolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		];
		var scenario = new SimulationScenario(roles.Length, roles);
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);
		var material = new RunSeedMaterial(
			identity,
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			runNumber: 0);
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
		var players = builder.GetGameState()!.GetPlayers().ToArray();
		var simpleWerewolf = players[
			startState.RoleAssignments.Single(assignment =>
				assignment.Role == MainRoleType.SimpleWerewolf).SeatNumber - 1];
		var bigBadWolf = players[
			startState.RoleAssignments.Single(assignment =>
				assignment.Role == MainRoleType.BigBadWolf).SeatNumber - 1];
		var knownNonAgents = startState.FactionFacts
			.Where(facts =>
				facts.GetAgentKnowledge(Faction.Werewolf) ==
				FactionAgentKnowledge.KnownNonAgent)
			.Select(facts => players[facts.SeatNumber - 1])
			.ToArray();
		builder.ArrangeKnownRole(bigBadWolf.Id, MainRoleType.BigBadWolf);
		builder.ArrangeKnownWerewolfFactionAgentGroup(
			simpleWerewolf.Id,
			bigBadWolf.Id);
		var recorder = new RecordingDecisionStrategy(
			new BaselineRandomDecisionStrategy(
				material,
				startState,
				SimulatorCapability.SafetyScreening.HeadlessResponsePolicy,
				random));
		return (
			builder,
			recorder,
			simpleWerewolf,
			bigBadWolf,
			knownNonAgents);
	}

	private static ModeratorInstruction RespondToCurrentInstruction(
		GameTestBuilder builder,
		RecordingDecisionStrategy recorder)
	{
		var instruction = builder.GetCurrentInstruction();
		instruction.Should().NotBeNull();
		var response = recorder.CreateResponse(
			instruction!,
			builder.GetGameState()!);
		var result = builder.Process(response);
		result.IsSuccess.Should().BeTrue();
		result.ModeratorInstruction.Should().NotBeNull();
		return result.ModeratorInstruction!;
	}

	private sealed class RecordingDecisionStrategy : IModeratorDecisionStrategy
	{
		private readonly IModeratorDecisionStrategy _inner;

		internal RecordingDecisionStrategy(IModeratorDecisionStrategy inner)
		{
			ArgumentNullException.ThrowIfNull(inner);
			_inner = inner;
		}

		internal List<ModeratorInstructionSemantic> ObservedSemantics { get; } = [];
		internal List<(
			ModeratorInstruction Instruction,
			ModeratorResponse Response,
			int TurnNumber)> Observations { get; } = [];

		public ModeratorResponse CreateResponse(
			ModeratorInstruction instruction,
			IGameSession session)
		{
			ObservedSemantics.Add(instruction.Semantic);
			var response = _inner.CreateResponse(instruction, session);
			Observations.Add((instruction, response, session.TurnNumber));
			return response;
		}
	}

	private static SimulationScenario CreateOfferBearingThiefScenario()
	{
		MainRoleType[] dealPool =
		[
			MainRoleType.Thief,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		];
		return new SimulationScenario(
			playerCount: 5,
			roleCompositionCards: dealPool.Concat(
				[MainRoleType.Seer, MainRoleType.Defender]),
			dealPoolCards: dealPool,
			offer1Role: MainRoleType.Seer,
			offer2Role: MainRoleType.Defender);
	}

	private static SimulationCompatibilityIdentity CreateIdentity(SimulationScenario scenario) =>
		new(scenario.ToCanonical(), SimulatorCapability.FullProbability.Identity);

	private static SimulationExecutor CreateExecutor(
		Action<SimulationExecutionCheckpoint, long> checkpoint) =>
		new(
			SimulationStartStateDeriver.Derive,
			strategy => new HeadlessGameDriver(strategy),
			SimulationExecutor.AdaptTerminalEvidence,
			checkpoint);

	private static PhaseTransitionLogEntry CreateTransition(
		GamePhase previousPhase,
		GamePhase currentPhase,
		int turnNumber) =>
		new()
		{
			Timestamp = DateTimeOffset.UnixEpoch,
			TurnNumber = turnNumber,
			PreviousPhase = previousPhase,
			CurrentPhase = currentPhase
		};

	private static VictoryConditionMetLogEntry CreateVictory(
		GameResult gameResult,
		VictoryCheckWindow victoryCheckWindow,
		GamePhase currentPhase,
		int turnNumber) =>
		new()
		{
			Timestamp = DateTimeOffset.UnixEpoch,
			TurnNumber = turnNumber,
			CurrentPhase = currentPhase,
			GameResult = gameResult,
			VictoryCheckWindow = victoryCheckWindow
		};
}
