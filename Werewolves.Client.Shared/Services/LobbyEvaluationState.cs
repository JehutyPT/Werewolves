using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Client.Services;

public enum LobbyEvaluationStateKind
{
	NotApplicable,
	Pending,
	AlreadyDecided,
	Degenerate,
	ScreeningPassed,
	Probability,
	SimulatorUnavailable,
	CouldNotEvaluate
}

public sealed class LobbyProbabilityData
{
	private readonly IReadOnlyList<LobbyProbabilityOutcomeData> _outcomes;

	public IReadOnlyList<LobbyProbabilityOutcomeData> Outcomes => _outcomes;

	internal LobbyProbabilityData(IEnumerable<LobbyProbabilityOutcomeData> outcomes)
	{
		ArgumentNullException.ThrowIfNull(outcomes);
		_outcomes = Array.AsReadOnly(outcomes.ToArray());
	}
}

public sealed class LobbyProbabilityOutcomeData
{
	private readonly IReadOnlyList<LobbyProbabilityTurnData> _turns;

	public GameResult GameResult { get; }
	public int Numerator { get; }
	public int Denominator { get; }
	public IReadOnlyList<LobbyProbabilityTurnData> Turns => _turns;

	internal LobbyProbabilityOutcomeData(
		GameResult gameResult,
		int numerator,
		int denominator,
		IEnumerable<LobbyProbabilityTurnData> turns)
	{
		GameResult = gameResult ?? throw new ArgumentNullException(nameof(gameResult));
		Numerator = numerator;
		Denominator = denominator;
		ArgumentNullException.ThrowIfNull(turns);
		_turns = Array.AsReadOnly(turns.ToArray());
	}
}

public sealed record LobbyProbabilityTurnData(
	int EndingTurn,
	int Numerator,
	int Denominator);

public sealed record LobbyEvaluationState
{
	public LobbyEvaluationStateKind Kind { get; }
	public SimulationCompatibilityIdentity? Identity { get; }
	public GameResult? DecidedGameResult { get; }
	public AlreadyDecidedReason? DecidedReason { get; }
	public LobbyProbabilityData? Probability { get; }

	public bool BlocksLobbyExit => Kind is
		LobbyEvaluationStateKind.Pending or
		LobbyEvaluationStateKind.AlreadyDecided or
		LobbyEvaluationStateKind.Degenerate;

	private LobbyEvaluationState(
		LobbyEvaluationStateKind kind,
		SimulationCompatibilityIdentity? identity = null,
		GameResult? decidedGameResult = null,
		AlreadyDecidedReason? decidedReason = null,
		LobbyProbabilityData? probability = null)
	{
		Kind = kind;
		Identity = identity;
		DecidedGameResult = decidedGameResult;
		DecidedReason = decidedReason;
		Probability = probability;
	}

	internal static LobbyEvaluationState NotApplicable() =>
		new(LobbyEvaluationStateKind.NotApplicable);

	internal static LobbyEvaluationState Pending(SimulationCompatibilityIdentity identity) =>
		new(LobbyEvaluationStateKind.Pending, identity);

	internal static LobbyEvaluationState SimulatorUnavailable() =>
		new(LobbyEvaluationStateKind.SimulatorUnavailable);

	internal static LobbyEvaluationState CouldNotEvaluate(SimulationCompatibilityIdentity identity) =>
		new(LobbyEvaluationStateKind.CouldNotEvaluate, identity);

	internal static LobbyEvaluationState AlreadyDecided(
		SimulationCompatibilityIdentity identity,
		GameResult gameResult,
		AlreadyDecidedReason reason) =>
		new(
			LobbyEvaluationStateKind.AlreadyDecided,
			identity,
			gameResult,
			reason);

	internal static LobbyEvaluationState Degenerate(
		SimulationCompatibilityIdentity identity) =>
		new(LobbyEvaluationStateKind.Degenerate, identity);

	internal static LobbyEvaluationState ScreeningPassed(
		SimulationCompatibilityIdentity identity) =>
		new(LobbyEvaluationStateKind.ScreeningPassed, identity);

	internal static LobbyEvaluationState ProbabilityResult(
		SimulationCompatibilityIdentity identity,
		LobbyProbabilityData probability) =>
		new(LobbyEvaluationStateKind.Probability, identity, probability: probability);
}
