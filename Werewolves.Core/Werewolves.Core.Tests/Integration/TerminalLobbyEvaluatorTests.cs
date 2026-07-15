using FluentAssertions;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Integration;

public class TerminalLobbyEvaluatorTests
{
	[Fact]
	public void Evaluate_AlreadyDecided_ReturnsStructuredResultWithoutExecuting()
	{
		var scenario = Scenario(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var calls = 0;
		var evaluator = new TerminalLobbyEvaluator((_, _, _, _) =>
		{
			calls++;
			throw new InvalidOperationException();
		});

		var result = evaluator.Evaluate(scenario);

		var decided = result.Should().BeOfType<AlreadyDecidedTerminalEvaluation>().Subject;
		decided.GameResult.Should().Be(new SingleFactionGameResult(Faction.Werewolf));
		decided.Reason.Should().Be(AlreadyDecidedReason.WerewolfControlShortcut);
		calls.Should().Be(0);
	}

	[Fact]
	public void Evaluate_UnsupportedInput_ReturnsNoTerminalEvaluationWithoutExecuting()
	{
		var scenario = Scenario(
			MainRoleType.Seer,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var calls = 0;
		var evaluator = new TerminalLobbyEvaluator((_, _, _, _) =>
		{
			calls++;
			throw new InvalidOperationException();
		});

		evaluator.Evaluate(scenario).Should().BeNull();
		calls.Should().Be(0);
	}

	[Fact]
	public void Evaluate_AllTurnOneScreening_ReturnsDegenerateAndStops()
	{
		var scenario = SupportedScenario();
		var calls = new List<int>();
		var evaluator = new TerminalLobbyEvaluator((input, identity, count, _) =>
		{
			calls.Add(count);
			return Batch(input, identity, count, _ => (1, VictoryCheckWindow.Dawn));
		});

		var result = evaluator.Evaluate(scenario);

		var degenerate = result.Should().BeOfType<DegenerateTerminalEvaluation>().Subject;
		degenerate.ScreeningEvidence.AttemptedRunCount.Should().Be(1_000);
		calls.Should().Equal(1_000);
	}

	[Fact]
	public void Evaluate_OneLaterScreeningRun_UsesSharedIdentityAndPublishesOnlyCompleteProbability()
	{
		var scenario = SupportedScenario();
		var calls = new List<(int Count, SimulationCompatibilityIdentity Identity)>();
		var evaluator = new TerminalLobbyEvaluator((input, identity, count, _) =>
		{
			calls.Add((count, identity));
			return count == 1_000
				? Batch(input, identity, count, run => run == 999
					? (2, VictoryCheckWindow.PreNight)
					: (1, VictoryCheckWindow.Dawn))
				: Batch(input, identity, count, run => run % 2 == 0
					? (2, VictoryCheckWindow.Dawn)
					: (3, VictoryCheckWindow.PreNight));
		});

		var result = evaluator.Evaluate(scenario);

		var probability = result.Should().BeOfType<ProbabilityTerminalEvaluation>().Subject;
		calls.Select(call => call.Count).Should().Equal(1_000, 10_000);
		calls[0].Identity.Should().Be(calls[1].Identity);
		probability.Evidence.CompletedRunCount.Should().Be(10_000);
		probability.Evidence.IncompleteRunCount.Should().Be(0);
		probability.Evidence.PossibleFactions.Should().Equal(Faction.Villager, Faction.Werewolf);
		probability.Evidence.PossibleGameResults.Should().Equal(
			new GameResult[]
			{
				new SingleFactionGameResult(Faction.Villager),
				new SingleFactionGameResult(Faction.Werewolf),
				new NoWinnerGameResult()
			});
	}

	[Fact]
	public void Evaluate_IncompleteOrFailedScreening_ReturnsNothingAndDoesNotStartProbability()
	{
		var scenario = SupportedScenario();
		var identity = Identity(scenario);
		var incompleteCalls = 0;
		var incomplete = new TerminalLobbyEvaluator((input, supplied, count, _) =>
		{
			incompleteCalls++;
			var records = Batch(input, supplied, count, _ => (1, VictoryCheckWindow.Dawn)).Records.ToArray();
			records[^1] = new IncompleteSimulationRun(records[^1].RunSeedMaterial);
			return new SimulationBatchSourceEvidence(
				input.ToCanonical(), supplied.Profile, BaselineRandomDecisionStrategy.Identity, records);
		});
		var failedCalls = 0;
		var failed = new TerminalLobbyEvaluator((_, _, _, _) =>
		{
			failedCalls++;
			throw new InvalidOperationException();
		});

		incomplete.Evaluate(scenario).Should().BeNull();
		failed.Evaluate(scenario).Should().BeNull();
		incompleteCalls.Should().Be(1);
		failedCalls.Should().Be(1);
		identity.Should().Be(Identity(scenario));
	}

	[Fact]
	public void Evaluate_CancellationAtEveryGate_PropagatesWithoutPublishing()
	{
		var scenario = SupportedScenario();
		using var preCancelled = new CancellationTokenSource();
		preCancelled.Cancel();
		var evaluator = new TerminalLobbyEvaluator((input, identity, count, token) =>
		{
			token.ThrowIfCancellationRequested();
			return Batch(input, identity, count, _ => (1, VictoryCheckWindow.Dawn));
		});
		Action pre = () => evaluator.Evaluate(scenario, preCancelled.Token);

		using var between = new CancellationTokenSource();
		var betweenEvaluator = new TerminalLobbyEvaluator((input, identity, count, _) =>
		{
			var batch = Batch(input, identity, count, run => run == 999
				? (2, VictoryCheckWindow.Dawn)
				: (1, VictoryCheckWindow.Dawn));
			between.Cancel();
			return batch;
		});
		Action betweenGates = () => betweenEvaluator.Evaluate(scenario, between.Token);

		pre.Should().Throw<OperationCanceledException>();
		betweenGates.Should().Throw<OperationCanceledException>();
	}

	[Fact]
	public void Evaluate_IncompleteProbabilityOrIdentityInconsistentEvidence_PublishesNothing()
	{
		var scenario = SupportedScenario();
		var probabilityCalls = 0;
		var incompleteProbability = new TerminalLobbyEvaluator((input, identity, count, _) =>
		{
			probabilityCalls++;
			var batch = Batch(input, identity, count, run => run == count - 1
				? (2, VictoryCheckWindow.PreNight)
				: (1, VictoryCheckWindow.Dawn));
			if (count == 10_000)
			{
				var records = batch.Records.ToArray();
				records[^1] = new IncompleteSimulationRun(records[^1].RunSeedMaterial);
				return new SimulationBatchSourceEvidence(
					input.ToCanonical(), identity.Profile, BaselineRandomDecisionStrategy.Identity, records);
			}
			return batch;
		});
		var inconsistent = new TerminalLobbyEvaluator((input, identity, count, _) =>
			new SimulationBatchSourceEvidence(
				input.ToCanonical(),
				new SimulatorProfileIdentity("other", "1"),
				BaselineRandomDecisionStrategy.Identity,
				[]));

		incompleteProbability.Evaluate(scenario).Should().BeNull();
		inconsistent.Evaluate(scenario).Should().BeNull();
		probabilityCalls.Should().Be(2);
	}

	[Theory]
	[InlineData(1_000)]
	[InlineData(10_000)]
	public void Evaluate_CancellationThrownDuringEitherBatch_Propagates(int cancellingBatch)
	{
		var evaluator = new TerminalLobbyEvaluator((input, identity, count, token) =>
		{
			if (count == cancellingBatch)
			{
				throw new OperationCanceledException(token);
			}
			return Batch(input, identity, count, run => run == count - 1
				? (2, VictoryCheckWindow.PreNight)
				: (1, VictoryCheckWindow.Dawn));
		});

		Action evaluate = () => evaluator.Evaluate(SupportedScenario());

		evaluate.Should().Throw<OperationCanceledException>();
	}

	private static SimulationScenario SupportedScenario() => Scenario(
		MainRoleType.SimpleWerewolf,
		MainRoleType.Seer,
		MainRoleType.SimpleVillager,
		MainRoleType.SimpleVillager,
		MainRoleType.SimpleVillager);

	private static SimulationScenario Scenario(params MainRoleType[] roles) => new(5, roles);

	private static SimulationCompatibilityIdentity Identity(SimulationScenario scenario) =>
		new(scenario.ToCanonical(), SimulatorProfile.Active.Identity);

	private static SimulationBatchSourceEvidence Batch(
		SimulationScenario scenario,
		SimulationCompatibilityIdentity identity,
		int count,
		Func<int, (int Turn, VictoryCheckWindow Window)> ending)
	{
		var records = Enumerable.Range(0, count).Select(run =>
		{
			var value = ending(run);
			return (SimulationRun)new CompletedSimulationRun(
				new RunSeedMaterial(identity, BaselineRandomDecisionStrategy.Identity, run),
				new SingleFactionGameResult(run % 2 == 0 ? Faction.Villager : Faction.Werewolf),
				value.Turn,
				value.Window);
		});
		return new SimulationBatchSourceEvidence(
			scenario.ToCanonical(), identity.Profile, BaselineRandomDecisionStrategy.Identity, records);
	}
}
