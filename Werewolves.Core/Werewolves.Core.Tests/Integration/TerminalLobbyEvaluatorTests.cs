using FluentAssertions;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;
using Werewolves.Core.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Werewolves.Core.Tests.Integration;

public class TerminalLobbyEvaluatorTests : DiagnosticTestBase
{
	public TerminalLobbyEvaluatorTests(ITestOutputHelper output) : base(output)
	{
	}

	[Fact]
	public void Evaluate_FullProbabilityDepthWithSafetyCapabilityRejectsBeforeExecution()
	{
		var calls = 0;
		var evaluator = new TerminalLobbyEvaluator((_, _, _, _, _) =>
		{
			calls++;
			throw new InvalidOperationException();
		});

		var act = () => evaluator.Evaluate(
			SupportedScenario(),
			SimulatorCapability.SafetyScreening,
			LobbyEvaluationDepth.FullProbability);

		act.Should().Throw<ArgumentException>().WithParameterName("depth");
		calls.Should().Be(0);
		MarkTestCompleted();
	}

	[Fact]
	public void Evaluate_RulesInvalid_ReturnsRulesGateResultWithoutExecuting()
	{
		var calls = 0;
		var evaluator = new TerminalLobbyEvaluator((_, _, _, _) =>
		{
			calls++;
			throw new InvalidOperationException();
		});
		var scenario = Scenario(MainRoleType.Seer, MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager, MainRoleType.SimpleVillager, MainRoleType.SimpleVillager);

		var result = evaluator.Evaluate(
			scenario,
			SimulatorCapability.SafetyScreening,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

		var stopped = result.Should().BeOfType<RulesInvalidLobbyEvaluation>().Subject.RulesValidity;
		stopped.Scenario.Should().BeSameAs(scenario);
		stopped.IsValid.Should().BeFalse();
		calls.Should().Be(0);
		MarkTestCompleted();
	}

	[Fact]
	public void Evaluate_AppUnsupported_ReturnsAppGateResultWithoutExecuting()
	{
		var calls = 0;
		var evaluator = new TerminalLobbyEvaluator((_, _, _, _) =>
		{
			calls++;
			throw new InvalidOperationException();
		});
		var scenario = Scenario(MainRoleType.Cupid, MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager, MainRoleType.SimpleVillager, MainRoleType.SimpleVillager);

		var result = evaluator.Evaluate(
			scenario,
			SimulatorCapability.SafetyScreening,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

		var stopped = result.Should().BeOfType<AppUnsupportedLobbyEvaluation>().Subject.AppSupport;
		stopped.Scenario.Should().BeSameAs(scenario);
		stopped.IsSupported.Should().BeFalse();
		stopped.UnsupportedRoles.Should().Contain(MainRoleType.Cupid);
		calls.Should().Be(0);
		MarkTestCompleted();
	}

	[Fact]
	public void Evaluate_SimulatorUnsupported_ReturnsSimulatorGateResultWithoutExecuting()
	{
		var calls = 0;
		var evaluator = new TerminalLobbyEvaluator((_, _, _, _) =>
		{
			calls++;
			throw new InvalidOperationException();
		});
		var scenario = new SimulationScenario(
			5,
			[MainRoleType.SimpleWerewolf, MainRoleType.Seer, MainRoleType.WildChild,
				MainRoleType.SimpleVillager, MainRoleType.SimpleVillager],
			new ActorSetupCards([MainRoleType.Cupid, MainRoleType.Defender, MainRoleType.Elder]));

		var result = evaluator.Evaluate(
			scenario,
			SimulatorCapability.SafetyScreening,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

		var stopped = result.Should().BeOfType<SimulatorUnsupportedLobbyEvaluation>().Subject.SimulatorSupport;
		stopped.Scenario.Should().BeSameAs(scenario);
		stopped.IsSupported.Should().BeFalse();
		stopped.HasUnsupportedActorSetupCards.Should().BeTrue();
		calls.Should().Be(0);
		MarkTestCompleted();
	}

	[Fact]
	public void Evaluate_DegenerateScreeningOnly_AlreadyDecided_ReturnsStructuredResultWithoutExecuting()
	{
		var calls = 0;
		var evaluator = new TerminalLobbyEvaluator((_, _, _, _) =>
		{
			calls++;
			throw new InvalidOperationException();
		});
		var scenario = Scenario(MainRoleType.SimpleWerewolf, MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf, MainRoleType.SimpleVillager, MainRoleType.SimpleVillager);

		var result = evaluator.Evaluate(
			scenario,
			SimulatorCapability.SafetyScreening,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

		var decided = result.Should().BeOfType<AlreadyDecidedTerminalEvaluation>().Subject;
		decided.GameResult.Should().Be(new SingleFactionGameResult(Faction.Werewolf));
		decided.Reason.Should().Be(AlreadyDecidedReason.WerewolfControlShortcut);
		calls.Should().Be(0);
		MarkTestCompleted();
	}

	[Fact]
	public void Evaluate_DegenerateScreeningOnly_AllTurnOneScreening_ReturnsDegenerateAndStops()
	{
		var calls = new List<int>();
		var evaluator = new TerminalLobbyEvaluator((scenario, identity, count, _) =>
		{
			calls.Add(count);
			return Batch(scenario, identity, count, _ => (1, VictoryCheckWindow.Dawn));
		});

		var result = evaluator.Evaluate(
			SupportedScenario(),
			SimulatorCapability.SafetyScreening,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

		result.Should().BeOfType<DegenerateTerminalEvaluation>().Subject
			.ScreeningEvidence.AttemptedRunCount.Should().Be(1_000);
		calls.Should().Equal(1_000);
		MarkTestCompleted();
	}

	[Fact]
	public void Evaluate_DegenerateScreeningOnly_WhenOneRunEndsLater_ReturnsScreeningPassedAndStops()
	{
		var calls = new List<int>();
		var evaluator = new TerminalLobbyEvaluator((scenario, identity, count, _) =>
		{
			calls.Add(count);
			return Batch(scenario, identity, count, run => run == count - 1
				? (2, VictoryCheckWindow.PreNight)
				: (1, VictoryCheckWindow.Dawn));
		});

		var result = evaluator.Evaluate(
			SupportedScenario(),
			SimulatorCapability.SafetyScreening,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

		result.Should().BeOfType<ScreeningPassedLobbyEvaluation>();
		calls.Should().Equal(1_000);
		MarkTestCompleted();
	}

	[Fact]
	public void Evaluate_DegenerateScreeningOnly_VillagerVillagerCompletesTheRequiredRealScreen()
	{
		var scenario = Scenario(
			MainRoleType.SimpleWerewolf,
			MainRoleType.Seer,
			MainRoleType.VillagerVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);

		var result = new TerminalLobbyEvaluator().Evaluate(
			scenario,
			SimulatorCapability.SafetyScreening,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

		(result is DegenerateTerminalEvaluation or ScreeningPassedLobbyEvaluation)
			.Should().BeTrue();
		MarkTestCompleted();
	}

	[Fact]
	public void Evaluate_OneLaterScreeningRun_UsesSharedIdentityAndPublishesCompleteProbability()
	{
		var calls = new List<(int Count, SimulationCompatibilityIdentity Identity)>();
		var evaluator = new TerminalLobbyEvaluator((scenario, identity, count, _) =>
		{
			calls.Add((count, identity));
			return Batch(scenario, identity, count, run => count == 1_000 && run == 999
				? (2, VictoryCheckWindow.PreNight)
				: (1, VictoryCheckWindow.Dawn));
		});

		var result = evaluator.Evaluate(
			SupportedScenario(),
			SimulatorCapability.FullProbability,
			LobbyEvaluationDepth.FullProbability);

		var probability = result.Should().BeOfType<ProbabilityTerminalEvaluation>().Subject;
		calls.Select(call => call.Count).Should().Equal(1_000, 10_000);
		calls[0].Identity.Should().Be(calls[1].Identity);
		probability.Evidence.CompletedRunCount.Should().Be(10_000);
		probability.Evidence.PossibleGameResults.Should().Equal(
			new SingleFactionGameResult(Faction.Villager),
			new SingleFactionGameResult(Faction.Werewolf),
			new NoWinnerGameResult());
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Evaluate_IncompleteRequiredBatch_ReturnsCouldNotEvaluate(bool screening)
	{
		var evaluator = new TerminalLobbyEvaluator((scenario, identity, count, _) =>
		{
			var batch = Batch(scenario, identity, count, run => count == 1_000 && run == count - 1
				? (2, VictoryCheckWindow.PreNight)
				: (1, VictoryCheckWindow.Dawn));
			if ((screening && count == 1_000) || (!screening && count == 10_000))
			{
				var records = batch.Records.ToArray();
				records[^1] = new IncompleteSimulationRun(records[^1].RunSeedMaterial);
				return new SimulationBatchSourceEvidence(
					scenario.ToCanonical(), identity.Profile, BaselineRandomDecisionStrategy.Identity, records);
			}
			return batch;
		});

		evaluator.Evaluate(
				SupportedScenario(),
				SimulatorCapability.FullProbability,
				LobbyEvaluationDepth.FullProbability)
			.Should().BeOfType<CouldNotEvaluateLobbyEvaluation>();
		MarkTestCompleted();
	}

	[Fact]
	public void Evaluate_PolicyMissingReachableStartGameSemantic_UsesOneRealIncompleteScreeningBatch()
	{
		const long runNumber = 41;
		var scenario = SupportedScenario();
		var capability = FullProbabilityWithoutStartGame();
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			capability.Identity);
		var expectedMaterial = new RunSeedMaterial(
			identity,
			BaselineRandomDecisionStrategy.Identity,
			runNumber);
		var executor = new SimulationExecutor();

		var run = executor.Execute(
			scenario,
			capability,
			identity,
			runNumber);

		run.Should().Be(new IncompleteSimulationRun(expectedMaterial));

		var calls = new List<int>();
		SimulationBatchSourceEvidence? screening = null;
		var evaluator = new TerminalLobbyEvaluator(
			(batchScenario, batchCapability, batchIdentity, count, cancellationToken) =>
			{
				calls.Add(count);
				screening = executor.ExecuteBatch(
					batchScenario,
					batchCapability,
					batchIdentity,
					count,
					cancellationToken);
				return screening;
			});

		var result = evaluator.Evaluate(
			scenario,
			capability,
			LobbyEvaluationDepth.FullProbability);

		result.Should().BeOfType<CouldNotEvaluateLobbyEvaluation>();
		calls.Should().Equal(TerminalLobbyEvaluator.ScreeningAttemptCount);
		screening.Should().NotBeNull();
		screening!.Records.Should().HaveCount(TerminalLobbyEvaluator.ScreeningAttemptCount);
		screening.Records.OfType<IncompleteSimulationRun>().Should()
			.HaveCount(TerminalLobbyEvaluator.ScreeningAttemptCount);
		screening.Records.Select(record => record.RunSeedMaterial).Should().Equal(
			Enumerable.Range(0, TerminalLobbyEvaluator.ScreeningAttemptCount)
				.Select(attempt => new RunSeedMaterial(
					identity,
					BaselineRandomDecisionStrategy.Identity,
					attempt)));
		MarkTestCompleted();
	}

	[Fact]
	public void Evaluate_StutteringJudgePolicyMissingSignalObservation_UsesOneRealScreeningBatchWithOnlyIncompleteRuns()
	{
		var scenario = Scenario(
			MainRoleType.SimpleWerewolf,
			MainRoleType.StutteringJudge,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var capability = SafetyScreeningWithoutStutteringJudgeSignalObservation();
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			capability.Identity);
		var executor = new SimulationExecutor();
		var calls = new List<int>();
		SimulationBatchSourceEvidence? screening = null;
		var evaluator = new TerminalLobbyEvaluator(
			(batchScenario, batchCapability, batchIdentity, count, cancellationToken) =>
			{
				calls.Add(count);
				screening = executor.ExecuteBatch(
					batchScenario,
					batchCapability,
					batchIdentity,
					count,
					cancellationToken);
				return screening;
			});

		var result = evaluator.Evaluate(
			scenario,
			capability,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

		result.Should().BeOfType<CouldNotEvaluateLobbyEvaluation>();
		calls.Should().Equal(TerminalLobbyEvaluator.ScreeningAttemptCount);
		screening.Should().NotBeNull();
		screening!.Records.Should().HaveCount(TerminalLobbyEvaluator.ScreeningAttemptCount);
		screening.IncompleteRunCount.Should()
			.BeGreaterThan(0);
		screening.CompletedRunCount.Should()
			.Be(0);
		screening.Records.Select(record => record.RunSeedMaterial).Should().Equal(
			Enumerable.Range(0, TerminalLobbyEvaluator.ScreeningAttemptCount)
				.Select(attempt => new RunSeedMaterial(
					identity,
					BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
					attempt)));
		MarkTestCompleted();
	}

	[Fact]
	public void Evaluate_ScapegoatPolicyMissingHolderObservation_UsesFixedIncompleteRunAndSyntheticMixedBatch()
	{
		const long runNumber = 2;
		var scenario = Scenario(
			MainRoleType.SimpleWerewolf,
			MainRoleType.Scapegoat,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var capability = SafetyScreeningWithoutScapegoatHolderObservation();
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			capability.Identity);
		var executor = new SimulationExecutor();
		var expectedMaterial = new RunSeedMaterial(
			identity,
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			runNumber);
		var run = executor.Execute(
			scenario,
			capability,
			identity,
			runNumber);
		run.Should().Be(new IncompleteSimulationRun(expectedMaterial));

		var calls = new List<int>();
		var evaluator = new TerminalLobbyEvaluator(
			(batchScenario, _, batchIdentity, count, _) =>
			{
				calls.Add(count);
				var completed = Batch(
					batchScenario,
					batchIdentity,
					count,
					_ => (1, VictoryCheckWindow.Dawn));
				var records = completed.Records.ToArray();
				records[^1] = new IncompleteSimulationRun(
					records[^1].RunSeedMaterial);
				return new SimulationBatchSourceEvidence(
					batchScenario.ToCanonical(),
					batchIdentity.Profile,
					BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
					records);
			});

		var result = evaluator.Evaluate(
			scenario,
			capability,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

		result.Should().BeOfType<CouldNotEvaluateLobbyEvaluation>();
		calls.Should().Equal(TerminalLobbyEvaluator.ScreeningAttemptCount);
		MarkTestCompleted();
	}

	[Fact]
	public void Evaluate_ExecutionFailure_ReturnsCouldNotEvaluateWithoutDiagnostics()
	{
		var evaluator = new TerminalLobbyEvaluator((_, _, _, _) => throw new InvalidOperationException("secret"));

		var result = evaluator.Evaluate(
			SupportedScenario(),
			SimulatorCapability.FullProbability,
			LobbyEvaluationDepth.FullProbability);

		result.Should().Be(new CouldNotEvaluateLobbyEvaluation());
		MarkTestCompleted();
	}

	[Fact]
	public void Evaluate_IdentityInconsistentEvidence_ReturnsCouldNotEvaluate()
	{
		var evaluator = new TerminalLobbyEvaluator((scenario, _, _, _) =>
			new SimulationBatchSourceEvidence(
				scenario.ToCanonical(), new SimulatorProfileIdentity("other", "1"),
				BaselineRandomDecisionStrategy.Identity, []));

		evaluator.Evaluate(
				SupportedScenario(),
				SimulatorCapability.FullProbability,
				LobbyEvaluationDepth.FullProbability)
			.Should().BeOfType<CouldNotEvaluateLobbyEvaluation>();
		MarkTestCompleted();
	}

	[Fact]
	public void Evaluate_PreCancellationAndBetweenGates_PropagateWithoutResult()
	{
		using var preCancelled = new CancellationTokenSource();
		preCancelled.Cancel();
		var evaluator = new TerminalLobbyEvaluator((scenario, identity, count, token) =>
		{
			token.ThrowIfCancellationRequested();
			return Batch(scenario, identity, count, _ => (1, VictoryCheckWindow.Dawn));
		});
		LobbyEvaluationResult? result = null;
		Action pre = () => result = evaluator.Evaluate(
			SupportedScenario(),
			SimulatorCapability.FullProbability,
			LobbyEvaluationDepth.FullProbability,
			preCancelled.Token);

		using var between = new CancellationTokenSource();
		var betweenEvaluator = new TerminalLobbyEvaluator((scenario, identity, count, token) =>
		{
			var batch = Batch(scenario, identity, count, run => run == count - 1
				? (2, VictoryCheckWindow.Dawn) : (1, VictoryCheckWindow.Dawn));
			between.Cancel();
			return batch;
		});
		Action betweenGates = () => result = betweenEvaluator.Evaluate(
			SupportedScenario(),
			SimulatorCapability.FullProbability,
			LobbyEvaluationDepth.FullProbability,
			between.Token);

		pre.Should().Throw<OperationCanceledException>();
		betweenGates.Should().Throw<OperationCanceledException>();
		result.Should().BeNull();
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(1_000)]
	[InlineData(10_000)]
	public void Evaluate_CallerCancellationDuringEitherBatch_PropagatesSameTokenWithoutResult(int cancellingBatch)
	{
		using var cancellation = new CancellationTokenSource();
		var evaluator = new TerminalLobbyEvaluator((scenario, identity, count, token) =>
		{
			token.Should().Be(cancellation.Token);
			if (count == cancellingBatch)
			{
				cancellation.Cancel();
				token.ThrowIfCancellationRequested();
			}
			return Batch(scenario, identity, count, run => count == 1_000 && run == count - 1
				? (2, VictoryCheckWindow.PreNight) : (1, VictoryCheckWindow.Dawn));
		});
		LobbyEvaluationResult? result = null;

		Action evaluate = () => result = evaluator.Evaluate(
			SupportedScenario(),
			SimulatorCapability.FullProbability,
			LobbyEvaluationDepth.FullProbability,
			cancellation.Token);

		evaluate.Should().Throw<OperationCanceledException>();
		result.Should().BeNull();
		MarkTestCompleted();
	}

	private static SimulationScenario SupportedScenario() => Scenario(
		MainRoleType.SimpleWerewolf, MainRoleType.Seer, MainRoleType.SimpleVillager,
		MainRoleType.SimpleVillager, MainRoleType.SimpleVillager);

	private static SimulationScenario Scenario(params MainRoleType[] roles) => new(5, roles);

	private static SimulatorCapability FullProbabilityWithoutStartGame() => new(
		SimulatorCapability.FullProbability.Identity,
		[
			new(MainRoleType.SimpleWerewolf, Faction.Werewolf),
			new(MainRoleType.Seer, Faction.Villager),
			new(MainRoleType.WildChild, Faction.Villager),
			new(MainRoleType.SimpleVillager, Faction.Villager)
		],
		headlessResponsePolicy: new HeadlessResponsePolicy(
			BaselineRandomDecisionStrategy.Identity,
			[
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
			]),
		supportsActorSetupCards: false,
		supportedRuleStates: [SimulationRuleState.Default]);

	private static SimulatorCapability SafetyScreeningWithoutStutteringJudgeSignalObservation() => new(
		SimulatorCapability.SafetyScreening.Identity,
		[
			new(MainRoleType.SimpleWerewolf, Faction.Werewolf),
			new(MainRoleType.StutteringJudge, Faction.Villager),
			new(MainRoleType.SimpleVillager, Faction.Villager)
		],
		headlessResponsePolicy: new HeadlessResponsePolicy(
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			SimulatorCapability.SafetyScreening.HeadlessResponsePolicy.AdmittedSemantics
				.Where(semantic =>
					semantic != ModeratorInstructionSemantic.ObserveStutteringJudgeSignal)),
		supportsActorSetupCards: false,
		supportedRuleStates: [SimulationRuleState.Default]);

	private static SimulatorCapability SafetyScreeningWithoutScapegoatHolderObservation() => new(
		SimulatorCapability.SafetyScreening.Identity,
		[
			new(MainRoleType.SimpleWerewolf, Faction.Werewolf),
			new(MainRoleType.Scapegoat, Faction.Villager),
			new(MainRoleType.SimpleVillager, Faction.Villager)
		],
		headlessResponsePolicy: new HeadlessResponsePolicy(
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			SimulatorCapability.SafetyScreening.HeadlessResponsePolicy.AdmittedSemantics
				.Where(semantic =>
					semantic != ModeratorInstructionSemantic.ObserveScapegoatHolderForTie)),
		supportsActorSetupCards: false,
		supportedRuleStates: [SimulationRuleState.Default]);

	private static SimulationBatchSourceEvidence Batch(
		SimulationScenario scenario,
		SimulationCompatibilityIdentity identity,
		int count,
		Func<int, (int Turn, VictoryCheckWindow Window)> ending)
	{
		var strategyIdentity =
			identity.Profile == SimulatorCapability.SafetyScreening.Identity
				? BaselineRandomDecisionStrategy.SafetyScreeningIdentity
				: BaselineRandomDecisionStrategy.Identity;
		var records = Enumerable.Range(0, count).Select(run =>
		{
			var value = ending(run);
			return (SimulationRun)new CompletedSimulationRun(
				new RunSeedMaterial(identity, strategyIdentity, run),
				new SingleFactionGameResult(run % 2 == 0 ? Faction.Villager : Faction.Werewolf),
				value.Turn,
				value.Window);
		});
		return new SimulationBatchSourceEvidence(
			scenario.ToCanonical(), identity.Profile, strategyIdentity, records);
	}
}
