using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public static partial class TerminalLobbyCache
{
	public const string SchemaIdentifier = "terminal-lobby-cache";

	public const int SchemaVersion = 1;

	public static TerminalLobbyCacheRecord Capture(
		SimulationCompatibilityIdentity expectedIdentity,
		TerminalLobbyEvaluation evaluation)
	{
		return Capture(expectedIdentity, evaluation, capability: null);
	}

	public static TerminalLobbyCacheRecord Capture(
		SimulationScenario scenario,
		SimulatorCapability capability,
		TerminalLobbyEvaluation evaluation)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		ArgumentNullException.ThrowIfNull(capability);
		return Capture(
			capability.CreateCompatibilityIdentity(scenario),
			evaluation,
			capability);
	}

	private static TerminalLobbyCacheRecord Capture(
		SimulationCompatibilityIdentity expectedIdentity,
		TerminalLobbyEvaluation evaluation,
		SimulatorCapability? capability)
	{
		ArgumentNullException.ThrowIfNull(expectedIdentity);
		ArgumentNullException.ThrowIfNull(evaluation);
		return (evaluation, capability) switch
		{
			(AlreadyDecidedTerminalEvaluation value, null) =>
				new AlreadyDecidedTerminalCacheRecord(
					expectedIdentity,
					value.GameResult,
					value.Reason),
			(AlreadyDecidedTerminalEvaluation value, not null) =>
				new AlreadyDecidedTerminalCacheRecord(
					expectedIdentity,
					value.GameResult,
					value.Reason,
					capability),
			(DegenerateTerminalEvaluation value, _) =>
				CaptureAggregate(
					expectedIdentity,
					value.ScreeningEvidence,
					degenerate: true,
					capability),
			(ProbabilityTerminalEvaluation value, _) =>
				CaptureAggregate(
					expectedIdentity,
					value.Evidence,
					degenerate: false,
					capability),
			_ => throw new ArgumentException(
				"Only complete terminal lobby evaluations are cacheable.",
				nameof(evaluation))
		};
	}

	public static TerminalLobbyCacheDocument CreateDocument(
		IEnumerable<TerminalLobbyCacheRecord> records)
	{
		ArgumentNullException.ThrowIfNull(records);
		var values = records.ToArray();
		if (values.Any(record => record is null))
		{
			throw new ArgumentException(
				"Records cannot contain null.",
				nameof(records));
		}

		if (values
			.Select(record => record.CompatibilityIdentity)
			.Distinct()
			.Count() != values.Length)
		{
			throw new ArgumentException(
				"Only one terminal record is permitted per compatibility identity.",
				nameof(records));
		}

		return new TerminalLobbyCacheDocument(values
			.OrderBy(
				record => record.CompatibilityIdentity.ToString(),
				StringComparer.Ordinal)
			.ToArray());
	}

	public static bool TryGet(
		TerminalLobbyCacheDocument document,
		SimulationCompatibilityIdentity expectedIdentity,
		out TerminalLobbyCacheRecord record)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(expectedIdentity);
		try
		{
			_ = ClassifyProducer(expectedIdentity, probabilityRecord: false);
		}
		catch (ArgumentException)
		{
			record = null!;
			return false;
		}

		record = document.Records.SingleOrDefault(candidate =>
			candidate.CompatibilityIdentity.Equals(expectedIdentity))!;
		return record is not null;
	}

	public static bool TryGet(
		TerminalLobbyCacheDocument document,
		SimulationScenario scenario,
		SimulatorCapability capability,
		out TerminalLobbyCacheRecord record)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(scenario);
		ArgumentNullException.ThrowIfNull(capability);
		var expectedIdentity = capability.CreateCompatibilityIdentity(scenario);
		try
		{
			_ = ClassifyProducer(
				expectedIdentity,
				capability,
				LobbyEvaluationDepth.DegenerateScreeningOnly);
		}
		catch (ArgumentException)
		{
			record = null!;
			return false;
		}

		record = document.Records.SingleOrDefault(candidate =>
			candidate.CompatibilityIdentity.Equals(expectedIdentity))!;
		return record is not null;
	}

	private static TerminalLobbyCacheRecord CaptureAggregate(
		SimulationCompatibilityIdentity identity,
		SimulationResultEvidence evidence,
		bool degenerate,
		SimulatorCapability? capability)
	{
		ArgumentNullException.ThrowIfNull(evidence);
		var producer = capability
			?? ResolveProducerProfile(identity.Profile, probabilityRecord: !degenerate);
		var depth = degenerate
			? LobbyEvaluationDepth.DegenerateScreeningOnly
			: LobbyEvaluationDepth.FullProbability;
		if (!producer.SupportsEvaluationDepth(depth))
		{
			throw new ArgumentException(
				"The terminal evaluation depth is not supported by its Simulator Capability.",
				nameof(evidence));
		}
		if (!evidence.CanonicalScenario.Equals(identity.Scenario)
			|| !evidence.SimulatorProfile.Equals(identity.Profile)
			|| !evidence.DecisionStrategy.Equals(
				producer.HeadlessResponsePolicy.StrategyIdentity))
		{
			throw new ArgumentException(
				"Terminal evidence is incomplete or compatibility-mismatched.",
				nameof(evidence));
		}

		if (degenerate
			&& identity.Scenario.ThiefOfferBranchPolicy is { } branchPolicy)
		{
			return CaptureDegenerateThiefBranchWitness(
				identity,
				evidence,
				branchPolicy,
				producer);
		}

		if (evidence.IncompleteRunCount != 0)
		{
			throw new ArgumentException(
				"Terminal evidence is incomplete or compatibility-mismatched.",
				nameof(evidence));
		}

		var rows = evidence.GameResultFrequencies.Select(frequency =>
			new TerminalCacheGameResultFrequency(
				frequency.GameResult,
				frequency.Numerator,
				frequency.Denominator));
		var cells = evidence.GameResultFrequencyByTurn.Select(frequency =>
			new TerminalCacheTurnWindowFrequency(
				frequency.GameResult,
				frequency.EndingTurn,
				frequency.VictoryCheckWindow,
				frequency.Numerator,
				frequency.Denominator));
		return degenerate
			? new DegenerateTerminalCacheRecord(identity, rows, cells, producer)
			: new ProbabilityTerminalCacheRecord(identity, rows, cells, producer);
	}

	private static DegenerateTerminalCacheRecord CaptureDegenerateThiefBranchWitness(
		SimulationCompatibilityIdentity identity,
		SimulationResultEvidence evidence,
		ThiefOfferBranchPolicy branchPolicy,
		SimulatorCapability capability)
	{
		if (!TerminalLobbyEvaluator.TrySelectDegenerateThiefBranch(
			evidence.Records,
			branchPolicy,
			TerminalLobbyEvaluator.ScreeningAttemptCount,
			out var completedRuns))
		{
			throw new ArgumentException(
				"Terminal evidence does not contain a complete degenerate Thief branch.",
				nameof(evidence));
		}

		var rows = evidence.PossibleGameResults.Select(result =>
			new TerminalCacheGameResultFrequency(
				result,
				completedRuns.Count(run => run.GameResult.Equals(result)),
				TerminalLobbyEvaluator.ScreeningAttemptCount));
		var cells = completedRuns
			.GroupBy(run => new
			{
				run.GameResult,
				run.EndingTurn,
				run.VictoryCheckWindow
			})
			.Select(group => new TerminalCacheTurnWindowFrequency(
				group.Key.GameResult,
				group.Key.EndingTurn,
				group.Key.VictoryCheckWindow,
				group.Count(),
				TerminalLobbyEvaluator.ScreeningAttemptCount));
		return new DegenerateTerminalCacheRecord(identity, rows, cells, capability);
	}
}
