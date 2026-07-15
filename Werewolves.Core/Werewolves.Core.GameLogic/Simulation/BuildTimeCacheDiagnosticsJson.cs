using System.Text.Json;

namespace Werewolves.Core.GameLogic.Simulation;

public static class BuildTimeCacheDiagnosticsJson
{
	public const string SchemaIdentifier = "terminal-lobby-cache-generation-diagnostics";
	public const int SchemaVersion = 1;

	public static byte[] Write(BuildTimeCacheGenerationDiagnostics diagnostics)
	{
		ArgumentNullException.ThrowIfNull(diagnostics);
		using var stream = new MemoryStream();
		using var writer = new Utf8JsonWriter(stream);
		writer.WriteStartObject();
		writer.WriteString("schema", SchemaIdentifier);
		writer.WriteNumber("version", SchemaVersion);
		writer.WriteString("status", diagnostics.StatusCode);
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
		writer.WriteString("profile", SimulatorProfile.Active.Identity.ProfileId);
		writer.WriteString("version", SimulatorProfile.Active.Identity.Version);
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
		WriteCounts(writer, "omissions", diagnostics.OmissionsByCode);
		WriteCounts(writer, "suspicions", diagnostics.SuspicionsByCode);
		writer.WritePropertyName("incompleteRunSeedMaterial");
		writer.WriteStartArray();
		foreach (var material in diagnostics.IncompleteRunSeedMaterial)
		{
			writer.WriteStartObject();
			writer.WriteString("batchPhase", material.BatchPhase);
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

	private static void WriteCounts(
		Utf8JsonWriter writer,
		string propertyName,
		IReadOnlyDictionary<string, int> values)
	{
		writer.WritePropertyName(propertyName);
		writer.WriteStartArray();
		foreach (var value in values.OrderBy(pair => pair.Key, StringComparer.Ordinal))
		{
			writer.WriteStartObject();
			writer.WriteString("code", value.Key);
			writer.WriteNumber("count", value.Value);
			writer.WriteEndObject();
		}
		writer.WriteEndArray();
	}
}
