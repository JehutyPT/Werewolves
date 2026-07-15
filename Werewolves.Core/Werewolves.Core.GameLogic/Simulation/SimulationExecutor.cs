using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Log;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

internal enum SimulationExecutionCheckpoint
{
	BeforeStartStateDerivation,
	BetweenModeratorInstructions,
	BetweenBatchAttempts
}

public sealed class SimulationExecutor
{
	private readonly Func<RunSeedMaterial, DeterministicRandomSource, SimulationStartState> _startStateDeriver;
	private readonly Func<IModeratorDecisionStrategy, HeadlessGameDriver> _driverFactory;
	private readonly Func<RunSeedMaterial, IGameSession, SimulationRun> _terminalAdapter;
	private readonly Action<SimulationExecutionCheckpoint, long>? _checkpoint;

	public SimulationExecutor()
		: this(
			SimulationStartStateDeriver.Derive,
			strategy => new HeadlessGameDriver(strategy),
			AdaptTerminalEvidence,
			checkpoint: null)
	{
	}

	internal SimulationExecutor(
		Func<RunSeedMaterial, DeterministicRandomSource, SimulationStartState> startStateDeriver,
		Func<IModeratorDecisionStrategy, HeadlessGameDriver> driverFactory,
		Func<RunSeedMaterial, IGameSession, SimulationRun> terminalAdapter,
		Action<SimulationExecutionCheckpoint, long>? checkpoint = null)
	{
		_startStateDeriver = startStateDeriver
			?? throw new ArgumentNullException(nameof(startStateDeriver));
		_driverFactory = driverFactory
			?? throw new ArgumentNullException(nameof(driverFactory));
		_terminalAdapter = terminalAdapter
			?? throw new ArgumentNullException(nameof(terminalAdapter));
		_checkpoint = checkpoint;
	}

	public SimulationRun Execute(
		SimulationScenario scenario,
		SimulationCompatibilityIdentity compatibilityIdentity,
		long runNumber,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		ArgumentNullException.ThrowIfNull(compatibilityIdentity);
		ArgumentOutOfRangeException.ThrowIfNegative(runNumber);
		EnsureSupportedMatchingInput(scenario, compatibilityIdentity);
		return ExecuteValidated(compatibilityIdentity, runNumber, cancellationToken);
	}

	public SimulationBatchResult ExecuteBatch(
		SimulationScenario scenario,
		SimulationCompatibilityIdentity compatibilityIdentity,
		int runCount,
		CancellationToken cancellationToken = default) =>
		ExecuteBatch(
			scenario,
			compatibilityIdentity,
			runCount,
			degreeOfParallelism: 1,
			cancellationToken);

	internal SimulationBatchResult ExecuteBatch(
		SimulationScenario scenario,
		SimulationCompatibilityIdentity compatibilityIdentity,
		int runCount,
		int degreeOfParallelism,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		ArgumentNullException.ThrowIfNull(compatibilityIdentity);
		ArgumentOutOfRangeException.ThrowIfNegative(runCount);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(degreeOfParallelism);
		EnsureSupportedMatchingInput(scenario, compatibilityIdentity);
		cancellationToken.ThrowIfCancellationRequested();

		var records = new SimulationRun[runCount];
		var options = new ParallelOptions
		{
			CancellationToken = cancellationToken,
			MaxDegreeOfParallelism = degreeOfParallelism
		};
		Parallel.For(0, runCount, options, runNumber =>
		{
			if (runNumber > 0)
			{
				_checkpoint?.Invoke(SimulationExecutionCheckpoint.BetweenBatchAttempts, runNumber);
				cancellationToken.ThrowIfCancellationRequested();
			}

			records[runNumber] = ExecuteValidated(
				compatibilityIdentity,
				runNumber,
				cancellationToken);
		});

		return new SimulationBatchResult(
			scenario.ToCanonical(),
			compatibilityIdentity.Profile,
			BaselineRandomDecisionStrategy.Identity,
			records);
	}

	private SimulationRun ExecuteValidated(
		SimulationCompatibilityIdentity compatibilityIdentity,
		long runNumber,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var material = new RunSeedMaterial(
			compatibilityIdentity,
			BaselineRandomDecisionStrategy.Identity,
			runNumber);
		try
		{
			_checkpoint?.Invoke(SimulationExecutionCheckpoint.BeforeStartStateDerivation, runNumber);
			cancellationToken.ThrowIfCancellationRequested();
			var random = new DeterministicRandomSource(material);
			var startState = _startStateDeriver(material, random);
			var strategy = new BaselineRandomDecisionStrategy(material, startState, random);
			var driver = _driverFactory(strategy);
			var execution = driver.CompleteGameSession(
				startState.CreateGameSessionConfig(),
				cancellationToken,
				() =>
				{
					_checkpoint?.Invoke(
						SimulationExecutionCheckpoint.BetweenModeratorInstructions,
						runNumber);
				});
			return _terminalAdapter(material, execution.Session);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			return new IncompleteSimulationRun(material);
		}
	}

	private static void EnsureSupportedMatchingInput(
		SimulationScenario scenario,
		SimulationCompatibilityIdentity compatibilityIdentity)
	{
		var classification = SimulationScenarioClassifier.Classify(scenario);
		if (classification.SimulatorSupport is not { IsSupported: true } simulatorSupport)
		{
			throw new ArgumentException(
				"The Simulation Scenario is not supported by the active simulator profile.",
				nameof(scenario));
		}

		var expectedIdentity = new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			simulatorSupport.Profile.Identity);
		if (!compatibilityIdentity.Equals(expectedIdentity))
		{
			throw new ArgumentException(
				"The Simulation Compatibility Identity does not match the supplied Simulation Scenario and active profile.",
				nameof(compatibilityIdentity));
		}
	}

	internal static SimulationRun AdaptTerminalEvidence(
		RunSeedMaterial material,
		IGameSession session)
	{
		ArgumentNullException.ThrowIfNull(material);
		ArgumentNullException.ThrowIfNull(session);
		var history = session.GameHistoryLog.ToArray();
		var victoryIndexes = history
			.Select((entry, index) => (entry, index))
			.Where(item => item.entry is VictoryConditionMetLogEntry)
			.Select(item => item.index)
			.ToArray();
		if (victoryIndexes.Length != 1 || victoryIndexes[0] == 0)
		{
			return new IncompleteSimulationRun(material);
		}

		var victoryIndex = victoryIndexes[0];
		var victory = (VictoryConditionMetLogEntry)history[victoryIndex];
		if (history[victoryIndex - 1] is not PhaseTransitionLogEntry transition
			|| transition.CurrentPhase != victory.CurrentPhase
			|| transition.TurnNumber != victory.TurnNumber)
		{
			return new IncompleteSimulationRun(material);
		}

		GameResult? gameResult = victory.WinningTeam switch
		{
			Team.Villagers => new SingleFactionGameResult(Faction.Villager),
			Team.Werewolves => new SingleFactionGameResult(Faction.Werewolf),
			_ => null
		};
		if (gameResult is null)
		{
			return new IncompleteSimulationRun(material);
		}

		var (window, endingTurn) = transition.CurrentPhase switch
		{
			GamePhase.Day => (VictoryCheckWindow.Dawn, transition.TurnNumber),
			GamePhase.Night => (VictoryCheckWindow.PreNight, transition.TurnNumber - 1),
			_ => (default(VictoryCheckWindow), 0)
		};
		if (endingTurn <= 0)
		{
			return new IncompleteSimulationRun(material);
		}

		return new CompletedSimulationRun(material, gameResult, endingTurn, window);
	}
}
