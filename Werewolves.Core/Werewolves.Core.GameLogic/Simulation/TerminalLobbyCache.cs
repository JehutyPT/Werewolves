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
		ArgumentNullException.ThrowIfNull(expectedIdentity);
		ArgumentNullException.ThrowIfNull(evaluation);
		return evaluation switch
		{
			AlreadyDecidedTerminalEvaluation value =>
				new AlreadyDecidedTerminalCacheRecord(
					expectedIdentity,
					value.GameResult,
					value.Reason),
			DegenerateTerminalEvaluation value =>
				CaptureAggregate(expectedIdentity, value.ScreeningEvidence, degenerate: true),
			ProbabilityTerminalEvaluation value =>
				CaptureAggregate(expectedIdentity, value.Evidence, degenerate: false),
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
			_ = ClassifyCurrent(expectedIdentity);
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
		bool degenerate)
	{
		ArgumentNullException.ThrowIfNull(evidence);
		if (!evidence.CanonicalScenario.Equals(identity.Scenario)
			|| !evidence.SimulatorProfile.Equals(identity.Profile)
			|| !evidence.DecisionStrategy.Equals(BaselineRandomDecisionStrategy.Identity)
			|| evidence.IncompleteRunCount != 0)
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
			? new DegenerateTerminalCacheRecord(identity, rows, cells)
			: new ProbabilityTerminalCacheRecord(identity, rows, cells);
	}
}
