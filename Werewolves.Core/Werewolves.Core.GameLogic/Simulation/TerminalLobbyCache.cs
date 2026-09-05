using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public static partial class TerminalLobbyCache
{
	public const string SchemaIdentifier = "terminal-lobby-cache";

	public const int SchemaVersion = 1;

	public static TerminalLobbyCacheRecord Capture(
		SimulationScenario scenario,
		SimulatorCapability capability,
		TerminalLobbyEvaluation evaluation)
	{
		ArgumentNullException.ThrowIfNull(scenario);
		ArgumentNullException.ThrowIfNull(capability);
		ArgumentNullException.ThrowIfNull(evaluation);
		var expectedIdentity = capability.CreateCompatibilityIdentity(scenario);
		return evaluation switch
		{
			AlreadyDecidedTerminalEvaluation value =>
				new AlreadyDecidedTerminalCacheRecord(
					expectedIdentity,
					value.GameResult,
					value.Reason,
					capability),
			DegenerateTerminalEvaluation value =>
				CaptureDegenerate(expectedIdentity, value, capability),
			ProbabilityTerminalEvaluation value =>
				CaptureProbability(expectedIdentity, value.Evidence, capability),
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

	private static DegenerateTerminalCacheRecord CaptureDegenerate(
		SimulationCompatibilityIdentity identity,
		DegenerateTerminalEvaluation evaluation,
		SimulatorCapability capability)
	{
		ValidateEvidenceIdentity(
			identity,
			evaluation.ScreeningEvidence,
			capability,
			LobbyEvaluationDepth.DegenerateScreeningOnly);
		var aggregate = evaluation.SupportingAggregate;
		return new DegenerateTerminalCacheRecord(
			identity,
			aggregate.GameResultFrequencies.Select(frequency =>
				new TerminalCacheGameResultFrequency(
					frequency.GameResult, frequency.Numerator, frequency.Denominator)),
			aggregate.GameResultFrequencyByTurn.Select(frequency =>
				new TerminalCacheTurnWindowFrequency(
					frequency.GameResult, frequency.EndingTurn, frequency.VictoryCheckWindow,
					frequency.Numerator, frequency.Denominator)),
			capability);
	}

	private static ProbabilityTerminalCacheRecord CaptureProbability(
		SimulationCompatibilityIdentity identity,
		SimulationResultEvidence evidence,
		SimulatorCapability capability)
	{
		ValidateEvidenceIdentity(identity, evidence, capability, LobbyEvaluationDepth.FullProbability);
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
		return new ProbabilityTerminalCacheRecord(identity, rows, cells, capability);
	}

	private static void ValidateEvidenceIdentity(
		SimulationCompatibilityIdentity identity,
		SimulationResultEvidence evidence,
		SimulatorCapability capability,
		LobbyEvaluationDepth depth)
	{
		ArgumentNullException.ThrowIfNull(evidence);
		if (!capability.SupportsEvaluationDepth(depth))
		{
			throw new ArgumentException(
				"The terminal evaluation depth is not supported by its Simulator Capability.",
				nameof(evidence));
		}
		if (!evidence.CanonicalScenario.Equals(identity.Scenario)
			|| !evidence.SimulatorProfile.Equals(identity.Profile)
			|| !evidence.DecisionStrategy.Equals(
				capability.HeadlessResponsePolicy.StrategyIdentity))
		{
			throw new ArgumentException(
				"Terminal evidence is incomplete or compatibility-mismatched.",
				nameof(evidence));
		}
	}
}
