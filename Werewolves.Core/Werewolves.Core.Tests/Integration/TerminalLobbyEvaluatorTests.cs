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
	public void Evaluate_DepthNotDeclaredByCapabilityRejectsBeforeExecution()
	{
		var calls = 0;
		var evaluator = new TerminalLobbyEvaluator((_, _, _, _, _) =>
		{
			calls++;
			throw new InvalidOperationException();
		});
		var capability = new SimulatorCapability(
			SimulatorCapability.FullProbability.Identity,
			[
				(MainRoleType.SimpleVillager, Faction.Villager, [])
			],
			supportedEvaluationDepths:
			[
				LobbyEvaluationDepth.DegenerateScreeningOnly
			]);

		var act = () => evaluator.Evaluate(
			SupportedScenario(),
			capability,
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

	[Theory]
	[InlineData(
		false,
		GameConfigValidationErrorType.ActorSetupCardCountMismatch)]
	[InlineData(
		true,
		GameConfigValidationErrorType.DuplicateActorSetupCardSource)]
	public void Evaluate_SafetyActorWithMissingOrInvalidExactThreeSetup_ReturnsRulesGateWithoutExecuting(
		bool invalidExactThreeSetup,
		GameConfigValidationErrorType expectedError)
	{
		var calls = 0;
		var evaluator = new TerminalLobbyEvaluator((_, _, _, _) =>
		{
			calls++;
			throw new InvalidOperationException();
		});
		MainRoleType[] roles =
		[
			MainRoleType.Actor,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager
		];
		var setup = invalidExactThreeSetup
			? new ActorSetupCards(
				[MainRoleType.Cupid, MainRoleType.Cupid, MainRoleType.Elder])
			: ActorSetupCards.None;
		var scenario = new SimulationScenario(5, roles, setup);

		var result = evaluator.Evaluate(
			scenario,
			SimulatorCapability.SafetyScreening,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

		var stopped = result.Should().BeOfType<RulesInvalidLobbyEvaluation>()
			.Subject.RulesValidity;
		stopped.Scenario.Should().BeSameAs(scenario);
		stopped.IsValid.Should().BeFalse();
		stopped.Errors.Should().Contain(error => error.Type == expectedError);
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
		var scenario = Scenario(MainRoleType.Gypsy, MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager, MainRoleType.SimpleVillager, MainRoleType.SimpleVillager);

		var result = evaluator.Evaluate(
			scenario,
			SimulatorCapability.SafetyScreening,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

		var stopped = result.Should().BeOfType<AppUnsupportedLobbyEvaluation>().Subject.AppSupport;
		stopped.Scenario.Should().BeSameAs(scenario);
		stopped.IsSupported.Should().BeFalse();
		stopped.UnsupportedRoles.Should().Contain(MainRoleType.Gypsy);
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
			ruleState: new SimulationRuleState(NewMoonEnabled: true));

		var result = evaluator.Evaluate(
			scenario,
			SimulatorCapability.SafetyScreening,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

		var stopped = result.Should().BeOfType<SimulatorUnsupportedLobbyEvaluation>().Subject.SimulatorSupport;
		stopped.Scenario.Should().BeSameAs(scenario);
		stopped.IsSupported.Should().BeFalse();
		stopped.HasUnsupportedActorSetupCards.Should().BeFalse();
		stopped.HasUnsupportedRuleState.Should().BeTrue();
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

		var degenerate = result.Should().BeOfType<DegenerateTerminalEvaluation>().Subject;
		degenerate.ScreeningEvidence.AttemptedRunCount.Should().Be(1_000);
		degenerate.SupportingAggregate.GameResultFrequencies
			.Should().BeEquivalentTo(degenerate.ScreeningEvidence.GameResultFrequencies);
		degenerate.SupportingAggregate.GameResultFrequencyByTurn
			.Should().BeEquivalentTo(degenerate.ScreeningEvidence.GameResultFrequencyByTurn);
		degenerate.SupportingAggregate.GameResultFrequencies
			.Should().OnlyContain(row => row.Denominator == 1_000);
		degenerate.SupportingAggregate.GameResultFrequencies
			.Should().Contain(row => row.GameResult is NoWinnerGameResult && row.Numerator == 0);
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
	public void Evaluate_ThiefBranchPolicy_WhenOneWholeBranchIsTurnOne_ReturnsDegenerate()
	{
		var scenario = ActorScenario(ActorReachability.DealPoolWithThief);
		var policy = scenario.ThiefOfferBranchPolicy!;
		var evaluator = new TerminalLobbyEvaluator((scenario, identity, count, _) =>
			Batch(
				scenario,
				identity,
				count,
				run => policy.GetBranch(run) == policy.Branches[0]
					? (1, VictoryCheckWindow.Dawn)
					: (2, VictoryCheckWindow.PreNight)));

		var result = evaluator.Evaluate(
			scenario,
			SimulatorCapability.SafetyScreening,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

		var degenerate = result.Should().BeOfType<DegenerateTerminalEvaluation>().Subject;
		degenerate.ScreeningEvidence.AttemptedRunCount.Should().Be(3_000);
		degenerate.ScreeningEvidence.Records.Should().HaveCount(3_000);
		MarkTestCompleted();
	}

	[Fact]
	public void Evaluate_ThiefBranchPolicy_DegenerateBranchWinsOverIncompleteOtherBranch()
	{
		var scenario = ActorScenario(ActorReachability.DealPoolWithThief);
		var policy = scenario.ThiefOfferBranchPolicy!;
		var evaluator = new TerminalLobbyEvaluator((scenario, identity, count, _) =>
		{
			var complete = Batch(
				scenario,
				identity,
				count,
				run => policy.GetBranch(run) == policy.Branches[0]
					? (1, VictoryCheckWindow.Dawn)
					: (2, VictoryCheckWindow.PreNight));
			var records = complete.Records.ToArray();
			var incompleteIndex = Enumerable.Range(0, count)
				.Last(run => policy.GetBranch(run) == policy.Branches[1]);
			records[incompleteIndex] = new IncompleteSimulationRun(
				records[incompleteIndex].RunSeedMaterial);
			return new SimulationBatchSourceEvidence(
				scenario.ToCanonical(),
				identity.Profile,
				BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
				records);
		});

		var result = evaluator.Evaluate(
			scenario,
			SimulatorCapability.SafetyScreening,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

		var degenerate = result.Should().BeOfType<DegenerateTerminalEvaluation>().Subject;
		degenerate.ScreeningEvidence.AttemptedRunCount.Should().Be(3_000);
		degenerate.ScreeningEvidence.IncompleteRunCount.Should().Be(1);
		degenerate.ScreeningEvidence.Records.Should().HaveCount(3_000);
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(false, 0, 750)]
	[InlineData(false, 1, 250)]
	[InlineData(false, 2, 0)]
	[InlineData(true, 0, 750)]
	[InlineData(true, 1, 250)]
	[InlineData(true, 2, 0)]
	public void Evaluate_SeveralProvingThiefBranches_RetainsFirstCanonicalAggregateAndAllOriginalEvidence(
		bool actorReachable,
		int firstProvingBranch,
		int expectedVillagerCount)
	{
		var scenario = actorReachable
			? ActorScenario(ActorReachability.DealPoolWithThief)
			: ThiefScenario();
		var capability = SimulatorCapability.SafetyScreening;
		var identity = capability.CreateCompatibilityIdentity(scenario);
		var policy = scenario.ThiefOfferBranchPolicy!;
		policy.Branches.Should().Equal(
			ThiefOfferBranch.Offer1, ThiefOfferBranch.Offer2, ThiefOfferBranch.Decline);
		var villager = new SingleFactionGameResult(Faction.Villager);
		var werewolf = new SingleFactionGameResult(Faction.Werewolf);
		var noWinner = new NoWinnerGameResult();
		SimulationBatchSourceEvidence? source = null;
		var evaluator = new TerminalLobbyEvaluator((_, _, count, _) =>
		{
			count.Should().Be(3_000);
			var records = Enumerable.Range(0, count).Select(run =>
			{
				var branchIndex = run % 3;
				var seed = new RunSeedMaterial(
					identity, capability.HeadlessResponsePolicy.StrategyIdentity, run);
				if (branchIndex < firstProvingBranch && run < 3)
				{
					return (SimulationRun)new IncompleteSimulationRun(seed);
				}
				var villagerCount = branchIndex switch { 0 => 750, 1 => 250, _ => 0 };
				var result = run / 3 < villagerCount ? villager : werewolf;
				return new CompletedSimulationRun(
					seed,
					result,
					branchIndex >= firstProvingBranch ? 1 : 2,
					result == villager ? VictoryCheckWindow.Dawn : VictoryCheckWindow.PreNight);
			});
			source = new SimulationBatchSourceEvidence(
				identity.Scenario, identity.Profile, capability.HeadlessResponsePolicy.StrategyIdentity, records);
			return source;
		});

		var evaluation = evaluator.Evaluate(
			scenario, capability, LobbyEvaluationDepth.DegenerateScreeningOnly)
			.Should().BeOfType<DegenerateTerminalEvaluation>().Subject;
		var evidence = evaluation.ScreeningEvidence;
		evidence.Records.Should().BeSameAs(source!.Records);
		evidence.Records.Select(record => record.RunSeedMaterial).Should().Equal(
			Enumerable.Range(0, 3_000).Select(run => new RunSeedMaterial(
				identity, capability.HeadlessResponsePolicy.StrategyIdentity, run)));
		evidence.CompletedRunCount.Should().Be(3_000 - firstProvingBranch);
		evidence.IncompleteRunCount.Should().Be(firstProvingBranch);
		if (firstProvingBranch > 0)
		{
			Action frequencies = () => _ = evidence.GameResultFrequencies;
			Action cells = () => _ = evidence.GameResultFrequencyByTurn;
			Action endedByTurn = () => evidence.GetEndedByTurnFrequency(1);
			frequencies.Should().Throw<InvalidOperationException>();
			cells.Should().Throw<InvalidOperationException>();
			endedByTurn.Should().Throw<InvalidOperationException>();
		}

		var aggregate = evaluation.SupportingAggregate;
		aggregate.GameResultFrequencies.Select(row => row.GameResult)
			.Should().Equal(evidence.PossibleGameResults);
		aggregate.GameResultFrequencies.Select(row => row.Numerator)
			.Should().Equal(expectedVillagerCount, 1_000 - expectedVillagerCount, 0);
		aggregate.GameResultFrequencies.Should().OnlyContain(row => row.Denominator == 1_000);
		var expectedCells = new List<TerminalCacheTurnWindowFrequency>();
		if (expectedVillagerCount > 0)
		{
			expectedCells.Add(new(villager, 1, VictoryCheckWindow.Dawn, expectedVillagerCount, 1_000));
		}
		expectedCells.Add(new(werewolf, 1, VictoryCheckWindow.PreNight, 1_000 - expectedVillagerCount, 1_000));
		var expected = new DegenerateTerminalCacheRecord(
			identity,
			[
				new(villager, expectedVillagerCount, 1_000),
				new(werewolf, 1_000 - expectedVillagerCount, 1_000),
				new(noWinner, 0, 1_000)
			],
			expectedCells,
			capability);
		var encoded = TerminalLobbyCache.Write(TerminalLobbyCache.Capture(scenario, capability, evaluation));
		encoded.Should().Equal(TerminalLobbyCache.Write(expected));

		Action replaceRow = () => ((IList<GameResultFrequency>)aggregate.GameResultFrequencies)[0] =
			new GameResultFrequency(noWinner, 1_000, 1_000);
		Action clearCells = () => ((IList<GameResultTurnWindowFrequency>)aggregate.GameResultFrequencyByTurn).Clear();
		Action clearRecords = () => ((IList<SimulationRun>)evidence.Records).Clear();
		Action clearInventory = () => ((IList<GameResult>)evidence.PossibleGameResults).Clear();
		replaceRow.Should().Throw<NotSupportedException>();
		clearCells.Should().Throw<NotSupportedException>();
		clearRecords.Should().Throw<NotSupportedException>();
		clearInventory.Should().Throw<NotSupportedException>();
		TerminalLobbyCache.Write(TerminalLobbyCache.Capture(scenario, capability, evaluation))
			.Should().Equal(encoded);
		TerminalLobbyCache.Read(encoded, scenario, capability).Record.Should().BeEquivalentTo(expected);
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(ScreeningEvidenceMismatch.Missing)]
	[InlineData(ScreeningEvidenceMismatch.Short)]
	[InlineData(ScreeningEvidenceMismatch.Scenario)]
	[InlineData(ScreeningEvidenceMismatch.Profile)]
	[InlineData(ScreeningEvidenceMismatch.Strategy)]
	[InlineData(ScreeningEvidenceMismatch.ResultInventory)]
	public void Evaluate_InvalidThiefScreeningEvidence_CannotEstablishDegenerateResult(
		ScreeningEvidenceMismatch mismatch)
	{
		var evaluator = new TerminalLobbyEvaluator((scenario, identity, count, _) =>
		{
			var batchScenario = mismatch == ScreeningEvidenceMismatch.Scenario
				? ThiefScenario(MainRoleType.Defender, MainRoleType.Seer) : scenario;
			var batchIdentity = new SimulationCompatibilityIdentity(
				batchScenario.ToCanonical(),
				mismatch == ScreeningEvidenceMismatch.Profile
					? SimulatorCapability.FullProbability.Identity : identity.Profile);
			var strategy = mismatch == ScreeningEvidenceMismatch.Strategy
				? BaselineRandomDecisionStrategy.Identity : BaselineRandomDecisionStrategy.SafetyScreeningIdentity;
			var batchCount = mismatch switch
			{
				ScreeningEvidenceMismatch.Missing => 0,
				ScreeningEvidenceMismatch.Short => count - 1,
				_ => count
			};
			var records = Enumerable.Range(0, batchCount).Select(run => new CompletedSimulationRun(
				new RunSeedMaterial(batchIdentity, strategy, run),
				new SingleFactionGameResult(mismatch == ScreeningEvidenceMismatch.ResultInventory
					? Faction.Piper : Faction.Villager),
				1,
				VictoryCheckWindow.Dawn));
			return new SimulationBatchSourceEvidence(
				batchIdentity.Scenario, batchIdentity.Profile, strategy, records);
		});

		evaluator.Evaluate(
			ThiefScenario(), SimulatorCapability.SafetyScreening, LobbyEvaluationDepth.DegenerateScreeningOnly)
			.Should().BeOfType<CouldNotEvaluateLobbyEvaluation>();
		MarkTestCompleted();
	}

	[Fact]
	public void Evaluate_ThiefBranchPolicy_AllBranchesCompleteAndNonDegenerate_ReturnsScreeningPassed()
	{
		var evaluator = new TerminalLobbyEvaluator((scenario, identity, count, _) =>
			Batch(
				scenario,
				identity,
				count,
				_ => (2, VictoryCheckWindow.PreNight)));

		var result = evaluator.Evaluate(
			ThiefScenario(),
			SimulatorCapability.SafetyScreening,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

		result.Should().BeOfType<ScreeningPassedLobbyEvaluation>();
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(ActorReachability.DealPool)]
	[InlineData(ActorReachability.Offer1)]
	[InlineData(ActorReachability.Offer2)]
	[InlineData(ActorReachability.DealPoolWithThief)]
	public void Evaluate_ActorReachability_UsesOneThousandSafetyAttemptsPerDistinctLegalBranch(
		ActorReachability reachability)
	{
		var scenario = ActorScenario(reachability);
		var calls = new List<int>();
		SimulationBatchSourceEvidence? screening = null;
		var evaluator = new TerminalLobbyEvaluator((batchScenario, identity, count, _) =>
		{
			calls.Add(count);
			screening = Batch(
				batchScenario,
				identity,
				count,
				_ => (2, VictoryCheckWindow.PreNight));
			return screening;
		});
		var branchPolicy = scenario.ThiefOfferBranchPolicy;
		var expectedBranchCount = branchPolicy?.Branches.Count ?? 1;
		var expectedAttemptCount = checked(
			TerminalLobbyEvaluator.ScreeningAttemptCount * expectedBranchCount);

		var result = evaluator.Evaluate(
			scenario,
			SimulatorCapability.SafetyScreening,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

		result.Should().BeOfType<ScreeningPassedLobbyEvaluation>();
		calls.Should().Equal(expectedAttemptCount);
		screening.Should().NotBeNull();
		screening!.Records.Should().HaveCount(expectedAttemptCount);
		screening.Records.Select(record => record.RunSeedMaterial.RunNumber)
			.Should().Equal(Enumerable.Range(0, expectedAttemptCount)
				.Select(attempt => (long)attempt));
		if (branchPolicy is not null)
		{
			screening.Records
				.GroupBy(record => branchPolicy.GetBranch(
					record.RunSeedMaterial.RunNumber))
				.Should().HaveCount(expectedBranchCount)
				.And.OnlyContain(group =>
					group.Count() == TerminalLobbyEvaluator.ScreeningAttemptCount);
		}
		MarkTestCompleted();
	}

	[Theory]
	[InlineData(MainRoleType.Seer, MainRoleType.Defender, 3)]
	[InlineData(MainRoleType.Seer, MainRoleType.Seer, 2)]
	[InlineData(MainRoleType.SimpleWerewolf, MainRoleType.BigBadWolf, 2)]
	[InlineData(MainRoleType.SimpleWerewolf, MainRoleType.SimpleWerewolf, 1)]
	public void Evaluate_ThiefWithoutActorReachability_UsesOneThousandSafetyAttemptsPerSemanticBranch(
		MainRoleType offer1,
		MainRoleType offer2,
		int expectedBranchCount)
	{
		var scenario = ThiefScenario(offer1, offer2);
		var calls = new List<int>();
		SimulationBatchSourceEvidence? screening = null;
		var evaluator = new TerminalLobbyEvaluator((batchScenario, identity, count, _) =>
		{
			calls.Add(count);
			screening = Batch(
				batchScenario,
				identity,
				count,
				_ => (2, VictoryCheckWindow.PreNight));
			return screening;
		});
		var expectedAttemptCount = checked(
			TerminalLobbyEvaluator.ScreeningAttemptCount * expectedBranchCount);

		var result = evaluator.Evaluate(
			scenario,
			SimulatorCapability.SafetyScreening,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

		scenario.ToCanonical().ActorSetupCards.Should().BeEmpty();
		scenario.ThiefOfferBranchPolicy.Should().NotBeNull();
		scenario.ThiefOfferBranchPolicy!.Branches.Should().HaveCount(expectedBranchCount);
		result.Should().BeOfType<ScreeningPassedLobbyEvaluation>();
		calls.Should().Equal(expectedAttemptCount);
		screening!.Records
			.GroupBy(record => scenario.ThiefOfferBranchPolicy.GetBranch(
				record.RunSeedMaterial.RunNumber))
			.Should().HaveCount(expectedBranchCount)
			.And.OnlyContain(group =>
				group.Count() == TerminalLobbyEvaluator.ScreeningAttemptCount);
		MarkTestCompleted();
	}

	[Fact]
	public void Evaluate_ThiefBranchPolicy_NoDegenerateBranchAndOneIncomplete_ReturnsCouldNotEvaluate()
	{
		var evaluator = new TerminalLobbyEvaluator((scenario, identity, count, _) =>
		{
			var complete = Batch(
				scenario,
				identity,
				count,
				_ => (2, VictoryCheckWindow.PreNight));
			var records = complete.Records.ToArray();
			records[^1] = new IncompleteSimulationRun(
				records[^1].RunSeedMaterial);
			return new SimulationBatchSourceEvidence(
				scenario.ToCanonical(),
				identity.Profile,
				BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
				records);
		});

		var result = evaluator.Evaluate(
			ThiefScenario(),
			SimulatorCapability.SafetyScreening,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

		result.Should().BeOfType<CouldNotEvaluateLobbyEvaluation>();
		MarkTestCompleted();
	}

	[Fact]
	public void Evaluate_DegenerateScreeningOnly_TwoWerewolvesAndThreeVillagersReturnsKnownDegenerateOracle()
	{
		var scenario = Scenario(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var identity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorCapability.SafetyScreening.Identity);

		var result = new TerminalLobbyEvaluator().Evaluate(
			scenario,
			SimulatorCapability.SafetyScreening,
			LobbyEvaluationDepth.DegenerateScreeningOnly);

		var evidence = result.Should().BeOfType<DegenerateTerminalEvaluation>()
			.Subject.ScreeningEvidence;
		evidence.AttemptedRunCount.Should()
			.Be(TerminalLobbyEvaluator.ScreeningAttemptCount);
		evidence.CompletedRunCount.Should()
			.Be(TerminalLobbyEvaluator.ScreeningAttemptCount);
		evidence.IncompleteRunCount.Should().Be(0);
		evidence.Records.Select(record => record.RunSeedMaterial).Should().Equal(
			Enumerable.Range(0, TerminalLobbyEvaluator.ScreeningAttemptCount)
				.Select(runNumber => new RunSeedMaterial(
					identity,
					BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
					runNumber)));
		var completed = evidence.Records.OfType<CompletedSimulationRun>().ToArray();
		completed.Should().HaveCount(TerminalLobbyEvaluator.ScreeningAttemptCount);
		completed.Should().OnlyContain(run =>
			run.EndingTurn == 1
			&& run.VictoryCheckWindow == VictoryCheckWindow.Dawn
			&& run.GameResult.Equals(new SingleFactionGameResult(Faction.Werewolf)));
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
	public void Evaluate_PolicyMissingReachableStartGameSemantic_UsesOneRealIncompleteParallelScreeningBatch()
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
					degreeOfParallelism: 4,
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
	public void Evaluate_DevotedServantPolicyMissingVoteWindow_UsesFixedIncompleteRunAndReturnsCouldNotEvaluate()
	{
		const long runNumber = 1;
		var scenario = new SimulationScenario(
			7,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.DevotedServant,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var capability = SafetyScreeningWithoutDevotedServantVoteWindow();
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

	private static SimulationScenario ThiefScenario(
		MainRoleType offer1 = MainRoleType.Seer,
		MainRoleType offer2 = MainRoleType.Defender)
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
			5,
			dealPool.Concat([offer1, offer2]),
			dealPool,
			offer1,
			offer2);
	}

	private static SimulationScenario ActorScenario(
		ActorReachability reachability)
	{
		var setup = new ActorSetupCards(
			[MainRoleType.Cupid, MainRoleType.Defender, MainRoleType.Elder]);
		if (reachability == ActorReachability.DealPool)
		{
			return new SimulationScenario(
				7,
				[
					MainRoleType.Actor,
					MainRoleType.SimpleWerewolf,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager
				],
				setup);
		}

		MainRoleType[] dealPool = reachability == ActorReachability.DealPoolWithThief
			?
			[
				MainRoleType.Thief,
				MainRoleType.Actor,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]
			:
			[
				MainRoleType.Thief,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			];
		var offers = reachability switch
		{
			ActorReachability.DealPoolWithThief =>
				(Offer1: MainRoleType.Seer, Offer2: MainRoleType.Witch),
			ActorReachability.Offer1 =>
				(Offer1: MainRoleType.Actor, Offer2: MainRoleType.Seer),
			ActorReachability.Offer2 =>
				(Offer1: MainRoleType.Seer, Offer2: MainRoleType.Actor),
			_ => throw new ArgumentOutOfRangeException(
				nameof(reachability),
				reachability,
				null)
		};
		return new SimulationScenario(
			5,
			dealPool.Concat([offers.Offer1, offers.Offer2]),
			dealPool,
			offers.Offer1,
			offers.Offer2,
			setup);
	}

	private static SimulationScenario Scenario(params MainRoleType[] roles) => new(5, roles);

	private static SimulatorCapability FullProbabilityWithoutStartGame() => new(
		SimulatorCapability.FullProbability.Identity,
		[
			(MainRoleType.SimpleWerewolf, Faction.Werewolf, []),
			(MainRoleType.Seer, Faction.Villager, []),
			(MainRoleType.WildChild, Faction.Villager, []),
			(MainRoleType.SimpleVillager, Faction.Villager, [])
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
		    ModeratorInstructionSemantic.AnnounceVillageIdiotPardon,
				ModeratorInstructionSemantic.AnnounceDayElimination
			]),
		supportsActorSetupCards: false,
		supportedRuleStates: [SimulationRuleState.Default],
		supportedEvaluationDepths:
		[
			LobbyEvaluationDepth.DegenerateScreeningOnly,
			LobbyEvaluationDepth.FullProbability
		]);

	private static SimulatorCapability SafetyScreeningWithoutStutteringJudgeSignalObservation() => new(
		SimulatorCapability.SafetyScreening.Identity,
		[
			(MainRoleType.SimpleWerewolf, Faction.Werewolf, []),
			(MainRoleType.StutteringJudge, Faction.Villager, []),
			(MainRoleType.SimpleVillager, Faction.Villager, [])
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
			(MainRoleType.SimpleWerewolf, Faction.Werewolf, []),
			(MainRoleType.Scapegoat, Faction.Villager, []),
			(MainRoleType.SimpleVillager, Faction.Villager, [])
		],
		headlessResponsePolicy: new HeadlessResponsePolicy(
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			SimulatorCapability.SafetyScreening.HeadlessResponsePolicy.AdmittedSemantics
				.Where(semantic =>
					semantic != ModeratorInstructionSemantic.ObserveScapegoatHolderForTie)),
		supportsActorSetupCards: false,
		supportedRuleStates: [SimulationRuleState.Default]);

	private static SimulatorCapability SafetyScreeningWithoutDevotedServantVoteWindow() => new(
		SimulatorCapability.SafetyScreening.Identity,
		[
			(MainRoleType.SimpleWerewolf, Faction.Werewolf, [Faction.Werewolf]),
			(MainRoleType.DevotedServant, Faction.Villager, []),
			(MainRoleType.SimpleVillager, Faction.Villager, [])
		],
		headlessResponsePolicy: new HeadlessResponsePolicy(
			BaselineRandomDecisionStrategy.SafetyScreeningIdentity,
			SimulatorCapability.SafetyScreening.HeadlessResponsePolicy.AdmittedSemantics
				.Where(semantic =>
					semantic != ModeratorInstructionSemantic.ResolveDevotedServantVoteWindow)),
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

	public enum ScreeningEvidenceMismatch
	{
		Missing,
		Short,
		Scenario,
		Profile,
		Strategy,
		ResultInventory
	}

	public enum ActorReachability
	{
		DealPool,
		Offer1,
		Offer2,
		DealPoolWithThief
	}
}
