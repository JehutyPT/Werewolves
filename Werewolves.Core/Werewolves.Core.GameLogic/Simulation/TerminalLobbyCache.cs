using System.Text;
using System.Text.Json;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public abstract record TerminalLobbyCacheRecord
{
	public SimulationCompatibilityIdentity CompatibilityIdentity { get; }
	protected TerminalLobbyCacheRecord(SimulationCompatibilityIdentity compatibilityIdentity) =>
		CompatibilityIdentity = compatibilityIdentity ?? throw new ArgumentNullException(nameof(compatibilityIdentity));
}

public sealed record AlreadyDecidedTerminalCacheRecord : TerminalLobbyCacheRecord
{
	public GameResult GameResult { get; }
	public AlreadyDecidedReason Reason { get; }
	public AlreadyDecidedTerminalCacheRecord(SimulationCompatibilityIdentity identity, GameResult gameResult, AlreadyDecidedReason reason) : base(identity)
	{
		GameResult = TerminalLobbyCache.ValidateGameResult(gameResult);
		if (!Enum.IsDefined(reason) || reason == AlreadyDecidedReason.NoLobbyExitVictoryPredicateSatisfied)
			throw new ArgumentOutOfRangeException(nameof(reason));
		Reason = reason;
	}
}

public sealed record TerminalCacheGameResultFrequency
{
	public GameResult GameResult { get; }
	public int Numerator { get; }
	public int Denominator { get; }
	public TerminalCacheGameResultFrequency(GameResult gameResult, int numerator, int denominator)
	{
		GameResult = TerminalLobbyCache.ValidateGameResult(gameResult);
		TerminalLobbyCache.ValidateFrequency(numerator, denominator);
		Numerator = numerator; Denominator = denominator;
	}
}

public sealed record TerminalCacheTurnWindowFrequency
{
	public GameResult GameResult { get; }
	public int EndingTurn { get; }
	public VictoryCheckWindow VictoryCheckWindow { get; }
	public int Numerator { get; }
	public int Denominator { get; }
	public TerminalCacheTurnWindowFrequency(GameResult gameResult, int endingTurn, VictoryCheckWindow window, int numerator, int denominator)
	{
		GameResult = TerminalLobbyCache.ValidateGameResult(gameResult);
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(endingTurn);
		if (!Enum.IsDefined(window)) throw new ArgumentOutOfRangeException(nameof(window));
		TerminalLobbyCache.ValidateFrequency(numerator, denominator);
		if (numerator == 0) throw new ArgumentOutOfRangeException(nameof(numerator));
		EndingTurn = endingTurn; VictoryCheckWindow = window; Numerator = numerator; Denominator = denominator;
	}
}

public abstract record AggregateTerminalCacheRecord : TerminalLobbyCacheRecord
{
	private readonly IReadOnlyList<TerminalCacheGameResultFrequency> _frequencies;
	private readonly IReadOnlyList<TerminalCacheTurnWindowFrequency> _cells;
	public int AttemptedRunCount { get; }
	public int CompletedRunCount { get; }
	public int IncompleteRunCount { get; }
	public IReadOnlyList<TerminalCacheGameResultFrequency> GameResultFrequencies => _frequencies;
	public IReadOnlyList<TerminalCacheTurnWindowFrequency> GameResultFrequencyByTurn => _cells;

	protected AggregateTerminalCacheRecord(SimulationCompatibilityIdentity identity, int policyCount,
		IEnumerable<TerminalCacheGameResultFrequency> frequencies, IEnumerable<TerminalCacheTurnWindowFrequency> cells,
		bool turnOneOnly) : base(identity)
	{
		ArgumentNullException.ThrowIfNull(frequencies); ArgumentNullException.ThrowIfNull(cells);
		var rows = frequencies.OrderBy(x => TerminalLobbyCache.ResultKey(x.GameResult), StringComparer.Ordinal).ToArray();
		var timing = cells.OrderBy(x => x.EndingTurn).ThenBy(x => x.VictoryCheckWindow)
			.ThenBy(x => TerminalLobbyCache.ResultKey(x.GameResult), StringComparer.Ordinal).ToArray();
		TerminalLobbyCache.ValidateAggregate(policyCount, rows, timing, turnOneOnly);
		AttemptedRunCount = CompletedRunCount = policyCount; IncompleteRunCount = 0;
		_frequencies = Array.AsReadOnly(rows); _cells = Array.AsReadOnly(timing);
	}
}

public sealed record DegenerateTerminalCacheRecord : AggregateTerminalCacheRecord
{
	public int InclusiveEndingTurnCutoff => 1;
	public DegenerateTerminalCacheRecord(SimulationCompatibilityIdentity identity,
		IEnumerable<TerminalCacheGameResultFrequency> frequencies, IEnumerable<TerminalCacheTurnWindowFrequency> cells)
		: base(identity, TerminalLobbyEvaluator.ScreeningAttemptCount, frequencies, cells, true) { }
}

public sealed record ProbabilityTerminalCacheRecord : AggregateTerminalCacheRecord
{
	public ProbabilityTerminalCacheRecord(SimulationCompatibilityIdentity identity,
		IEnumerable<TerminalCacheGameResultFrequency> frequencies, IEnumerable<TerminalCacheTurnWindowFrequency> cells)
		: base(identity, TerminalLobbyEvaluator.ProbabilityAttemptCount, frequencies, cells, false) { }
}

public sealed class TerminalLobbyCacheDocument
{
	private readonly IReadOnlyList<TerminalLobbyCacheRecord> _records;
	public IReadOnlyList<TerminalLobbyCacheRecord> Records => _records;
	internal TerminalLobbyCacheDocument(TerminalLobbyCacheRecord[] records) => _records = Array.AsReadOnly(records);
}

public sealed record TerminalLobbyCacheReadResult(TerminalLobbyCacheRecord? Record, string? Rejection)
{ public bool IsUsable => Record is not null; }
public sealed record TerminalLobbyCacheDocumentReadResult(TerminalLobbyCacheDocument? Document, string? Rejection)
{ public bool IsUsable => Document is not null; }

public static class TerminalLobbyCache
{
	public const string SchemaIdentifier = "terminal-lobby-cache";
	public const int SchemaVersion = 1;

	public static TerminalLobbyCacheRecord Capture(SimulationCompatibilityIdentity expectedIdentity, TerminalLobbyEvaluation evaluation)
	{
		ArgumentNullException.ThrowIfNull(expectedIdentity); ArgumentNullException.ThrowIfNull(evaluation);
		return evaluation switch
		{
			AlreadyDecidedTerminalEvaluation value => new AlreadyDecidedTerminalCacheRecord(expectedIdentity, value.GameResult, value.Reason),
			DegenerateTerminalEvaluation value => CaptureAggregate(expectedIdentity, value.ScreeningEvidence, true),
			ProbabilityTerminalEvaluation value => CaptureAggregate(expectedIdentity, value.Evidence, false),
			_ => throw new ArgumentException("Only complete terminal lobby evaluations are cacheable.", nameof(evaluation))
		};
	}

	private static TerminalLobbyCacheRecord CaptureAggregate(SimulationCompatibilityIdentity identity, SimulationResultEvidence evidence, bool degenerate)
	{
		ArgumentNullException.ThrowIfNull(evidence);
		if (!evidence.CanonicalScenario.Equals(identity.Scenario) || !evidence.SimulatorProfile.Equals(identity.Profile)
			|| !evidence.DecisionStrategy.Equals(BaselineRandomDecisionStrategy.Identity)
			|| evidence.IncompleteRunCount != 0)
			throw new ArgumentException("Terminal evidence is incomplete or compatibility-mismatched.", nameof(evidence));
		var rows = evidence.GameResultFrequencies.Select(x => new TerminalCacheGameResultFrequency(x.GameResult, x.Numerator, x.Denominator));
		var cells = evidence.GameResultFrequencyByTurn.Select(x => new TerminalCacheTurnWindowFrequency(x.GameResult, x.EndingTurn, x.VictoryCheckWindow, x.Numerator, x.Denominator));
		return degenerate ? new DegenerateTerminalCacheRecord(identity, rows, cells) : new ProbabilityTerminalCacheRecord(identity, rows, cells);
	}

	public static TerminalLobbyCacheDocument CreateDocument(IEnumerable<TerminalLobbyCacheRecord> records)
	{
		ArgumentNullException.ThrowIfNull(records);
		var values = records.ToArray();
		if (values.Any(x => x is null)) throw new ArgumentException("Records cannot contain null.", nameof(records));
		if (values.Select(x => x.CompatibilityIdentity).Distinct().Count() != values.Length)
			throw new ArgumentException("Only one terminal record is permitted per compatibility identity.", nameof(records));
		return new TerminalLobbyCacheDocument(values.OrderBy(x => x.CompatibilityIdentity.ToString(), StringComparer.Ordinal).ToArray());
	}

	public static bool TryGet(TerminalLobbyCacheDocument document, SimulationCompatibilityIdentity expectedIdentity, out TerminalLobbyCacheRecord record)
	{
		ArgumentNullException.ThrowIfNull(document); ArgumentNullException.ThrowIfNull(expectedIdentity);
		record = document.Records.SingleOrDefault(x => x.CompatibilityIdentity.Equals(expectedIdentity))!;
		return record is not null;
	}

	public static byte[] Write(TerminalLobbyCacheRecord record)
	{
		ArgumentNullException.ThrowIfNull(record);
		using var stream = new MemoryStream(); using var writer = new Utf8JsonWriter(stream);
		writer.WriteStartObject(); writer.WriteString("schema", SchemaIdentifier); writer.WriteNumber("version", SchemaVersion); writer.WritePropertyName("record"); WriteRecord(writer, record); writer.WriteEndObject(); writer.Flush(); return stream.ToArray();
	}

	public static byte[] Write(TerminalLobbyCacheDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);
		using var stream = new MemoryStream(); using var writer = new Utf8JsonWriter(stream);
		writer.WriteStartObject(); writer.WriteString("schema", SchemaIdentifier); writer.WriteNumber("version", SchemaVersion); writer.WritePropertyName("records"); writer.WriteStartArray();
		foreach (var record in document.Records) WriteRecord(writer, record);
		writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush(); return stream.ToArray();
	}

	public static TerminalLobbyCacheReadResult Read(ReadOnlySpan<byte> utf8Json, SimulationCompatibilityIdentity expectedIdentity)
	{
		ArgumentNullException.ThrowIfNull(expectedIdentity);
		try
		{
			using var json = JsonDocument.Parse(utf8Json.ToArray()); var root = json.RootElement;
			RequireProperties(root, "schema", "version", "record"); RequireSchema(root);
			var record = ParseRecord(root.GetProperty("record"));
			if (!record.CompatibilityIdentity.Equals(expectedIdentity)) throw new FormatException("Incompatible identity.");
			if (!Write(record).AsSpan().SequenceEqual(utf8Json)) throw new FormatException("The payload is not canonical.");
			return new(record, null);
		}
		catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException or OverflowException)
		{ return new(null, "The cache record is unusable."); }
	}

	public static TerminalLobbyCacheDocumentReadResult ReadDocument(ReadOnlySpan<byte> utf8Json, IEnumerable<SimulationCompatibilityIdentity> expectedIdentities)
	{
		ArgumentNullException.ThrowIfNull(expectedIdentities);
		try
		{
			var expected = expectedIdentities.ToArray();
			using var json = JsonDocument.Parse(utf8Json.ToArray()); var root = json.RootElement;
			RequireProperties(root, "schema", "version", "records"); RequireSchema(root);
			var records = root.GetProperty("records").EnumerateArray().Select(ParseRecord).ToArray();
			var document = CreateDocument(records);
			if (records.Any(x => !expected.Contains(x.CompatibilityIdentity))) throw new FormatException("Incompatible identity.");
			if (!Write(document).AsSpan().SequenceEqual(utf8Json)) throw new FormatException("The payload is not canonical.");
			return new(document, null);
		}
		catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or InvalidOperationException or OverflowException)
		{ return new(null, "The cache document is unusable."); }
	}

	private static void WriteRecord(Utf8JsonWriter w, TerminalLobbyCacheRecord record)
	{
		w.WriteStartObject(); w.WriteString("identity", record.CompatibilityIdentity.ToString());
		switch (record)
		{
			case AlreadyDecidedTerminalCacheRecord d:
				w.WriteString("kind", "alreadyDecided"); w.WritePropertyName("result"); WriteResult(w, d.GameResult); w.WriteNumber("reason", (int)d.Reason); break;
			case DegenerateTerminalCacheRecord d:
				w.WriteString("kind", "degenerate"); WriteAggregate(w, d); w.WriteNumber("inclusiveEndingTurnCutoff", 1); break;
			case ProbabilityTerminalCacheRecord p:
				w.WriteString("kind", "probability"); WriteAggregate(w, p); break;
			default: throw new ArgumentException("Unknown terminal cache record.", nameof(record));
		}
		w.WriteEndObject();
	}

	private static void WriteAggregate(Utf8JsonWriter w, AggregateTerminalCacheRecord record)
	{
		w.WriteNumber("attempted", record.AttemptedRunCount); w.WriteNumber("completed", record.CompletedRunCount); w.WriteNumber("incomplete", record.IncompleteRunCount);
		w.WritePropertyName("results"); w.WriteStartArray(); foreach (var row in record.GameResultFrequencies) { w.WriteStartObject(); w.WritePropertyName("result"); WriteResult(w, row.GameResult); w.WriteNumber("numerator", row.Numerator); w.WriteNumber("denominator", row.Denominator); w.WriteEndObject(); } w.WriteEndArray();
		w.WritePropertyName("cells"); w.WriteStartArray(); foreach (var cell in record.GameResultFrequencyByTurn) { w.WriteStartObject(); w.WritePropertyName("result"); WriteResult(w, cell.GameResult); w.WriteNumber("turn", cell.EndingTurn); w.WriteNumber("window", (int)cell.VictoryCheckWindow); w.WriteNumber("numerator", cell.Numerator); w.WriteNumber("denominator", cell.Denominator); w.WriteEndObject(); } w.WriteEndArray();
	}

	private static void WriteResult(Utf8JsonWriter w, GameResult result)
	{
		w.WriteStartObject();
		switch (result) { case SingleFactionGameResult s: w.WriteNumber("kind", 0); w.WritePropertyName("factions"); w.WriteStartArray(); w.WriteNumberValue((int)s.Faction); w.WriteEndArray(); break; case SharedVictoryGameResult s: w.WriteNumber("kind", 1); w.WritePropertyName("factions"); w.WriteStartArray(); foreach (var f in s.Factions) w.WriteNumberValue((int)f); w.WriteEndArray(); break; case NoWinnerGameResult: w.WriteNumber("kind", 2); w.WritePropertyName("factions"); w.WriteStartArray(); w.WriteEndArray(); break; default: throw new ArgumentException("Unknown Game Result.", nameof(result)); }
		w.WriteEndObject();
	}

	private static TerminalLobbyCacheRecord ParseRecord(JsonElement e)
	{
		var identity = SimulationCompatibilityIdentity.Parse(RequiredString(e, "identity")); var kind = RequiredString(e, "kind");
		if (kind == "alreadyDecided") { RequireProperties(e, "identity", "kind", "result", "reason"); return new AlreadyDecidedTerminalCacheRecord(identity, ParseResult(e.GetProperty("result")), RequiredEnum<AlreadyDecidedReason>(e, "reason")); }
		var degenerate = kind == "degenerate"; if (!degenerate && kind != "probability") throw new FormatException("Unknown kind.");
		RequireProperties(e, degenerate ? ["identity", "kind", "attempted", "completed", "incomplete", "results", "cells", "inclusiveEndingTurnCutoff"] : ["identity", "kind", "attempted", "completed", "incomplete", "results", "cells"]);
		var policy = degenerate ? TerminalLobbyEvaluator.ScreeningAttemptCount : TerminalLobbyEvaluator.ProbabilityAttemptCount;
		if (RequiredInt(e,"attempted") != policy || RequiredInt(e,"completed") != policy || RequiredInt(e,"incomplete") != 0 || (degenerate && RequiredInt(e,"inclusiveEndingTurnCutoff") != 1)) throw new FormatException("Invalid policy counts.");
		var rows = e.GetProperty("results").EnumerateArray().Select(ParseFrequency).ToArray(); var cells = e.GetProperty("cells").EnumerateArray().Select(ParseCell).ToArray();
		return degenerate ? new DegenerateTerminalCacheRecord(identity, rows, cells) : new ProbabilityTerminalCacheRecord(identity, rows, cells);
	}

	private static TerminalCacheGameResultFrequency ParseFrequency(JsonElement e) { RequireProperties(e,"result","numerator","denominator"); return new(ParseResult(e.GetProperty("result")),RequiredInt(e,"numerator"),RequiredInt(e,"denominator")); }
	private static TerminalCacheTurnWindowFrequency ParseCell(JsonElement e) { RequireProperties(e,"result","turn","window","numerator","denominator"); return new(ParseResult(e.GetProperty("result")),RequiredInt(e,"turn"),RequiredEnum<VictoryCheckWindow>(e,"window"),RequiredInt(e,"numerator"),RequiredInt(e,"denominator")); }
	private static GameResult ParseResult(JsonElement e)
	{
		RequireProperties(e,"kind","factions"); var kind=RequiredInt(e,"kind"); var factions=e.GetProperty("factions").EnumerateArray().Select(x => { if(!x.TryGetInt32(out var n)||!Enum.IsDefined((Faction)n)) throw new FormatException(); return (Faction)n; }).ToArray();
		return kind switch { 0 when factions.Length==1 => new SingleFactionGameResult(factions[0]), 1 => new SharedVictoryGameResult(factions), 2 when factions.Length==0 => new NoWinnerGameResult(), _ => throw new FormatException("Invalid Game Result.") };
	}

	internal static GameResult ValidateGameResult(GameResult gameResult)
	{
		ArgumentNullException.ThrowIfNull(gameResult); if (gameResult.GetType()!=typeof(SingleFactionGameResult) && gameResult.GetType()!=typeof(SharedVictoryGameResult) && gameResult.GetType()!=typeof(NoWinnerGameResult)) throw new ArgumentException("Unknown Game Result.",nameof(gameResult)); return gameResult;
	}
	internal static string ResultKey(GameResult result) => result switch
	{
		SingleFactionGameResult value => $"0:{(int)value.Faction:D10}",
		SharedVictoryGameResult value => $"1:{string.Join(',', value.Factions.Select(x => ((int)x).ToString("D10")))}",
		NoWinnerGameResult => "2:",
		_ => throw new ArgumentException("Unknown Game Result.", nameof(result))
	};
	internal static void ValidateFrequency(int numerator,int denominator) { ArgumentOutOfRangeException.ThrowIfNegative(numerator); ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator); if(numerator>denominator) throw new ArgumentOutOfRangeException(nameof(numerator)); }
	internal static void ValidateAggregate(int policy, TerminalCacheGameResultFrequency[] rows, TerminalCacheTurnWindowFrequency[] cells, bool turnOneOnly)
	{
		if(rows.Length==0 || rows.Any(x=>x.Denominator!=policy) || cells.Any(x=>x.Denominator!=policy) || rows.Select(x=>x.GameResult).Distinct().Count()!=rows.Length || rows.Sum(x=>x.Numerator)!=policy) throw new ArgumentException("Invalid complete Game Result distribution.");
		var inventory=rows.Select(x=>x.GameResult).ToArray(); if(cells.Any(x=>!inventory.Contains(x.GameResult)) || cells.GroupBy(x=>new{x.GameResult,x.EndingTurn,x.VictoryCheckWindow}).Any(g=>g.Count()!=1) || (turnOneOnly && cells.Any(x=>x.EndingTurn>1))) throw new ArgumentException("Invalid Turn/window cells.");
		foreach(var row in rows) if(cells.Where(x=>x.GameResult.Equals(row.GameResult)).Sum(x=>x.Numerator)!=row.Numerator) throw new ArgumentException("Turn/window cells do not reproduce the distribution.");
		if(cells.Sum(x=>x.Numerator)!=policy) throw new ArgumentException("Turn/window cells do not reproduce the distribution.");
	}

	private static void RequireSchema(JsonElement root) { if(RequiredString(root,"schema")!=SchemaIdentifier || RequiredInt(root,"version")!=SchemaVersion) throw new FormatException("Unsupported schema."); }
	private static string RequiredString(JsonElement e,string name) { var p=e.GetProperty(name); if(p.ValueKind!=JsonValueKind.String) throw new FormatException(); return p.GetString()!; }
	private static int RequiredInt(JsonElement e,string name) { var p=e.GetProperty(name); if(p.ValueKind!=JsonValueKind.Number || !p.TryGetInt32(out var value)) throw new FormatException(); return value; }
	private static T RequiredEnum<T>(JsonElement e,string name) where T:struct,Enum { var value=RequiredInt(e,name); var parsed=(T)Enum.ToObject(typeof(T),value); if(!Enum.IsDefined(parsed)) throw new FormatException(); return parsed; }
	private static void RequireProperties(JsonElement e, params string[] expected)
	{
		if(e.ValueKind!=JsonValueKind.Object) throw new FormatException(); var actual=e.EnumerateObject().Select(x=>x.Name).ToArray(); if(actual.Length!=expected.Length || actual.Distinct(StringComparer.Ordinal).Count()!=actual.Length || !actual.SequenceEqual(expected,StringComparer.Ordinal)) throw new FormatException("Unexpected, missing, duplicate, or out-of-order field.");
	}
}
