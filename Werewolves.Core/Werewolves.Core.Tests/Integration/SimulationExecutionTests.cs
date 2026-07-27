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
		var capability = new SimulatorCapability(
			new SimulatorProfileIdentity(
				$"test-{role}-missing-{missingSemantic}",
				"1"),
			[
				new(role, Faction.Villager),
				new(MainRoleType.SimpleWerewolf, Faction.Werewolf),
				new(MainRoleType.SimpleVillager, Faction.Villager)
			],
			headlessResponsePolicy: new HeadlessResponsePolicy(
				BaselineRandomDecisionStrategy.Identity,
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
					BaselineRandomDecisionStrategy.Identity,
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
	[InlineData(MainRoleType.Witch, 1)]
	[InlineData(MainRoleType.TwoSisters, 2)]
	[InlineData(MainRoleType.ThreeBrothers, 3)]
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
		batch.CompletedRunCount.Should().Be(1_000);
		batch.IncompleteRunCount.Should().Be(0);
		batch.Records.Should().OnlyContain(run =>
			run is CompletedSimulationRun);
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
			.Be(new SimulatorProfileIdentity("safety-screening", "5"));
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
			CreateVictory(Team.Werewolves, GamePhase.Day, turnNumber: 2)
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
		var validTransition = CreateTransition(GamePhase.Dawn, GamePhase.Day, turnNumber: 1);
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
				CreateTransition(GamePhase.Day, GamePhase.Day, turnNumber: 1),
				validVictory
			],
			[
				CreateTransition(GamePhase.Night, GamePhase.Day, turnNumber: 1),
				validVictory
			],
			[
				CreateTransition(GamePhase.Night, GamePhase.Night, turnNumber: 2),
				CreateVictory(Team.Villagers, GamePhase.Night, turnNumber: 2)
			],
			[
				CreateTransition(GamePhase.Dawn, GamePhase.Night, turnNumber: 2),
				CreateVictory(Team.Villagers, GamePhase.Night, turnNumber: 2)
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
