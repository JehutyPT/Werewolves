using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public abstract record TerminalLobbyEvaluation;

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

	public TerminalLobbyEvaluation? Evaluate(
		SimulationScenario scenario,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		cancellationToken.ThrowIfCancellationRequested();
		var classification = SimulationScenarioClassifier.Classify(scenario);
		cancellationToken.ThrowIfCancellationRequested();
		if (classification.SimulatorSupport is not { IsSupported: true } simulatorSupport
			|| classification.AlreadyDecided is null)
		{
			return null;
		}

		if (classification.AlreadyDecided is { IsAlreadyDecided: true, GameResult: not null } decided)
		{
			return new AlreadyDecidedTerminalEvaluation(decided.GameResult, decided.Reason);
		}

		var identity = classification.Cacheability?.CompatibilityIdentity;
		if (identity is null)
		{
			return null;
		}
		var inventory = CreateInventory(scenario, simulatorSupport.Profile);
		if (inventory is null)
		{
			return null;
		}

		SimulationBatchSourceEvidence screening;
		try
		{
			screening = _executeBatch(
				scenario,
				identity,
				ScreeningAttemptCount,
				cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			return null;
		}

		cancellationToken.ThrowIfCancellationRequested();
		if (!IsConsistentCompleteBatch(screening, identity, ScreeningAttemptCount))
		{
			return null;
		}
		SimulationResultEvidence screeningEvidence;
		try
		{
			screeningEvidence = new SimulationResultEvidence(
				screening,
				inventory.Value.Factions,
				inventory.Value.GameResults);
		}
		catch (ArgumentException)
		{
			return null;
		}
		if (screening.Records.Cast<CompletedSimulationRun>()
			.All(run => run.EndingTurn == 1))
		{
			return new DegenerateTerminalEvaluation(screeningEvidence);
		}

		cancellationToken.ThrowIfCancellationRequested();
		SimulationBatchSourceEvidence probability;
		try
		{
			probability = _executeBatch(
				scenario,
				identity,
				ProbabilityAttemptCount,
				cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			return null;
		}

		cancellationToken.ThrowIfCancellationRequested();
		if (!IsConsistentCompleteBatch(probability, identity, ProbabilityAttemptCount))
		{
			return null;
		}
		try
		{
			return new ProbabilityTerminalEvaluation(new SimulationResultEvidence(
				probability,
				inventory.Value.Factions,
				inventory.Value.GameResults));
		}
		catch (ArgumentException)
		{
			return null;
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

	private static (Faction[] Factions, GameResult[] GameResults)? CreateInventory(
		SimulationScenario scenario,
		SimulatorProfile profile)
	{
		var factions = new HashSet<Faction>();
		foreach (var role in scenario.ToCanonical().RoleComposition.Entries.Select(entry => entry.Role))
		{
			if (!profile.TryGetBeneficiaryFaction(role, out var faction)
				|| !Enum.IsDefined(faction))
			{
				return null;
			}
			factions.Add(faction);
		}
		var orderedFactions = factions.Order().ToArray();
		var results = orderedFactions
			.Select(faction => (GameResult)new SingleFactionGameResult(faction))
			.Append(new NoWinnerGameResult())
			.ToArray();
		return (orderedFactions, results);
	}
}
