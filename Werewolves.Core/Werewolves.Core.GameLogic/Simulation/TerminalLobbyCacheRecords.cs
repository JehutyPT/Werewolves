using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public abstract record TerminalLobbyCacheRecord
{
	public SimulationCompatibilityIdentity CompatibilityIdentity { get; }

	private protected TerminalLobbyCacheRecord(
		SimulationCompatibilityIdentity compatibilityIdentity)
	{
		CompatibilityIdentity = compatibilityIdentity
			?? throw new ArgumentNullException(nameof(compatibilityIdentity));
	}
}

public sealed record AlreadyDecidedTerminalCacheRecord : TerminalLobbyCacheRecord
{
	public GameResult GameResult { get; }

	public AlreadyDecidedReason Reason { get; }

	public AlreadyDecidedTerminalCacheRecord(
		SimulationCompatibilityIdentity identity,
		GameResult gameResult,
		AlreadyDecidedReason reason,
		SimulatorCapability capability)
		: base(identity)
	{
		ArgumentNullException.ThrowIfNull(capability);
		GameResult = TerminalLobbyCache.ValidateGameResult(gameResult);
		if (!Enum.IsDefined(reason)
			|| reason == AlreadyDecidedReason.NoLobbyExitVictoryPredicateSatisfied)
		{
			throw new ArgumentOutOfRangeException(nameof(reason));
		}

		Reason = reason;
		TerminalLobbyCache.ValidateAlreadyDecided(this, capability);
	}
}

public sealed record TerminalCacheGameResultFrequency
{
	public GameResult GameResult { get; }

	public int Numerator { get; }

	public int Denominator { get; }

	public TerminalCacheGameResultFrequency(
		GameResult gameResult,
		int numerator,
		int denominator)
	{
		GameResult = TerminalLobbyCache.ValidateGameResult(gameResult);
		TerminalLobbyCache.ValidateFrequency(numerator, denominator);
		Numerator = numerator;
		Denominator = denominator;
	}
}

public sealed record TerminalCacheTurnWindowFrequency
{
	public GameResult GameResult { get; }

	public int EndingTurn { get; }

	public VictoryCheckWindow VictoryCheckWindow { get; }

	public int Numerator { get; }

	public int Denominator { get; }

	public TerminalCacheTurnWindowFrequency(
		GameResult gameResult,
		int endingTurn,
		VictoryCheckWindow window,
		int numerator,
		int denominator)
	{
		GameResult = TerminalLobbyCache.ValidateGameResult(gameResult);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(endingTurn);
		if (!Enum.IsDefined(window))
		{
			throw new ArgumentOutOfRangeException(nameof(window));
		}

		TerminalLobbyCache.ValidateFrequency(numerator, denominator);
		if (numerator == 0)
		{
			throw new ArgumentOutOfRangeException(nameof(numerator));
		}

		EndingTurn = endingTurn;
		VictoryCheckWindow = window;
		Numerator = numerator;
		Denominator = denominator;
	}
}

public abstract record AggregateTerminalCacheRecord : TerminalLobbyCacheRecord
{
	private readonly IReadOnlyList<TerminalCacheGameResultFrequency> _frequencies;
	private readonly IReadOnlyList<TerminalCacheTurnWindowFrequency> _cells;

	public int AttemptedRunCount { get; }

	public int CompletedRunCount { get; }

	public int IncompleteRunCount { get; }

	public IReadOnlyList<TerminalCacheGameResultFrequency> GameResultFrequencies =>
		_frequencies;

	public IReadOnlyList<TerminalCacheTurnWindowFrequency> GameResultFrequencyByTurn =>
		_cells;

	private protected AggregateTerminalCacheRecord(
		SimulationCompatibilityIdentity identity,
		int policyCount,
		IEnumerable<TerminalCacheGameResultFrequency> frequencies,
		IEnumerable<TerminalCacheTurnWindowFrequency> cells,
		bool turnOneOnly,
		SimulatorCapability capability)
		: base(identity)
	{
		ArgumentNullException.ThrowIfNull(capability);
		ArgumentNullException.ThrowIfNull(frequencies);
		ArgumentNullException.ThrowIfNull(cells);
		var rows = frequencies
			.OrderBy(row => TerminalLobbyCache.ResultKey(row.GameResult), StringComparer.Ordinal)
			.ToArray();
		var timing = cells
			.OrderBy(cell => cell.EndingTurn)
			.ThenBy(cell => cell.VictoryCheckWindow)
			.ThenBy(
				cell => TerminalLobbyCache.ResultKey(cell.GameResult),
				StringComparer.Ordinal)
			.ToArray();
		TerminalLobbyCache.ValidateAggregate(
			identity,
			capability,
			policyCount,
			rows,
			timing,
			turnOneOnly);

		AttemptedRunCount = policyCount;
		CompletedRunCount = policyCount;
		IncompleteRunCount = 0;
		_frequencies = Array.AsReadOnly(rows);
		_cells = Array.AsReadOnly(timing);
	}
}

public sealed record DegenerateTerminalCacheRecord : AggregateTerminalCacheRecord
{
	public int InclusiveEndingTurnCutoff => 1;

	public DegenerateTerminalCacheRecord(
		SimulationCompatibilityIdentity identity,
		IEnumerable<TerminalCacheGameResultFrequency> frequencies,
		IEnumerable<TerminalCacheTurnWindowFrequency> cells,
		SimulatorCapability capability)
			: base(
				identity,
				TerminalLobbyEvaluator.ScreeningAttemptCount,
				frequencies,
				cells,
				turnOneOnly: true,
				capability: capability)
	{
	}
}

public sealed record ProbabilityTerminalCacheRecord : AggregateTerminalCacheRecord
{
	public ProbabilityTerminalCacheRecord(
		SimulationCompatibilityIdentity identity,
		IEnumerable<TerminalCacheGameResultFrequency> frequencies,
		IEnumerable<TerminalCacheTurnWindowFrequency> cells,
		SimulatorCapability capability)
		: base(
			identity,
			TerminalLobbyEvaluator.ProbabilityAttemptCount,
			frequencies,
			cells,
			turnOneOnly: false,
			capability: capability)
	{
	}
}

public sealed class TerminalLobbyCacheDocument
{
	private readonly IReadOnlyList<TerminalLobbyCacheRecord> _records;

	public IReadOnlyList<TerminalLobbyCacheRecord> Records => _records;

	internal TerminalLobbyCacheDocument(TerminalLobbyCacheRecord[] records)
	{
		_records = Array.AsReadOnly(records);
	}
}

public sealed record TerminalLobbyCacheReadResult(
	TerminalLobbyCacheRecord? Record,
	string? Rejection)
{
	public bool IsUsable => Record is not null;
}

public sealed record TerminalLobbyCacheDocumentReadResult(
	TerminalLobbyCacheDocument? Document,
	string? Rejection)
{
	public bool IsUsable => Document is not null;
}
