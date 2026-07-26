using System.Security.Cryptography;
using System.Text.Json;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public sealed record BuildTimeCacheDiagnosticsReadResult(
	BuildTimeCacheGenerationDiagnostics? Diagnostics,
	string? Rejection);

public static class BuildTimeCacheDiagnosticsJson
{
	public const string SchemaIdentifier = "terminal-lobby-cache-generation-diagnostics";
	public const int SchemaVersion = 1;

	public static byte[] Write(
		BuildTimeCacheGenerationDiagnostics diagnostics,
		byte[]? artifactBytes = null)
	{
		ArgumentNullException.ThrowIfNull(diagnostics);
		ValidateSemantics(diagnostics, artifactBytes);
		return WriteCanonical(diagnostics);
	}

	public static BuildTimeCacheDiagnosticsReadResult Read(
		ReadOnlySpan<byte> utf8Json,
		byte[]? artifactBytes)
	{
		try
		{
			using var json = JsonDocument.Parse(utf8Json.ToArray());
			var root = json.RootElement;
			RequireProperties(
				root,
				"schema",
				"version",
				"status",
				"generator",
				"cache",
				"simulator",
				"scenarios",
				"omissions",
				"suspicions",
				"incompleteRunSeedMaterial",
				"artifact");
			if (RequiredString(root, "schema") != SchemaIdentifier
				|| RequiredInt(root, "version") != SchemaVersion)
			{
				throw new FormatException("Unsupported diagnostics schema.");
			}

			var status = ParseStatus(RequiredString(root, "status"));
			var generator = root.GetProperty("generator");
			RequireProperties(generator, "id", "version");
			var generatorIdentifier = RequiredString(generator, "id");
			var generatorVersion = RequiredString(generator, "version");

			var cache = root.GetProperty("cache");
			RequireProperties(cache, "schema", "version");
			if (RequiredString(cache, "schema") != TerminalLobbyCache.SchemaIdentifier
				|| RequiredInt(cache, "version") != TerminalLobbyCache.SchemaVersion)
			{
				throw new FormatException("Unexpected cache identity.");
			}

			var simulator = root.GetProperty("simulator");
			RequireProperties(simulator, "profile", "version", "decisionStrategy");
			var profile = new SimulatorProfileIdentity(
				RequiredString(simulator, "profile"),
				RequiredString(simulator, "version"));
			var decisionStrategy = DecisionStrategyIdentity.Parse(
				RequiredString(simulator, "decisionStrategy"));
			if (profile != SimulatorProfile.LegacyCore.Identity
				|| !decisionStrategy.Equals(BaselineRandomDecisionStrategy.Identity))
			{
				throw new FormatException("Unexpected simulator identity.");
			}

			var scenarios = root.GetProperty("scenarios");
			RequireProperties(
				scenarios,
				"total",
				"enumerated",
				"alreadyDecided",
				"degenerate",
				"probability",
				"omitted");
			var omissions = ParseCounts(
				root.GetProperty("omissions"),
				ParseOmissionCode);
			var suspicions = ParseCounts(
				root.GetProperty("suspicions"),
				ParseSuspicionCode);
			var seeds = ParseSeeds(root.GetProperty("incompleteRunSeedMaterial"));
			var artifact = ParseArtifact(root.GetProperty("artifact"));
			var diagnostics = new BuildTimeCacheGenerationDiagnostics(
				generatorIdentifier,
				generatorVersion,
				status,
				RequiredInt(scenarios, "total"),
				RequiredInt(scenarios, "enumerated"),
				RequiredInt(scenarios, "alreadyDecided"),
				RequiredInt(scenarios, "degenerate"),
				RequiredInt(scenarios, "probability"),
				RequiredInt(scenarios, "omitted"),
				omissions,
				suspicions,
				seeds,
				artifact);
			ValidateSemantics(diagnostics, artifactBytes);
			if (!WriteCanonical(diagnostics).AsSpan().SequenceEqual(utf8Json))
			{
				throw new FormatException("The diagnostics payload is not canonical.");
			}

			return new BuildTimeCacheDiagnosticsReadResult(diagnostics, Rejection: null);
		}
		catch (Exception exception) when (IsUnusablePayload(exception))
		{
			return new BuildTimeCacheDiagnosticsReadResult(
				Diagnostics: null,
				$"The diagnostics payload is unusable: {exception.Message}");
		}
	}

	internal static string StatusCode(BuildTimeCacheGenerationStatus status) => status switch
	{
		BuildTimeCacheGenerationStatus.Completed => "completed",
		BuildTimeCacheGenerationStatus.Cancelled => "cancelled",
		BuildTimeCacheGenerationStatus.Failed => "failed",
		_ => throw new ArgumentOutOfRangeException(nameof(status))
	};

	internal static byte[] WriteCanonicalFixture(
		BuildTimeCacheGenerationDiagnostics diagnostics)
	{
		ArgumentNullException.ThrowIfNull(diagnostics);
		return WriteCanonical(diagnostics);
	}

	private static byte[] WriteCanonical(BuildTimeCacheGenerationDiagnostics diagnostics)
	{
		using var stream = new MemoryStream();
		using var writer = new Utf8JsonWriter(stream);
		writer.WriteStartObject();
		writer.WriteString("schema", SchemaIdentifier);
		writer.WriteNumber("version", SchemaVersion);
		writer.WriteString("status", StatusCode(diagnostics.Status));
		writer.WritePropertyName("generator");
		writer.WriteStartObject();
		writer.WriteString("id", diagnostics.GeneratorIdentifier);
		writer.WriteString("version", diagnostics.GeneratorVersion);
		writer.WriteEndObject();
		writer.WritePropertyName("cache");
		writer.WriteStartObject();
		writer.WriteString("schema", TerminalLobbyCache.SchemaIdentifier);
		writer.WriteNumber("version", TerminalLobbyCache.SchemaVersion);
		writer.WriteEndObject();
		writer.WritePropertyName("simulator");
		writer.WriteStartObject();
		writer.WriteString("profile", SimulatorProfile.LegacyCore.Identity.ProfileId);
		writer.WriteString("version", SimulatorProfile.LegacyCore.Identity.Version);
		writer.WriteString("decisionStrategy", BaselineRandomDecisionStrategy.Identity.ToString());
		writer.WriteEndObject();
		writer.WritePropertyName("scenarios");
		writer.WriteStartObject();
		writer.WriteNumber("total", diagnostics.TotalScenarioCount);
		writer.WriteNumber("enumerated", diagnostics.EnumeratedScenarioCount);
		writer.WriteNumber("alreadyDecided", diagnostics.AlreadyDecidedCount);
		writer.WriteNumber("degenerate", diagnostics.DegenerateCount);
		writer.WriteNumber("probability", diagnostics.ProbabilityCount);
		writer.WriteNumber("omitted", diagnostics.OmittedCount);
		writer.WriteEndObject();
		WriteCounts(
			writer,
			"omissions",
			diagnostics.OmissionsByCode,
			OmissionCode);
		WriteCounts(
			writer,
			"suspicions",
			diagnostics.SuspicionsByCode,
			SuspicionCode);
		writer.WritePropertyName("incompleteRunSeedMaterial");
		writer.WriteStartArray();
		foreach (var material in diagnostics.IncompleteRunSeedMaterial)
		{
			writer.WriteStartObject();
			writer.WriteString("batchPhase", BatchPhaseCode(material.BatchPhase));
			writer.WriteString("runSeedMaterial", material.RunSeedMaterial.ToString());
			writer.WriteEndObject();
		}

		writer.WriteEndArray();
		writer.WritePropertyName("artifact");
		if (diagnostics.Artifact is not { } artifact)
		{
			writer.WriteNullValue();
		}
		else
		{
			writer.WriteStartObject();
			writer.WriteString("logicalName", artifact.LogicalName);
			writer.WriteString("schema", artifact.SchemaIdentifier);
			writer.WriteNumber("version", artifact.SchemaVersion);
			writer.WriteString("profile", artifact.ProfileIdentifier);
			writer.WriteString("profileVersion", artifact.ProfileVersion);
			writer.WriteNumber("records", artifact.RecordCount);
			writer.WriteString("sha256", artifact.Sha256);
			writer.WriteNumber("bytes", artifact.ByteLength);
			writer.WriteEndObject();
		}

		writer.WriteEndObject();
		writer.Flush();
		return stream.ToArray();
	}

	private static void ValidateSemantics(
		BuildTimeCacheGenerationDiagnostics diagnostics,
		byte[]? artifactBytes)
	{
		if (diagnostics.GeneratorIdentifier != BuildTimeTerminalLobbyCacheGenerator.GeneratorIdentifier
			|| diagnostics.GeneratorVersion != BuildTimeTerminalLobbyCacheGenerator.GeneratorVersion)
		{
			throw new FormatException("Unexpected generator identity.");
		}

		if (!Enum.IsDefined(diagnostics.Status))
		{
			throw new FormatException("Unknown generation status.");
		}

		var counts = new[]
		{
			diagnostics.TotalScenarioCount,
			diagnostics.EnumeratedScenarioCount,
			diagnostics.AlreadyDecidedCount,
			diagnostics.DegenerateCount,
			diagnostics.ProbabilityCount,
			diagnostics.OmittedCount
		};
		var catalogScenarioCount = TerminalLobbyScenarioCatalog.EnumerateLegacyCore().Count;
		if (counts.Any(value => value < 0)
			|| diagnostics.TotalScenarioCount != catalogScenarioCount
			|| diagnostics.EnumeratedScenarioCount > diagnostics.TotalScenarioCount)
		{
			throw new FormatException("Invalid scenario counts.");
		}

		if (diagnostics.Status == BuildTimeCacheGenerationStatus.Completed
			&& (diagnostics.TotalScenarioCount != catalogScenarioCount
				|| diagnostics.EnumeratedScenarioCount != catalogScenarioCount))
		{
			throw new FormatException(
				"Completed diagnostics require the complete current scenario catalog.");
		}

		ValidateCounts(diagnostics.OmissionsByCode, nameof(diagnostics.OmissionsByCode));
		ValidateCounts(diagnostics.SuspicionsByCode, nameof(diagnostics.SuspicionsByCode));
		if (diagnostics.OmittedCount != diagnostics.OmissionsByCode.Values.Sum())
		{
			throw new FormatException("The omission count equation is invalid.");
		}

		var classifiedCount = (long)diagnostics.AlreadyDecidedCount
			+ diagnostics.DegenerateCount
			+ diagnostics.ProbabilityCount
			+ diagnostics.OmittedCount;
		if (diagnostics.Status == BuildTimeCacheGenerationStatus.Completed
			? classifiedCount != diagnostics.EnumeratedScenarioCount
			: classifiedCount > diagnostics.EnumeratedScenarioCount)
		{
			throw new FormatException("The scenario count equation is invalid.");
		}

		ValidateSeeds(diagnostics);
		if (diagnostics.Status == BuildTimeCacheGenerationStatus.Completed)
		{
			if (diagnostics.Artifact is null || artifactBytes is null)
			{
				throw new FormatException("Completed diagnostics require an artifact.");
			}

			ValidateArtifact(diagnostics, artifactBytes);
		}
		else if (diagnostics.Artifact is not null || artifactBytes is not null)
		{
			throw new FormatException("Terminal diagnostics cannot claim an artifact.");
		}
	}

	private static void ValidateCounts<TCode>(
		IReadOnlyDictionary<TCode, int> counts,
		string name)
		where TCode : struct, Enum
	{
		ArgumentNullException.ThrowIfNull(counts);
		if (counts.Any(pair => !Enum.IsDefined(pair.Key) || pair.Value <= 0))
		{
			throw new FormatException($"{name} contains an unknown code or invalid count.");
		}
	}

	private static void ValidateSeeds(BuildTimeCacheGenerationDiagnostics diagnostics)
	{
		ArgumentNullException.ThrowIfNull(diagnostics.IncompleteRunSeedMaterial);
		var seeds = diagnostics.IncompleteRunSeedMaterial;
		var catalogIdentities = TerminalLobbyScenarioCatalog.EnumerateLegacyCore()
			.Select(entry => entry.Identity.ToString())
			.ToHashSet(StringComparer.Ordinal);
		if (seeds.Any(seed =>
				seed is null
				|| !Enum.IsDefined(seed.BatchPhase)
				|| seed.RunSeedMaterial is null
				|| seed.RunSeedMaterial.CompatibilityIdentity.Profile != SimulatorProfile.LegacyCore.Identity
				|| !catalogIdentities.Contains(
					seed.RunSeedMaterial.CompatibilityIdentity.ToString())
				|| !seed.RunSeedMaterial.DecisionStrategyIdentity.Equals(
					BaselineRandomDecisionStrategy.Identity)
				|| seed.RunSeedMaterial.RunNumber >= (seed.BatchPhase switch
				{
					BuildTimeBatchPhase.Screening => TerminalLobbyEvaluator.ScreeningAttemptCount,
					BuildTimeBatchPhase.Probability => TerminalLobbyEvaluator.ProbabilityAttemptCount,
					_ => 0
				})))
		{
			throw new FormatException("Invalid incomplete Run Seed Material.");
		}

		var canonical = seeds
			.OrderBy(seed => seed.RunSeedMaterial.CompatibilityIdentity.ToString(), StringComparer.Ordinal)
			.ThenBy(seed => seed.BatchPhase)
			.ThenBy(seed => seed.RunSeedMaterial.RunNumber)
			.ToArray();
		if (!seeds.SequenceEqual(canonical)
			|| seeds.Select(seed => $"{seed.BatchPhase}|{seed.RunSeedMaterial}")
				.Distinct(StringComparer.Ordinal)
				.Count() != seeds.Count)
		{
			throw new FormatException("Incomplete Run Seed Material is duplicated or out of order.");
		}

		var reportedCount = diagnostics.SuspicionsByCode.GetValueOrDefault(
			BuildTimeCacheSuspicionCode.IncompleteRun);
		if (reportedCount != seeds.Count)
		{
			throw new FormatException("The incomplete-run suspicion count is inconsistent.");
		}
	}

	private static void ValidateArtifact(
		BuildTimeCacheGenerationDiagnostics diagnostics,
		byte[] artifactBytes)
	{
		var artifact = diagnostics.Artifact!;
		if (artifact.LogicalName != BuildTimeTerminalLobbyCacheGenerator.ArtifactLogicalName
			|| artifact.SchemaIdentifier != TerminalLobbyCache.SchemaIdentifier
			|| artifact.SchemaVersion != TerminalLobbyCache.SchemaVersion
			|| artifact.ProfileIdentifier != SimulatorProfile.LegacyCore.Identity.ProfileId
			|| artifact.ProfileVersion != SimulatorProfile.LegacyCore.Identity.Version
			|| artifact.RecordCount < 0
			|| artifact.ByteLength != artifactBytes.Length
			|| artifact.Sha256.Length != 64
			|| artifact.Sha256.Any(character =>
				!(character is >= '0' and <= '9' or >= 'a' and <= 'f')))
		{
			throw new FormatException("Invalid artifact identity or measurements.");
		}

		var hash = Convert.ToHexString(SHA256.HashData(artifactBytes)).ToLowerInvariant();
		if (artifact.Sha256 != hash)
		{
			throw new FormatException("The artifact hash does not match its bytes.");
		}

		var read = TerminalLobbyCache.ReadDocument(artifactBytes);
		if (read.Document is not { } document
			|| document.Records.Count != artifact.RecordCount
			|| document.Records.Any(record =>
				record.CompatibilityIdentity.Profile != SimulatorProfile.LegacyCore.Identity)
			|| document.Records.OfType<AlreadyDecidedTerminalCacheRecord>().Count()
				!= diagnostics.AlreadyDecidedCount
			|| document.Records.OfType<DegenerateTerminalCacheRecord>().Count()
				!= diagnostics.DegenerateCount
			|| document.Records.OfType<ProbabilityTerminalCacheRecord>().Count()
				!= diagnostics.ProbabilityCount
			|| artifact.RecordCount != (long)diagnostics.AlreadyDecidedCount
				+ diagnostics.DegenerateCount
				+ diagnostics.ProbabilityCount)
		{
			throw new FormatException("The artifact record inventory is inconsistent.");
		}
	}

	private static void WriteCounts<TCode>(
		Utf8JsonWriter writer,
		string propertyName,
		IReadOnlyDictionary<TCode, int> values,
		Func<TCode, string> code)
		where TCode : struct, Enum
	{
		writer.WritePropertyName(propertyName);
		writer.WriteStartArray();
		foreach (var value in values.OrderBy(pair => code(pair.Key), StringComparer.Ordinal))
		{
			writer.WriteStartObject();
			writer.WriteString("code", code(value.Key));
			writer.WriteNumber("count", value.Value);
			writer.WriteEndObject();
		}

		writer.WriteEndArray();
	}

	private static IReadOnlyDictionary<TCode, int> ParseCounts<TCode>(
		JsonElement element,
		Func<string, TCode> parseCode)
		where TCode : struct, Enum
	{
		if (element.ValueKind != JsonValueKind.Array)
		{
			throw new FormatException("A count array was required.");
		}

		var result = new Dictionary<TCode, int>();
		foreach (var item in element.EnumerateArray())
		{
			RequireProperties(item, "code", "count");
			result.Add(parseCode(RequiredString(item, "code")), RequiredInt(item, "count"));
		}

		return result;
	}

	private static IReadOnlyList<BuildTimeIncompleteRunDiagnostic> ParseSeeds(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Array)
		{
			throw new FormatException("An incomplete Run Seed Material array was required.");
		}

		return element.EnumerateArray().Select(item =>
		{
			RequireProperties(item, "batchPhase", "runSeedMaterial");
			return new BuildTimeIncompleteRunDiagnostic(
				ParseBatchPhase(RequiredString(item, "batchPhase")),
				RunSeedMaterial.Parse(RequiredString(item, "runSeedMaterial")));
		}).ToArray();
	}

	private static BuildTimeCacheArtifactDiagnostics? ParseArtifact(JsonElement element)
	{
		if (element.ValueKind == JsonValueKind.Null)
		{
			return null;
		}

		RequireProperties(
			element,
			"logicalName",
			"schema",
			"version",
			"profile",
			"profileVersion",
			"records",
			"sha256",
			"bytes");
		return new BuildTimeCacheArtifactDiagnostics(
			RequiredString(element, "logicalName"),
			RequiredString(element, "schema"),
			RequiredInt(element, "version"),
			RequiredString(element, "profile"),
			RequiredString(element, "profileVersion"),
			RequiredInt(element, "records"),
			RequiredString(element, "sha256"),
			RequiredInt(element, "bytes"));
	}

	private static BuildTimeCacheGenerationStatus ParseStatus(string value) => value switch
	{
		"completed" => BuildTimeCacheGenerationStatus.Completed,
		"cancelled" => BuildTimeCacheGenerationStatus.Cancelled,
		"failed" => BuildTimeCacheGenerationStatus.Failed,
		_ => throw new FormatException("Unknown generation status.")
	};

	private static string BatchPhaseCode(BuildTimeBatchPhase phase) => phase switch
	{
		BuildTimeBatchPhase.Screening => "screening",
		BuildTimeBatchPhase.Probability => "probability",
		_ => throw new ArgumentOutOfRangeException(nameof(phase))
	};

	private static BuildTimeBatchPhase ParseBatchPhase(string value) => value switch
	{
		"screening" => BuildTimeBatchPhase.Screening,
		"probability" => BuildTimeBatchPhase.Probability,
		_ => throw new FormatException("Unknown batch phase.")
	};

	private static string OmissionCode(BuildTimeCacheOmissionCode code) => code switch
	{
		BuildTimeCacheOmissionCode.ScreeningIncomplete => "screening-incomplete",
		BuildTimeCacheOmissionCode.ProbabilityIncomplete => "probability-incomplete",
		BuildTimeCacheOmissionCode.CouldNotEvaluate => "could-not-evaluate",
		BuildTimeCacheOmissionCode.TerminalRecordRejected => "terminal-record-rejected",
		_ => throw new ArgumentOutOfRangeException(nameof(code))
	};

	private static BuildTimeCacheOmissionCode ParseOmissionCode(string value) => value switch
	{
		"screening-incomplete" => BuildTimeCacheOmissionCode.ScreeningIncomplete,
		"probability-incomplete" => BuildTimeCacheOmissionCode.ProbabilityIncomplete,
		"could-not-evaluate" => BuildTimeCacheOmissionCode.CouldNotEvaluate,
		"terminal-record-rejected" => BuildTimeCacheOmissionCode.TerminalRecordRejected,
		_ => throw new FormatException("Unknown omission code.")
	};

	private static string SuspicionCode(BuildTimeCacheSuspicionCode code) => code switch
	{
		BuildTimeCacheSuspicionCode.IncompleteRun => "incomplete-run",
		BuildTimeCacheSuspicionCode.TerminalKindMismatch => "terminal-kind-mismatch",
		_ => throw new ArgumentOutOfRangeException(nameof(code))
	};

	private static BuildTimeCacheSuspicionCode ParseSuspicionCode(string value) => value switch
	{
		"incomplete-run" => BuildTimeCacheSuspicionCode.IncompleteRun,
		"terminal-kind-mismatch" => BuildTimeCacheSuspicionCode.TerminalKindMismatch,
		_ => throw new FormatException("Unknown suspicion code.")
	};

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

	private static void RequireProperties(JsonElement element, params string[] expected)
	{
		if (element.ValueKind != JsonValueKind.Object)
		{
			throw new FormatException("A JSON object was required.");
		}

		var actual = element.EnumerateObject().Select(property => property.Name).ToArray();
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
