using Werewolves.Core.GameLogic.Interfaces;
using Werewolves.Core.GameLogic.Services;
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
	private readonly Func<RunSeedMaterial, SimulatorCapability, DeterministicRandomSource, SimulationStartState> _startStateDeriver;
	private readonly Func<IModeratorDecisionStrategy, HeadlessGameDriver> _driverFactory;
	private readonly Func<RunSeedMaterial, IReadOnlyList<GameLogEntryBase>, SimulationRun> _terminalAdapter;
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
		Func<RunSeedMaterial, SimulatorCapability, DeterministicRandomSource, SimulationStartState> startStateDeriver,
		Func<IModeratorDecisionStrategy, HeadlessGameDriver> driverFactory,
		Func<RunSeedMaterial, IReadOnlyList<GameLogEntryBase>, SimulationRun> terminalAdapter,
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
		SimulatorCapability capability,
		SimulationCompatibilityIdentity compatibilityIdentity,
		long runNumber,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		ArgumentNullException.ThrowIfNull(capability);
		ArgumentNullException.ThrowIfNull(compatibilityIdentity);
		ArgumentOutOfRangeException.ThrowIfNegative(runNumber);
		EnsureSupportedMatchingInput(scenario, capability, compatibilityIdentity);
		return ExecuteValidated(capability, compatibilityIdentity, runNumber, cancellationToken);
	}

	public SimulationBatchSourceEvidence ExecuteBatch(
		SimulationScenario scenario,
		SimulatorCapability capability,
		SimulationCompatibilityIdentity compatibilityIdentity,
		int runCount,
		CancellationToken cancellationToken = default) =>
		ExecuteBatch(
			scenario,
			capability,
			compatibilityIdentity,
			runCount,
			degreeOfParallelism: Environment.ProcessorCount,
			cancellationToken);

	internal SimulationBatchSourceEvidence ExecuteBatch(
		SimulationScenario scenario,
		SimulatorCapability capability,
		SimulationCompatibilityIdentity compatibilityIdentity,
		int runCount,
		int degreeOfParallelism,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		ArgumentNullException.ThrowIfNull(capability);
		ArgumentNullException.ThrowIfNull(compatibilityIdentity);
		ArgumentOutOfRangeException.ThrowIfNegative(runCount);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(degreeOfParallelism);
		EnsureSupportedMatchingInput(scenario, capability, compatibilityIdentity);
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
				capability,
				compatibilityIdentity,
				runNumber,
				cancellationToken);
		});

		return new SimulationBatchSourceEvidence(
			scenario.ToCanonical(),
			compatibilityIdentity.Profile,
			capability.HeadlessResponsePolicy.StrategyIdentity,
			records);
	}

	private SimulationRun ExecuteValidated(
		SimulatorCapability capability,
		SimulationCompatibilityIdentity compatibilityIdentity,
		long runNumber,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var material = new RunSeedMaterial(
			compatibilityIdentity,
			capability.HeadlessResponsePolicy.StrategyIdentity,
			runNumber);
		try
		{
			_checkpoint?.Invoke(SimulationExecutionCheckpoint.BeforeStartStateDerivation, runNumber);
			cancellationToken.ThrowIfCancellationRequested();
			var random = new DeterministicRandomSource(material);
			var startState = _startStateDeriver(material, capability, random);
			var strategy = new BaselineRandomDecisionStrategy(
				material,
				startState,
				capability.HeadlessResponsePolicy,
				random);
			var driver = _driverFactory(strategy);
			var execution = driver.CompleteGameSession(
				startState,
				cancellationToken,
				() =>
				{
					_checkpoint?.Invoke(
						SimulationExecutionCheckpoint.BetweenModeratorInstructions,
						runNumber);
				});
			var history = execution.Session.GameHistoryLog.ToArray();
			return _terminalAdapter(material, history);
		}
		catch (OperationCanceledException)
			when (cancellationToken.IsCancellationRequested)
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
		SimulatorCapability capability,
		SimulationCompatibilityIdentity compatibilityIdentity)
	{
		var admission = SimulationScenarioClassifier.ClassifyAdmission(
			scenario,
			capability,
			compatibilityIdentity);
		if (admission == SimulationScenarioAdmission.Unsupported)
		{
			throw new ArgumentException(
				"The Simulation Scenario is not supported by the selected Simulator Capability.",
				nameof(scenario));
		}

		if (admission == SimulationScenarioAdmission.CompatibilityIdentityMismatch)
		{
			throw new ArgumentException(
				"The Simulation Compatibility Identity does not match the supplied Simulation Scenario and selected Simulator Capability.",
				nameof(compatibilityIdentity));
		}
	}

	internal static SimulationRun AdaptTerminalEvidence(
		RunSeedMaterial material,
		IReadOnlyList<GameLogEntryBase> history)
	{
		ArgumentNullException.ThrowIfNull(material);
		ArgumentNullException.ThrowIfNull(history);
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

		if (!Enum.IsDefined(victory.VictoryCheckWindow))
		{
			return new IncompleteSimulationRun(material);
		}
		var matchesBoundary = victory.VictoryCheckWindow switch
		{
			VictoryCheckWindow.Dawn =>
				transition.PreviousPhase == GamePhase.Dawn &&
				transition.CurrentPhase == GamePhase.Day,
			VictoryCheckWindow.PreNight =>
				transition.PreviousPhase == GamePhase.Day &&
				transition.CurrentPhase == GamePhase.Night,
			_ => false
		};
		if (!matchesBoundary)
		{
			return new IncompleteSimulationRun(material);
		}

		var endingTurn = victory.VictoryCheckWindow switch
		{
			VictoryCheckWindow.Dawn => victory.TurnNumber,
			VictoryCheckWindow.PreNight => victory.TurnNumber - 1,
			_ => 0
		};
		if (endingTurn <= 0)
		{
			return new IncompleteSimulationRun(material);
		}

		return new CompletedSimulationRun(
			material,
			victory.GameResult,
			endingTurn,
			victory.VictoryCheckWindow);
	}
}
