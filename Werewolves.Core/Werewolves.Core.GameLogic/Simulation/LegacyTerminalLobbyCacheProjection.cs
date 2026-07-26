using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public abstract record LegacyTerminalLobbyCacheProjection
{
	public SimulationCompatibilityIdentity ConsumerIdentity { get; }

	private protected LegacyTerminalLobbyCacheProjection(
		SimulationCompatibilityIdentity consumerIdentity)
	{
		ConsumerIdentity = consumerIdentity
			?? throw new ArgumentNullException(nameof(consumerIdentity));
	}
}

public sealed record LegacyAlreadyDecidedTerminalLobbyCacheProjection :
	LegacyTerminalLobbyCacheProjection
{
	public GameResult GameResult { get; }

	public AlreadyDecidedReason Reason { get; }

	internal LegacyAlreadyDecidedTerminalLobbyCacheProjection(
		SimulationCompatibilityIdentity consumerIdentity,
		GameResult gameResult,
		AlreadyDecidedReason reason)
		: base(consumerIdentity)
	{
		GameResult = gameResult;
		Reason = reason;
	}
}

public sealed record LegacyDegenerateTerminalLobbyCacheProjection :
	LegacyTerminalLobbyCacheProjection
{
	internal LegacyDegenerateTerminalLobbyCacheProjection(
		SimulationCompatibilityIdentity consumerIdentity)
		: base(consumerIdentity)
	{
	}
}

public sealed record LegacyScreeningPassedTerminalLobbyCacheProjection :
	LegacyTerminalLobbyCacheProjection
{
	internal LegacyScreeningPassedTerminalLobbyCacheProjection(
		SimulationCompatibilityIdentity consumerIdentity)
		: base(consumerIdentity)
	{
	}
}

public sealed record LegacyProbabilityTerminalLobbyCacheProjection :
	LegacyTerminalLobbyCacheProjection
{
	private readonly IReadOnlyList<TerminalCacheGameResultFrequency> _frequencies;
	private readonly IReadOnlyList<TerminalCacheTurnWindowFrequency> _cells;

	public IReadOnlyList<TerminalCacheGameResultFrequency> GameResultFrequencies =>
		_frequencies;

	public IReadOnlyList<TerminalCacheTurnWindowFrequency> GameResultFrequencyByTurn =>
		_cells;

	internal LegacyProbabilityTerminalLobbyCacheProjection(
		SimulationCompatibilityIdentity consumerIdentity,
		IEnumerable<TerminalCacheGameResultFrequency> frequencies,
		IEnumerable<TerminalCacheTurnWindowFrequency> cells)
		: base(consumerIdentity)
	{
		_frequencies = Array.AsReadOnly(frequencies.ToArray());
		_cells = Array.AsReadOnly(cells.ToArray());
	}
}
