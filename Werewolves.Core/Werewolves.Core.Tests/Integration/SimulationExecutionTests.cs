using FluentAssertions;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models;
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
			SimulatorProfile.Active.Identity);
		var executor = new SimulationExecutor();

		var run = executor.Execute(scenario, identity, runNumber: 0);

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

		var first = new SimulationExecutor().Execute(scenario, identity, runNumber: 0);
		var replay = new SimulationExecutor().Execute(scenario, identity, runNumber: 0);

		first.Should().BeOfType<CompletedSimulationRun>();
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
			identity,
			runCount: 8,
			degreeOfParallelism: 1);
		SimulationBatchSourceEvidence parallel = executor.ExecuteBatch(
			scenario,
			identity,
			runCount: 8,
			degreeOfParallelism: 4);

		sequential.CanonicalScenario.Should().Be(scenario.ToCanonical());
		sequential.SimulatorProfile.Should().Be(SimulatorProfile.Active.Identity);
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

		var batch = executor.ExecuteBatch(scenario, identity, runCount: 4);

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
			(material, _) =>
			{
				derivationCount++;
				return SimulationStartStateDeriver.Derive(material);
			},
			strategy => new HeadlessGameDriver(strategy),
			SimulationExecutor.AdaptTerminalEvidence);
		SimulationRun? runEvidence = null;

		Action executeRun = () => runEvidence = executor.Execute(
			scenario,
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
			(material, _) =>
			{
				derivationCount++;
				return SimulationStartStateDeriver.Derive(material);
			},
			strategy => new HeadlessGameDriver(strategy),
			SimulationExecutor.AdaptTerminalEvidence);
		SimulationBatchSourceEvidence? batchEvidence = null;

		Action executeBatch = () => batchEvidence = executor.ExecuteBatch(
			scenario,
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
			(material, _) =>
			{
				derivationCount++;
				return SimulationStartStateDeriver.Derive(material);
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
				MainRoleType.BigBadWolf,
				MainRoleType.Seer,
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
		var attempts = new Action[]
		{
			() => executor.Execute(rulesInvalid, CreateIdentity(rulesInvalid), 0),
			() => executor.Execute(appUnsupported, CreateIdentity(appUnsupported), 0),
			() => executor.Execute(simulatorUnsupported, CreateIdentity(simulatorUnsupported), 0),
			() => executor.Execute(supported, mismatchedIdentity, 0)
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
				(_, _) => throw new InvalidOperationException(),
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
			var run = executor.Execute(scenario, identity, runNumber: 23);

			run.Should().Be(new IncompleteSimulationRun(expectedMaterial));
		}

		MarkTestCompleted();
	}

	[Fact]
	public void Execute_WithDiagnosedWildChildDefect_ReturnsReplayableIncompleteEvidence()
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
		var expectedMaterial = new RunSeedMaterial(
			identity,
			BaselineRandomDecisionStrategy.Identity,
			runNumber: 11);

		var first = new SimulationExecutor().Execute(scenario, identity, runNumber: 11);
		var replay = new SimulationExecutor().Execute(scenario, identity, runNumber: 11);

		first.Should().Be(new IncompleteSimulationRun(expectedMaterial));
		replay.Should().Be(first);
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
			CreateVictory(Team.Villagers, GamePhase.Night, turnNumber: 2)
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
		var validTransition = CreateTransition(GamePhase.Night, GamePhase.Day, turnNumber: 1);
		var validVictory = CreateVictory(Team.Werewolves, GamePhase.Day, turnNumber: 1);
		GameLogEntryBase[][] histories =
		[
			[],
			[validTransition, validVictory, validVictory],
			[
				validTransition,
				CreateVictory((Team)42, GamePhase.Day, turnNumber: 1)
			],
			[validVictory],
			[
				validTransition,
				CreateVictory(Team.Werewolves, GamePhase.Night, turnNumber: 1)
			],
			[
				CreateTransition(GamePhase.Day, GamePhase.Night, turnNumber: 1),
				CreateVictory(Team.Villagers, GamePhase.Night, turnNumber: 1)
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

	private static SimulationCompatibilityIdentity CreateIdentity(SimulationScenario scenario) =>
		new(scenario.ToCanonical(), SimulatorProfile.Active.Identity);

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
		Team team,
		GamePhase currentPhase,
		int turnNumber) =>
		new()
		{
			Timestamp = DateTimeOffset.UnixEpoch,
			TurnNumber = turnNumber,
			CurrentPhase = currentPhase,
			WinningTeam = team
		};
}
