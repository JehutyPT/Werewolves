using Werewolves.Core.StateModels.Enums;

namespace Werewolves.Core.StateModels.Models.Simulation;

public record ExactFrequency
{
	public int Numerator { get; }
	public int Denominator { get; }

	public ExactFrequency(int numerator, int denominator)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(numerator);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
		if (numerator > denominator)
		{
			throw new ArgumentOutOfRangeException(nameof(numerator));
		}
		Numerator = numerator;
		Denominator = denominator;
	}
}

public sealed record GameResultFrequency : ExactFrequency
{
	public GameResult GameResult { get; }

	public GameResultFrequency(GameResult gameResult, int numerator, int denominator)
		: base(numerator, denominator)
	{
		GameResult = gameResult ?? throw new ArgumentNullException(nameof(gameResult));
	}
}

public sealed record GameResultTurnWindowFrequency : ExactFrequency
{
	public GameResult GameResult { get; }
	public int EndingTurn { get; }
	public VictoryCheckWindow VictoryCheckWindow { get; }

	public GameResultTurnWindowFrequency(
		GameResult gameResult,
		int endingTurn,
		VictoryCheckWindow victoryCheckWindow,
		int numerator,
		int denominator) : base(numerator, denominator)
	{
		GameResult = gameResult ?? throw new ArgumentNullException(nameof(gameResult));
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(endingTurn);
		if (!Enum.IsDefined(victoryCheckWindow))
		{
			throw new ArgumentOutOfRangeException(nameof(victoryCheckWindow));
		}
		EndingTurn = endingTurn;
		VictoryCheckWindow = victoryCheckWindow;
	}
}

public sealed class SimulationResultEvidence
{
	private readonly GameResult[] _possibleGameResults;
	private readonly CompletedSimulationRun[] _completedRuns;
	private readonly IReadOnlyList<GameResultFrequency> _gameResultFrequencies;
	private readonly IReadOnlyList<GameResultTurnWindowFrequency> _gameResultFrequencyByTurn;

	public CanonicalSimulationScenario CanonicalScenario { get; }
	public SimulatorProfileIdentity SimulatorProfile { get; }
	public DecisionStrategyIdentity DecisionStrategy { get; }
	public IReadOnlyList<SimulationRun> Records { get; }
	public int AttemptedRunCount => Records.Count;
	public int CompletedRunCount { get; }
	public int IncompleteRunCount { get; }
	public IReadOnlyList<Faction> PossibleFactions { get; }
	public IReadOnlyList<GameResult> PossibleGameResults { get; }
	public IReadOnlyList<GameResultFrequency> GameResultFrequencies
	{
		get
		{
			EnsureDistributionAvailable();
			return _gameResultFrequencies;
		}
	}
	public IReadOnlyList<GameResultTurnWindowFrequency> GameResultFrequencyByTurn
	{
		get
		{
			EnsureDistributionAvailable();
			return _gameResultFrequencyByTurn;
		}
	}

	public SimulationResultEvidence(
		SimulationBatchSourceEvidence source,
		IEnumerable<Faction> possibleFactions,
		IEnumerable<GameResult> possibleGameResults)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(possibleFactions);
		ArgumentNullException.ThrowIfNull(possibleGameResults);
		var factions = possibleFactions.ToArray();
		if (factions.Any(faction => !Enum.IsDefined(faction)))
		{
			throw new ArgumentOutOfRangeException(nameof(possibleFactions));
		}
		if (factions.Distinct().Count() != factions.Length)
		{
			throw new ArgumentException("Possible Factions must be duplicate-free.", nameof(possibleFactions));
		}

		_possibleGameResults = possibleGameResults.ToArray();
		if (_possibleGameResults.Any(result => result is null)
			|| _possibleGameResults.Any(result => result.GetType() != typeof(SingleFactionGameResult)
				&& result.GetType() != typeof(SharedVictoryGameResult)
				&& result.GetType() != typeof(NoWinnerGameResult))
			|| _possibleGameResults.Distinct().Count() != _possibleGameResults.Length)
		{
			throw new ArgumentException("Possible Game Results must be non-null and duplicate-free.", nameof(possibleGameResults));
		}
		var declaredSingles = _possibleGameResults
			.OfType<SingleFactionGameResult>()
			.Select(result => result.Faction)
			.ToArray();
		if (!declaredSingles.SequenceEqual(factions)
			|| _possibleGameResults.Count(result => result is NoWinnerGameResult) != 1
			|| _possibleGameResults.OfType<SharedVictoryGameResult>()
				.Any(result => result.Factions.Any(faction => !factions.Contains(faction))))
		{
			throw new ArgumentException(
				"The Game Result inventory must declare each Possible Faction once, No-Winner once, and only in-inventory Shared Victories.",
				nameof(possibleGameResults));
		}
		_completedRuns = source.Records.OfType<CompletedSimulationRun>().ToArray();
		if (_completedRuns.Any(run => !_possibleGameResults.Contains(run.GameResult)))
		{
			throw new ArgumentException("A completed Game Result is outside the declared inventory.", nameof(source));
		}

		CanonicalScenario = source.CanonicalScenario;
		SimulatorProfile = source.SimulatorProfile;
		DecisionStrategy = source.DecisionStrategy;
		Records = source.Records;
		CompletedRunCount = source.CompletedRunCount;
		IncompleteRunCount = source.IncompleteRunCount;
		PossibleFactions = Array.AsReadOnly(factions);
		PossibleGameResults = Array.AsReadOnly(_possibleGameResults);
		_gameResultFrequencies = CompletedRunCount == 0 || IncompleteRunCount > 0
			? Array.Empty<GameResultFrequency>()
			: Array.AsReadOnly(_possibleGameResults
				.Select(result => new GameResultFrequency(
					result,
					_completedRuns.Count(run => run.GameResult.Equals(result)),
					CompletedRunCount))
				.ToArray());
		_gameResultFrequencyByTurn = CompletedRunCount == 0 || IncompleteRunCount > 0
			? Array.Empty<GameResultTurnWindowFrequency>()
			: Array.AsReadOnly(_completedRuns
			.GroupBy(run => new { run.GameResult, run.EndingTurn, run.VictoryCheckWindow })
			.Select(group => new GameResultTurnWindowFrequency(
				group.Key.GameResult,
				group.Key.EndingTurn,
				group.Key.VictoryCheckWindow,
				group.Count(),
				CompletedRunCount))
			.OrderBy(cell => cell.EndingTurn)
			.ThenBy(cell => cell.VictoryCheckWindow)
			.ToArray());
	}

	public ExactFrequency GetEndedByTurnFrequency(int endingTurn, GameResult? gameResult = null)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(endingTurn);
		EnsureDistributionAvailable();
		if (CompletedRunCount == 0)
		{
			throw new InvalidOperationException(
				"Ended-By-Turn Frequency requires at least one Completed Simulation Run.");
		}
		if (gameResult is not null && !_possibleGameResults.Contains(gameResult))
		{
			throw new ArgumentException("The filter is outside the declared inventory.", nameof(gameResult));
		}
		return new ExactFrequency(
			_completedRuns.Count(run => run.EndingTurn <= endingTurn
				&& (gameResult is null || run.GameResult.Equals(gameResult))),
			CompletedRunCount);
	}

	private void EnsureDistributionAvailable()
	{
		if (IncompleteRunCount > 0)
		{
			throw new InvalidOperationException(
				"Incomplete Simulation Runs cannot be interpreted as a partial distribution.");
		}
	}
}
