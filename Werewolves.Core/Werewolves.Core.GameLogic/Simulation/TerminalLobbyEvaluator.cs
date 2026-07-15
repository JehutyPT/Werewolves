using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public abstract record LobbyEvaluationResult;

public sealed record RulesInvalidLobbyEvaluation(
	RulesValidityResult RulesValidity) : LobbyEvaluationResult;

public sealed record AppUnsupportedLobbyEvaluation(
	AppSupportResult AppSupport) : LobbyEvaluationResult;

public sealed record SimulatorUnsupportedLobbyEvaluation(
	SimulatorSupportResult SimulatorSupport) : LobbyEvaluationResult;

public sealed record CouldNotEvaluateLobbyEvaluation : LobbyEvaluationResult;

public abstract record TerminalLobbyEvaluation : LobbyEvaluationResult;

public sealed record AlreadyDecidedTerminalEvaluation(
	GameResult GameResult,
	AlreadyDecidedReason Reason) : TerminalLobbyEvaluation;

public sealed record DegenerateTerminalEvaluation(
	SimulationResultEvidence ScreeningEvidence) : TerminalLobbyEvaluation;

public sealed record ProbabilityTerminalEvaluation(
	SimulationResultEvidence Evidence) : TerminalLobbyEvaluation;

public sealed class TerminalLobbyEvaluator
{
	public const int ScreeningAttemptCount = 1_000;
	public const int ProbabilityAttemptCount = 10_000;

	private readonly Func<
		SimulationScenario,
		SimulationCompatibilityIdentity,
		int,
		CancellationToken,
		SimulationBatchSourceEvidence> _executeBatch;

	public TerminalLobbyEvaluator()
	{
		var executor = new SimulationExecutor();
		_executeBatch = executor.ExecuteBatch;
	}

	internal TerminalLobbyEvaluator(
		Func<
			SimulationScenario,
			SimulationCompatibilityIdentity,
			int,
			CancellationToken,
			SimulationBatchSourceEvidence> executeBatch)
	{
		_executeBatch = executeBatch ?? throw new ArgumentNullException(nameof(executeBatch));
	}

	public LobbyEvaluationResult Evaluate(
		SimulationScenario scenario,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		cancellationToken.ThrowIfCancellationRequested();
		var classification = SimulationScenarioClassifier.Classify(scenario);
		cancellationToken.ThrowIfCancellationRequested();
		if (!classification.RulesValidity.IsValid)
		{
			return new RulesInvalidLobbyEvaluation(classification.RulesValidity);
		}
		if (classification.AppSupport is not { IsSupported: true })
		{
			return classification.AppSupport is null
				? new CouldNotEvaluateLobbyEvaluation()
				: new AppUnsupportedLobbyEvaluation(classification.AppSupport);
		}
		if (classification.SimulatorSupport is not { IsSupported: true } simulatorSupport)
		{
			return classification.SimulatorSupport is null
				? new CouldNotEvaluateLobbyEvaluation()
				: new SimulatorUnsupportedLobbyEvaluation(classification.SimulatorSupport);
		}
		if (classification.AlreadyDecided is null)
		{
			return new CouldNotEvaluateLobbyEvaluation();
		}

		if (classification.AlreadyDecided is { IsAlreadyDecided: true, GameResult: not null } decided)
		{
			return new AlreadyDecidedTerminalEvaluation(decided.GameResult, decided.Reason);
		}

		var identity = classification.Cacheability?.CompatibilityIdentity;
		if (identity is null)
		{
			return new CouldNotEvaluateLobbyEvaluation();
		}
		if (!PossibleGameResultInventory.TryCreate(
			scenario,
			simulatorSupport.Profile,
			out var inventory))
		{
			return new CouldNotEvaluateLobbyEvaluation();
		}

		if (!TryExecuteBatch(
			scenario, identity, ScreeningAttemptCount, cancellationToken, out var screening))
		{
			return new CouldNotEvaluateLobbyEvaluation();
		}

		cancellationToken.ThrowIfCancellationRequested();
		if (!IsConsistentCompleteBatch(screening, identity, ScreeningAttemptCount))
		{
			return new CouldNotEvaluateLobbyEvaluation();
		}
		SimulationResultEvidence screeningEvidence;
		try
		{
			screeningEvidence = new SimulationResultEvidence(
				screening,
				inventory.Factions,
				inventory.GameResults);
		}
		catch (ArgumentException)
		{
			return new CouldNotEvaluateLobbyEvaluation();
		}
		if (screening.Records.Cast<CompletedSimulationRun>()
			.All(run => run.EndingTurn == 1))
		{
			return new DegenerateTerminalEvaluation(screeningEvidence);
		}

		cancellationToken.ThrowIfCancellationRequested();
		if (!TryExecuteBatch(
			scenario, identity, ProbabilityAttemptCount, cancellationToken, out var probability))
		{
			return new CouldNotEvaluateLobbyEvaluation();
		}

		cancellationToken.ThrowIfCancellationRequested();
		if (!IsConsistentCompleteBatch(probability, identity, ProbabilityAttemptCount))
		{
			return new CouldNotEvaluateLobbyEvaluation();
		}
		try
		{
			return new ProbabilityTerminalEvaluation(new SimulationResultEvidence(
				probability,
				inventory.Factions,
				inventory.GameResults));
		}
		catch (ArgumentException)
		{
			return new CouldNotEvaluateLobbyEvaluation();
		}
	}

	private bool TryExecuteBatch(
		SimulationScenario scenario,
		SimulationCompatibilityIdentity identity,
		int attemptCount,
		CancellationToken cancellationToken,
		out SimulationBatchSourceEvidence evidence)
	{
		try
		{
			evidence = _executeBatch(scenario, identity, attemptCount, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			return true;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			evidence = null!;
			return false;
		}
	}

	private static bool IsConsistentCompleteBatch(
		SimulationBatchSourceEvidence evidence,
		SimulationCompatibilityIdentity identity,
		int expectedCount) =>
		evidence is not null
		&& evidence.CanonicalScenario.Equals(identity.Scenario)
		&& evidence.SimulatorProfile.Equals(identity.Profile)
		&& evidence.DecisionStrategy.Equals(BaselineRandomDecisionStrategy.Identity)
		&& evidence.Records.Count == expectedCount
		&& evidence.CompletedRunCount == expectedCount
		&& evidence.IncompleteRunCount == 0;

}
