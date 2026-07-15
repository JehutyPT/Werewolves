using System.Security.Cryptography;
using System.Text.Json;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public sealed record BuildTimeCacheArtifactDiagnostics(
	string LogicalName,
	string SchemaIdentifier,
	int SchemaVersion,
	string ProfileIdentifier,
	string ProfileVersion,
	int RecordCount,
	string Sha256,
	int ByteLength);

public sealed record BuildTimeIncompleteRunDiagnostic(
	string BatchPhase,
	RunSeedMaterial RunSeedMaterial);

public sealed record BuildTimeCacheGenerationDiagnostics(
	string GeneratorIdentifier,
	string GeneratorVersion,
	string StatusCode,
	int TotalScenarioCount,
	int EnumeratedScenarioCount,
	int AlreadyDecidedCount,
	int DegenerateCount,
	int ProbabilityCount,
	int OmittedCount,
	IReadOnlyDictionary<string, int> OmissionsByCode,
	IReadOnlyDictionary<string, int> SuspicionsByCode,
	IReadOnlyList<BuildTimeIncompleteRunDiagnostic> IncompleteRunSeedMaterial,
	BuildTimeCacheArtifactDiagnostics? Artifact);

public sealed record BuildTimeCacheGenerationResult(
	string StatusCode,
	byte[]? ArtifactBytes,
	TerminalLobbyCacheDocument? Document,
	BuildTimeCacheGenerationDiagnostics Diagnostics);

public sealed record BuildTimeCacheGenerationProgress(
	int CompletedScenarioCount,
	int TotalScenarioCount,
	string CanonicalIdentity);

public sealed class BuildTimeTerminalLobbyCacheGenerator
{
	public const string GeneratorIdentifier = "terminal-lobby-cache-generator";
	public const string GeneratorVersion = "1";
	public const string ArtifactLogicalName = "terminal-lobby-cache.json";

	private readonly Func<
		SimulationScenario,
		SimulationCompatibilityIdentity,
		int,
		CancellationToken,
		SimulationBatchSourceEvidence> _executeBatch;
	private readonly Action<BuildTimeCacheGenerationProgress>? _progress;

	public BuildTimeTerminalLobbyCacheGenerator(
		int degreeOfParallelism = 1,
		Action<BuildTimeCacheGenerationProgress>? progress = null)
	{
		ArgumentOutOfRangeException.ThrowIfNegativeOrZero(degreeOfParallelism);
		var executor = new SimulationExecutor();
		_executeBatch = (scenario, identity, count, token) => executor.ExecuteBatch(
			scenario,
			identity,
			count,
			degreeOfParallelism,
			token);
		_progress = progress;
	}

	public BuildTimeTerminalLobbyCacheGenerator(
		Func<SimulationScenario, SimulationCompatibilityIdentity, int, CancellationToken,
			SimulationBatchSourceEvidence> executeBatch,
		Action<BuildTimeCacheGenerationProgress>? progress = null)
	{
		_executeBatch = executeBatch ?? throw new ArgumentNullException(nameof(executeBatch));
		_progress = progress;
	}

	public BuildTimeCacheGenerationResult Generate(
		IEnumerable<TerminalLobbyGenerationScenario>? scenarios = null,
		CancellationToken cancellationToken = default)
	{
		var completeCatalog = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile();
		var selected = (scenarios ?? completeCatalog).ToArray();
		var records = new List<TerminalLobbyCacheRecord>();
		var omitted = new Dictionary<string, int>(StringComparer.Ordinal);
		var suspicions = new Dictionary<string, int>(StringComparer.Ordinal);
		var incompleteSeeds = new List<BuildTimeIncompleteRunDiagnostic>();
		var already = 0;
		var degenerate = 0;
		var probability = 0;

		for (var scenarioIndex = 0; scenarioIndex < selected.Length; scenarioIndex++)
		{
			var entry = selected[scenarioIndex];
			cancellationToken.ThrowIfCancellationRequested();
			var observedBatches = new List<(int AttemptCount, SimulationBatchSourceEvidence Evidence)>();
			var evaluator = new TerminalLobbyEvaluator((scenario, identity, count, token) =>
			{
				var batch = _executeBatch(scenario, identity, count, token);
				observedBatches.Add((count, batch));
				return batch;
			});
			var evaluation = evaluator.Evaluate(entry.Scenario, cancellationToken);
			foreach (var observed in observedBatches)
			{
				var batchPhase = observed.AttemptCount == TerminalLobbyEvaluator.ScreeningAttemptCount
					? "screening"
					: "probability";
				foreach (var run in observed.Evidence.Records.OfType<IncompleteSimulationRun>())
				{
					incompleteSeeds.Add(new BuildTimeIncompleteRunDiagnostic(
						batchPhase,
						run.RunSeedMaterial));
					suspicions["incomplete-run"] = suspicions.GetValueOrDefault("incomplete-run") + 1;
				}
			}

			if (evaluation is CouldNotEvaluateLobbyEvaluation)
			{
				var code = observedBatches.LastOrDefault(batch => batch.Evidence.IncompleteRunCount > 0)
					.AttemptCount switch
				{
					TerminalLobbyEvaluator.ScreeningAttemptCount => "screening-incomplete",
					TerminalLobbyEvaluator.ProbabilityAttemptCount => "probability-incomplete",
					_ => "could-not-evaluate"
				};
				omitted[code] = omitted.GetValueOrDefault(code) + 1;
				ReportProgress();
				continue;
			}

			if (evaluation is not TerminalLobbyEvaluation terminal)
			{
				omitted["could-not-evaluate"] = omitted.GetValueOrDefault("could-not-evaluate") + 1;
				ReportProgress();
				continue;
			}

			if (entry.IsAlreadyDecided != (terminal is AlreadyDecidedTerminalEvaluation))
			{
				suspicions["terminal-kind-mismatch"] = suspicions.GetValueOrDefault("terminal-kind-mismatch") + 1;
			}

			try
			{
				records.Add(TerminalLobbyCache.Capture(entry.Identity, terminal));
				switch (terminal)
				{
					case AlreadyDecidedTerminalEvaluation: already++; break;
					case DegenerateTerminalEvaluation: degenerate++; break;
					case ProbabilityTerminalEvaluation: probability++; break;
				}
			}
			catch (ArgumentException)
			{
				omitted["terminal-record-rejected"] = omitted.GetValueOrDefault("terminal-record-rejected") + 1;
			}

			ReportProgress();

			void ReportProgress() => _progress?.Invoke(new BuildTimeCacheGenerationProgress(
				scenarioIndex + 1,
				selected.Length,
				entry.Identity.ToString()));
		}

		var document = TerminalLobbyCache.CreateDocument(records);
		var bytes = TerminalLobbyCache.Write(document);
		var read = TerminalLobbyCache.ReadDocument(bytes);
		if (read.Document is null)
		{
			throw new InvalidOperationException("Generated cache document failed semantic self-validation.");
		}

		var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
		var artifact = new BuildTimeCacheArtifactDiagnostics(
			ArtifactLogicalName,
			TerminalLobbyCache.SchemaIdentifier,
			TerminalLobbyCache.SchemaVersion,
			SimulatorProfile.Active.Identity.ProfileId,
			SimulatorProfile.Active.Identity.Version,
			records.Count,
			hash,
			bytes.Length);
		var diagnostics = new BuildTimeCacheGenerationDiagnostics(
			GeneratorIdentifier,
			GeneratorVersion,
			"completed",
			completeCatalog.Count,
			selected.Length,
			already,
			degenerate,
			probability,
			omitted.Values.Sum(),
			new SortedDictionary<string, int>(omitted, StringComparer.Ordinal),
			new SortedDictionary<string, int>(suspicions, StringComparer.Ordinal),
			incompleteSeeds
				.OrderBy(value => value.RunSeedMaterial.CompatibilityIdentity.ToString(), StringComparer.Ordinal)
				.ThenBy(value => value.BatchPhase == "screening" ? 0 : 1)
				.ThenBy(value => value.RunSeedMaterial.RunNumber)
				.ToArray(),
			artifact);
		return new BuildTimeCacheGenerationResult("completed", bytes, document, diagnostics);
	}

	public BuildTimeCacheGenerationResult GenerateToFile(
		string destinationPath,
		IEnumerable<TerminalLobbyGenerationScenario>? scenarios = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
		var fullPath = Path.GetFullPath(destinationPath);
		var directory = Path.GetDirectoryName(fullPath)
			?? throw new ArgumentException("Destination must have a parent directory.", nameof(destinationPath));
		Directory.CreateDirectory(directory);
		var selected = scenarios?.ToArray();
		var enumeratedCount = selected?.Length
			?? TerminalLobbyScenarioCatalog.EnumerateCurrentProfile().Count;
		var temporaryPath = Path.Combine(
			directory,
			$".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
		try
		{
			var result = Generate(selected, cancellationToken);
			cancellationToken.ThrowIfCancellationRequested();
			var diagnosticsBytes = BuildTimeCacheDiagnosticsJson.Write(result.Diagnostics);
			using var _ = JsonDocument.Parse(diagnosticsBytes);
			WriteDurable(temporaryPath, result.ArtifactBytes!);

			cancellationToken.ThrowIfCancellationRequested();
			File.Move(temporaryPath, fullPath, overwrite: true);
			return result;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return CreateTerminalResult("cancelled", enumeratedCount);
		}
		catch
		{
			return CreateTerminalResult("failed", enumeratedCount);
		}
		finally
		{
			if (File.Exists(temporaryPath))
			{
				File.Delete(temporaryPath);
			}
		}
	}

	public BuildTimeCacheGenerationResult GenerateToFiles(
		string destinationPath,
		string diagnosticsPath,
		IEnumerable<TerminalLobbyGenerationScenario>? scenarios = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
		ArgumentException.ThrowIfNullOrWhiteSpace(diagnosticsPath);
		var fullDestinationPath = Path.GetFullPath(destinationPath);
		var fullDiagnosticsPath = Path.GetFullPath(diagnosticsPath);
		var destinationDirectory = Path.GetDirectoryName(fullDestinationPath)!;
		var diagnosticsDirectory = Path.GetDirectoryName(fullDiagnosticsPath)!;
		Directory.CreateDirectory(destinationDirectory);
		Directory.CreateDirectory(diagnosticsDirectory);
		var selected = scenarios?.ToArray();
		var enumeratedCount = selected?.Length
			?? TerminalLobbyScenarioCatalog.EnumerateCurrentProfile().Count;
		var artifactTemporaryPath = Path.Combine(
			destinationDirectory,
			$".{Path.GetFileName(fullDestinationPath)}.{Guid.NewGuid():N}.tmp");
		var diagnosticsTemporaryPath = Path.Combine(
			diagnosticsDirectory,
			$".{Path.GetFileName(fullDiagnosticsPath)}.{Guid.NewGuid():N}.tmp");
		try
		{
			var result = Generate(selected, cancellationToken);
			var diagnosticsBytes = BuildTimeCacheDiagnosticsJson.Write(result.Diagnostics);
			using var _ = JsonDocument.Parse(diagnosticsBytes);
			WriteDurable(artifactTemporaryPath, result.ArtifactBytes!);
			WriteDurable(diagnosticsTemporaryPath, diagnosticsBytes);
			cancellationToken.ThrowIfCancellationRequested();
			File.Move(diagnosticsTemporaryPath, fullDiagnosticsPath, overwrite: true);
			File.Move(artifactTemporaryPath, fullDestinationPath, overwrite: true);
			return result;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			var result = CreateTerminalResult("cancelled", enumeratedCount);
			WriteTerminalDiagnostics(fullDiagnosticsPath, result.Diagnostics);
			return result;
		}
		catch
		{
			var result = CreateTerminalResult("failed", enumeratedCount);
			WriteTerminalDiagnostics(fullDiagnosticsPath, result.Diagnostics);
			return result;
		}
		finally
		{
			DeleteIfPresent(artifactTemporaryPath);
			DeleteIfPresent(diagnosticsTemporaryPath);
		}
	}

	private static void WriteTerminalDiagnostics(
		string path,
		BuildTimeCacheGenerationDiagnostics diagnostics)
	{
		var directory = Path.GetDirectoryName(path)!;
		var temporaryPath = Path.Combine(
			directory,
			$".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
		try
		{
			WriteDurable(temporaryPath, BuildTimeCacheDiagnosticsJson.Write(diagnostics));
			File.Move(temporaryPath, path, overwrite: true);
		}
		finally
		{
			DeleteIfPresent(temporaryPath);
		}
	}

	private static void WriteDurable(string path, ReadOnlySpan<byte> bytes)
	{
		using var stream = new FileStream(
			path,
			FileMode.CreateNew,
			FileAccess.Write,
			FileShare.None,
			bufferSize: 4096,
			FileOptions.WriteThrough);
		stream.Write(bytes);
		stream.Flush(flushToDisk: true);
	}

	private static void DeleteIfPresent(string path)
	{
		if (File.Exists(path))
		{
			File.Delete(path);
		}
	}

	private static BuildTimeCacheGenerationResult CreateTerminalResult(
		string statusCode,
		int enumeratedScenarioCount) => new(
		statusCode,
		ArtifactBytes: null,
		Document: null,
		new BuildTimeCacheGenerationDiagnostics(
			GeneratorIdentifier,
			GeneratorVersion,
			statusCode,
			TerminalLobbyScenarioCatalog.EnumerateCurrentProfile().Count,
			enumeratedScenarioCount,
			AlreadyDecidedCount: 0,
			DegenerateCount: 0,
			ProbabilityCount: 0,
			OmittedCount: 0,
			new SortedDictionary<string, int>(StringComparer.Ordinal),
			new SortedDictionary<string, int>(StringComparer.Ordinal),
			[],
			Artifact: null));
}
