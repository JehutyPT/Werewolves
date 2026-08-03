using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public abstract record LobbyEvaluationResult;

public enum LobbyEvaluationDepth
{
	DegenerateScreeningOnly,
	FullProbability
}

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

public sealed record ScreeningPassedLobbyEvaluation : LobbyEvaluationResult;

public sealed record ProbabilityTerminalEvaluation(
	SimulationResultEvidence Evidence) : TerminalLobbyEvaluation;

public sealed class TerminalLobbyEvaluator
{
	public const int ScreeningAttemptCount = 1_000;
	public const int ProbabilityAttemptCount = 10_000;

	private readonly Func<
		SimulationScenario,
		SimulatorCapability,
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
			SimulatorCapability,
			SimulationCompatibilityIdentity,
			int,
			CancellationToken,
			SimulationBatchSourceEvidence> executeBatch)
	{
		_executeBatch = executeBatch ?? throw new ArgumentNullException(nameof(executeBatch));
	}

	internal TerminalLobbyEvaluator(
		Func<
			SimulationScenario,
			SimulationCompatibilityIdentity,
			int,
			CancellationToken,
			SimulationBatchSourceEvidence> executeBatch)
	{
		ArgumentNullException.ThrowIfNull(executeBatch);
		_executeBatch = (scenario, _, identity, count, cancellationToken) =>
			executeBatch(scenario, identity, count, cancellationToken);
	}

	public LobbyEvaluationResult Evaluate(
		SimulationScenario scenario,
		SimulatorCapability capability,
		LobbyEvaluationDepth depth,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(capability);
		if (depth == LobbyEvaluationDepth.FullProbability
			&& !capability.Identity.Equals(SimulatorCapability.FullProbability.Identity))
		{
			throw new ArgumentException(
				"Full-Probability evaluation requires the Full-Probability Simulator Capability.",
				nameof(depth));
		}

		return EvaluateCore(scenario, capability, depth, cancellationToken);
	}

	private LobbyEvaluationResult EvaluateCore(
		SimulationScenario scenario,
		SimulatorCapability capability,
		LobbyEvaluationDepth depth,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		if (!Enum.IsDefined(depth))
		{
			throw new ArgumentOutOfRangeException(nameof(depth));
		}
		cancellationToken.ThrowIfCancellationRequested();
		var classification = SimulationScenarioClassifier.Classify(scenario, capability);
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
		var screeningAttemptCount = GetScreeningAttemptCount(identity.Scenario);

		if (!TryExecuteBatch(
			scenario, capability, identity, screeningAttemptCount, cancellationToken, out var screening))
		{
			return new CouldNotEvaluateLobbyEvaluation();
		}

		cancellationToken.ThrowIfCancellationRequested();
		if (!IsConsistentBatch(
			screening,
			identity,
			simulatorSupport.Profile.HeadlessResponsePolicy.StrategyIdentity,
			screeningAttemptCount))
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
		var thiefBranchPolicy = identity.Scenario.ThiefOfferBranchPolicy;
		if (thiefBranchPolicy != null &&
		    HasDegenerateThiefBranch(screening, thiefBranchPolicy))
		{
			return new DegenerateTerminalEvaluation(screeningEvidence);
		}
		if (screening.IncompleteRunCount > 0)
		{
			return new CouldNotEvaluateLobbyEvaluation();
		}
		if (thiefBranchPolicy == null &&
		    screening.Records.Cast<CompletedSimulationRun>()
			    .All(run => run.EndingTurn == 1))
		{
			return new DegenerateTerminalEvaluation(screeningEvidence);
		}
		if (depth == LobbyEvaluationDepth.DegenerateScreeningOnly)
		{
			return new ScreeningPassedLobbyEvaluation();
		}

		cancellationToken.ThrowIfCancellationRequested();
		if (!TryExecuteBatch(
			scenario, capability, identity, ProbabilityAttemptCount, cancellationToken, out var probability))
		{
			return new CouldNotEvaluateLobbyEvaluation();
		}

		cancellationToken.ThrowIfCancellationRequested();
		if (!IsConsistentCompleteBatch(
			probability,
			identity,
			simulatorSupport.Profile.HeadlessResponsePolicy.StrategyIdentity,
			ProbabilityAttemptCount))
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

	internal static int GetScreeningAttemptCount(
		CanonicalSimulationScenario scenario)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		var branchCount = scenario.ActorSetupCards.Count > 0
			? scenario.ThiefOfferBranchPolicy?.Branches.Count ?? 1
			: 1;
		return checked(
			ScreeningAttemptCount *
			branchCount);
	}

	private bool TryExecuteBatch(
		SimulationScenario scenario,
		SimulatorCapability capability,
		SimulationCompatibilityIdentity identity,
		int attemptCount,
		CancellationToken cancellationToken,
		out SimulationBatchSourceEvidence evidence)
	{
		try
		{
			evidence = _executeBatch(
				scenario,
				capability,
				identity,
				attemptCount,
				cancellationToken);
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
		DecisionStrategyIdentity decisionStrategyIdentity,
		int expectedCount) =>
		IsConsistentBatch(
			evidence,
			identity,
			decisionStrategyIdentity,
			expectedCount)
		&& evidence.CompletedRunCount == expectedCount
		&& evidence.IncompleteRunCount == 0;

	private static bool IsConsistentBatch(
		SimulationBatchSourceEvidence evidence,
		SimulationCompatibilityIdentity identity,
		DecisionStrategyIdentity decisionStrategyIdentity,
		int expectedCount) =>
		evidence is not null
		&& evidence.CanonicalScenario.Equals(identity.Scenario)
		&& evidence.SimulatorProfile.Equals(identity.Profile)
		&& evidence.DecisionStrategy.Equals(decisionStrategyIdentity)
		&& evidence.Records.Count == expectedCount;

	private static bool HasDegenerateThiefBranch(
		SimulationBatchSourceEvidence evidence,
		ThiefOfferBranchPolicy policy) =>
		policy.Branches.Any(branch =>
		{
			var records = evidence.Records
				.Where(record =>
					policy.GetBranch(record.RunSeedMaterial.RunNumber) == branch)
				.ToArray();
			return records.Length > 0 && records.All(record =>
				record is CompletedSimulationRun { EndingTurn: 1 });
		});

}
