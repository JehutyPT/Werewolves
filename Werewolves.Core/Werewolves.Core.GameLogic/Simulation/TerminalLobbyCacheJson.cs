using System.Text.Json;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public static partial class TerminalLobbyCache
{
	public static byte[] Write(TerminalLobbyCacheRecord record)
	{
		ArgumentNullException.ThrowIfNull(record);
		using var stream = new MemoryStream();
		using var writer = new Utf8JsonWriter(stream);
		writer.WriteStartObject();
		writer.WriteString("schema", SchemaIdentifier);
		writer.WriteNumber("version", SchemaVersion);
		writer.WritePropertyName("record");
		WriteRecord(writer, record);
		writer.WriteEndObject();
		writer.Flush();
		return stream.ToArray();
	}

	public static byte[] Write(TerminalLobbyCacheDocument document)
	{
		ArgumentNullException.ThrowIfNull(document);
		using var stream = new MemoryStream();
		using var writer = new Utf8JsonWriter(stream);
		writer.WriteStartObject();
		writer.WriteString("schema", SchemaIdentifier);
		writer.WriteNumber("version", SchemaVersion);
		writer.WritePropertyName("records");
		writer.WriteStartArray();
		foreach (var record in document.Records)
		{
			WriteRecord(writer, record);
		}

		writer.WriteEndArray();
		writer.WriteEndObject();
		writer.Flush();
		return stream.ToArray();
	}

	public static TerminalLobbyCacheReadResult Read(
		ReadOnlySpan<byte> utf8Json,
		SimulationCompatibilityIdentity expectedIdentity)
	{
		ArgumentNullException.ThrowIfNull(expectedIdentity);
		try
		{
			using var json = JsonDocument.Parse(utf8Json.ToArray());
			var root = json.RootElement;
			RequireProperties(root, "schema", "version", "record");
			RequireSchema(root);
			var record = ParseRecord(root.GetProperty("record"));
			if (!record.CompatibilityIdentity.Equals(expectedIdentity))
			{
				throw new FormatException("Incompatible identity.");
			}

			if (!Write(record).AsSpan().SequenceEqual(utf8Json))
			{
				throw new FormatException("The payload is not canonical.");
			}

			return new TerminalLobbyCacheReadResult(record, Rejection: null);
		}
		catch (Exception exception) when (IsUnusablePayload(exception))
		{
			return new TerminalLobbyCacheReadResult(
				Record: null,
				"The cache record is unusable.");
		}
	}

	public static TerminalLobbyCacheDocumentReadResult ReadDocument(
		ReadOnlySpan<byte> utf8Json)
	{
		try
		{
			using var json = JsonDocument.Parse(utf8Json.ToArray());
			var root = json.RootElement;
			RequireProperties(root, "schema", "version", "records");
			RequireSchema(root);
			var records = root
				.GetProperty("records")
				.EnumerateArray()
				.Select(ParseRecord)
				.ToArray();
			var document = CreateDocument(records);
			if (!Write(document).AsSpan().SequenceEqual(utf8Json))
			{
				throw new FormatException("The payload is not canonical.");
			}

			return new TerminalLobbyCacheDocumentReadResult(
				document,
				Rejection: null);
		}
		catch (Exception exception) when (IsUnusablePayload(exception))
		{
			return new TerminalLobbyCacheDocumentReadResult(
				Document: null,
				"The cache document is unusable.");
		}
	}

	private static void WriteRecord(
		Utf8JsonWriter writer,
		TerminalLobbyCacheRecord record)
	{
		writer.WriteStartObject();
		writer.WriteString("identity", record.CompatibilityIdentity.ToString());
		switch (record)
		{
			case AlreadyDecidedTerminalCacheRecord decided:
				writer.WriteString("kind", "alreadyDecided");
				writer.WritePropertyName("result");
				WriteResult(writer, decided.GameResult);
				writer.WriteNumber("reason", (int)decided.Reason);
				break;
			case DegenerateTerminalCacheRecord degenerate:
				writer.WriteString("kind", "degenerate");
				WriteAggregate(writer, degenerate);
				writer.WriteNumber("inclusiveEndingTurnCutoff", 1);
				break;
			case ProbabilityTerminalCacheRecord probability:
				writer.WriteString("kind", "probability");
				WriteAggregate(writer, probability);
				break;
			default:
				throw new ArgumentException(
					"Unknown terminal cache record.",
					nameof(record));
		}

		writer.WriteEndObject();
	}

	private static void WriteAggregate(
		Utf8JsonWriter writer,
		AggregateTerminalCacheRecord record)
	{
		writer.WriteNumber("attempted", record.AttemptedRunCount);
		writer.WriteNumber("completed", record.CompletedRunCount);
		writer.WriteNumber("incomplete", record.IncompleteRunCount);
		writer.WritePropertyName("results");
		writer.WriteStartArray();
		foreach (var row in record.GameResultFrequencies)
		{
			writer.WriteStartObject();
			writer.WritePropertyName("result");
			WriteResult(writer, row.GameResult);
			writer.WriteNumber("numerator", row.Numerator);
			writer.WriteNumber("denominator", row.Denominator);
			writer.WriteEndObject();
		}

		writer.WriteEndArray();
		writer.WritePropertyName("cells");
		writer.WriteStartArray();
		foreach (var cell in record.GameResultFrequencyByTurn)
		{
			writer.WriteStartObject();
			writer.WritePropertyName("result");
			WriteResult(writer, cell.GameResult);
			writer.WriteNumber("turn", cell.EndingTurn);
			writer.WriteNumber("window", (int)cell.VictoryCheckWindow);
			writer.WriteNumber("numerator", cell.Numerator);
			writer.WriteNumber("denominator", cell.Denominator);
			writer.WriteEndObject();
		}

		writer.WriteEndArray();
	}

	private static void WriteResult(Utf8JsonWriter writer, GameResult result)
	{
		writer.WriteStartObject();
		switch (result)
		{
			case SingleFactionGameResult single:
				writer.WriteNumber("kind", 0);
				writer.WritePropertyName("factions");
				writer.WriteStartArray();
				writer.WriteNumberValue((int)single.Faction);
				writer.WriteEndArray();
				break;
			case SharedVictoryGameResult shared:
				writer.WriteNumber("kind", 1);
				writer.WritePropertyName("factions");
				writer.WriteStartArray();
				foreach (var faction in shared.Factions)
				{
					writer.WriteNumberValue((int)faction);
				}

				writer.WriteEndArray();
				break;
			case NoWinnerGameResult:
				writer.WriteNumber("kind", 2);
				writer.WritePropertyName("factions");
				writer.WriteStartArray();
				writer.WriteEndArray();
				break;
			default:
				throw new ArgumentException(
					"Unknown Game Result.",
					nameof(result));
		}

		writer.WriteEndObject();
	}

	private static TerminalLobbyCacheRecord ParseRecord(JsonElement element)
	{
		var identity = SimulationCompatibilityIdentity.Parse(
			RequiredString(element, "identity"));
		var kind = RequiredString(element, "kind");
		if (kind == "alreadyDecided")
		{
			RequireProperties(element, "identity", "kind", "result", "reason");
			return new AlreadyDecidedTerminalCacheRecord(
				identity,
				ParseResult(element.GetProperty("result")),
				RequiredEnum<AlreadyDecidedReason>(element, "reason"));
		}

		var degenerate = kind == "degenerate";
		if (!degenerate && kind != "probability")
		{
			throw new FormatException("Unknown kind.");
		}

		RequireProperties(
			element,
			degenerate
				? [
					"identity", "kind", "attempted", "completed", "incomplete",
					"results", "cells", "inclusiveEndingTurnCutoff"
				]
				: [
					"identity", "kind", "attempted", "completed", "incomplete",
					"results", "cells"
				]);
		var policy = degenerate
			? TerminalLobbyEvaluator.ScreeningAttemptCount
			: TerminalLobbyEvaluator.ProbabilityAttemptCount;
		if (RequiredInt(element, "attempted") != policy
			|| RequiredInt(element, "completed") != policy
			|| RequiredInt(element, "incomplete") != 0
			|| (degenerate
				&& RequiredInt(element, "inclusiveEndingTurnCutoff") != 1))
		{
			throw new FormatException("Invalid policy counts.");
		}

		var rows = element
			.GetProperty("results")
			.EnumerateArray()
			.Select(ParseFrequency)
			.ToArray();
		var cells = element
			.GetProperty("cells")
			.EnumerateArray()
			.Select(ParseCell)
			.ToArray();
		return degenerate
			? new DegenerateTerminalCacheRecord(identity, rows, cells)
			: new ProbabilityTerminalCacheRecord(identity, rows, cells);
	}

	private static TerminalCacheGameResultFrequency ParseFrequency(JsonElement element)
	{
		RequireProperties(element, "result", "numerator", "denominator");
		return new TerminalCacheGameResultFrequency(
			ParseResult(element.GetProperty("result")),
			RequiredInt(element, "numerator"),
			RequiredInt(element, "denominator"));
	}

	private static TerminalCacheTurnWindowFrequency ParseCell(JsonElement element)
	{
		RequireProperties(
			element,
			"result",
			"turn",
			"window",
			"numerator",
			"denominator");
		return new TerminalCacheTurnWindowFrequency(
			ParseResult(element.GetProperty("result")),
			RequiredInt(element, "turn"),
			RequiredEnum<VictoryCheckWindow>(element, "window"),
			RequiredInt(element, "numerator"),
			RequiredInt(element, "denominator"));
	}

	private static GameResult ParseResult(JsonElement element)
	{
		RequireProperties(element, "kind", "factions");
		var kind = RequiredInt(element, "kind");
		var factions = element
			.GetProperty("factions")
			.EnumerateArray()
			.Select(factionElement =>
			{
				if (!factionElement.TryGetInt32(out var identifier)
					|| !Enum.IsDefined((Faction)identifier))
				{
					throw new FormatException("Unknown Faction identifier.");
				}

				return (Faction)identifier;
			})
			.ToArray();
		return kind switch
		{
			0 when factions.Length == 1 => new SingleFactionGameResult(factions[0]),
			1 => new SharedVictoryGameResult(factions),
			2 when factions.Length == 0 => new NoWinnerGameResult(),
			_ => throw new FormatException("Invalid Game Result.")
		};
	}

	private static void RequireSchema(JsonElement root)
	{
		if (RequiredString(root, "schema") != SchemaIdentifier
			|| RequiredInt(root, "version") != SchemaVersion)
		{
			throw new FormatException("Unsupported schema.");
		}
	}

	private static string RequiredString(JsonElement element, string name)
	{
		var property = element.GetProperty(name);
		if (property.ValueKind != JsonValueKind.String)
		{
			throw new FormatException($"{name} must be a string.");
		}

		return property.GetString()!;
	}

	private static int RequiredInt(JsonElement element, string name)
	{
		var property = element.GetProperty(name);
		if (property.ValueKind != JsonValueKind.Number
			|| !property.TryGetInt32(out var value))
		{
			throw new FormatException($"{name} must be an integer.");
		}

		return value;
	}

	private static T RequiredEnum<T>(JsonElement element, string name)
		where T : struct, Enum
	{
		var value = RequiredInt(element, name);
		var parsed = (T)Enum.ToObject(typeof(T), value);
		if (!Enum.IsDefined(parsed))
		{
			throw new FormatException($"{name} is not a defined {typeof(T).Name}.");
		}

		return parsed;
	}

	private static void RequireProperties(
		JsonElement element,
		params string[] expected)
	{
		if (element.ValueKind != JsonValueKind.Object)
		{
			throw new FormatException("A JSON object was required.");
		}

		var actual = element
			.EnumerateObject()
			.Select(property => property.Name)
			.ToArray();
		if (actual.Length != expected.Length
			|| actual.Distinct(StringComparer.Ordinal).Count() != actual.Length
			|| !actual.SequenceEqual(expected, StringComparer.Ordinal))
		{
			throw new FormatException(
				"An unexpected, missing, duplicate, or out-of-order field was found.");
		}
	}

	private static bool IsUnusablePayload(Exception exception) => exception is
		JsonException or
		FormatException or
		ArgumentException or
		InvalidOperationException or
		OverflowException or
		KeyNotFoundException;
}
