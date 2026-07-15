using System.Security.Cryptography;
using System.Text;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.GameLogic.Simulation;

public enum BuildTimeCacheGenerationStatus
{
	Completed,
	Cancelled,
	Failed
}

public enum BuildTimeBatchPhase
{
	Screening,
	Probability
}

public enum BuildTimeCacheOmissionCode
{
	ScreeningIncomplete,
	ProbabilityIncomplete,
	CouldNotEvaluate,
	TerminalRecordRejected
}

public enum BuildTimeCacheSuspicionCode
{
	IncompleteRun,
	TerminalKindMismatch
}

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
	BuildTimeBatchPhase BatchPhase,
	RunSeedMaterial RunSeedMaterial);

public sealed record BuildTimeCacheGenerationDiagnostics(
	string GeneratorIdentifier,
	string GeneratorVersion,
	BuildTimeCacheGenerationStatus Status,
	int TotalScenarioCount,
	int EnumeratedScenarioCount,
	int AlreadyDecidedCount,
	int DegenerateCount,
	int ProbabilityCount,
	int OmittedCount,
	IReadOnlyDictionary<BuildTimeCacheOmissionCode, int> OmissionsByCode,
	IReadOnlyDictionary<BuildTimeCacheSuspicionCode, int> SuspicionsByCode,
	IReadOnlyList<BuildTimeIncompleteRunDiagnostic> IncompleteRunSeedMaterial,
	BuildTimeCacheArtifactDiagnostics? Artifact);

public sealed record BuildTimeCacheGenerationResult(
	BuildTimeCacheGenerationStatus Status,
	byte[]? ArtifactBytes,
	TerminalLobbyCacheDocument? Document,
	BuildTimeCacheGenerationDiagnostics Diagnostics);

public sealed record BuildTimeCacheGenerationProgress(
	int CompletedScenarioCount,
	int TotalScenarioCount,
	string CanonicalIdentity);

internal enum BuildTimeCachePublicationBoundary
{
	ArtifactStaged,
	DiagnosticsStaged,
	BeforeCommitDecision,
	DiagnosticsCommitted,
	BeforeArtifactCommit,
	ArtifactCommitted,
	CleanupCompleted
}

internal interface IBuildTimeCachePublicationFileSystem
{
	void CreateDirectory(string path);

	void WriteDurable(string path, byte[] bytes);

	void MoveReplace(string sourcePath, string destinationPath);

	bool FileExists(string path);

	void DeleteFile(string path);

	void ReachBoundary(BuildTimeCachePublicationBoundary boundary);
}

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
	private readonly IBuildTimeCachePublicationFileSystem _fileSystem;
	private readonly Func<BuildTimeCacheGenerationDiagnostics, byte[]?, byte[]> _diagnosticsSerializer;

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
		_fileSystem = PhysicalBuildTimeCachePublicationFileSystem.Instance;
		_diagnosticsSerializer = BuildTimeCacheDiagnosticsJson.Write;
	}

	public BuildTimeTerminalLobbyCacheGenerator(
		Func<SimulationScenario, SimulationCompatibilityIdentity, int, CancellationToken,
			SimulationBatchSourceEvidence> executeBatch,
		Action<BuildTimeCacheGenerationProgress>? progress = null)
		: this(
			executeBatch,
			progress,
			PhysicalBuildTimeCachePublicationFileSystem.Instance,
			BuildTimeCacheDiagnosticsJson.Write)
	{
	}

	internal BuildTimeTerminalLobbyCacheGenerator(
		Func<SimulationScenario, SimulationCompatibilityIdentity, int, CancellationToken,
			SimulationBatchSourceEvidence> executeBatch,
		Action<BuildTimeCacheGenerationProgress>? progress,
		IBuildTimeCachePublicationFileSystem fileSystem,
		Func<BuildTimeCacheGenerationDiagnostics, byte[]?, byte[]> diagnosticsSerializer)
	{
		_executeBatch = executeBatch ?? throw new ArgumentNullException(nameof(executeBatch));
		_progress = progress;
		_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
		_diagnosticsSerializer = diagnosticsSerializer
			?? throw new ArgumentNullException(nameof(diagnosticsSerializer));
	}

	public BuildTimeCacheGenerationResult Generate(
		IEnumerable<TerminalLobbyGenerationScenario>? scenarios = null,
		CancellationToken cancellationToken = default)
	{
		var selected = SelectScenarios(scenarios);
		var observation = new GenerationObservation(selected.Length);
		return Generate(selected, observation, cancellationToken);
	}

	public BuildTimeCacheGenerationResult GenerateToFile(
		string destinationPath,
		CancellationToken cancellationToken = default) => GenerateToFile(
			destinationPath,
			TerminalLobbyScenarioCatalog.EnumerateCurrentProfile(),
			cancellationToken);

	internal BuildTimeCacheGenerationResult GenerateToFile(
		string destinationPath,
		IEnumerable<TerminalLobbyGenerationScenario> scenarios,
		CancellationToken cancellationToken = default)
	{
		var fullDestinationPath = NormalizePath(destinationPath, nameof(destinationPath));
		return GenerateAndPublish(
			fullDestinationPath,
			fullDiagnosticsPath: null,
			scenarios,
			cancellationToken);
	}

	public BuildTimeCacheGenerationResult GenerateToFiles(
		string destinationPath,
		string diagnosticsPath,
		CancellationToken cancellationToken = default) => GenerateToFiles(
			destinationPath,
			diagnosticsPath,
			TerminalLobbyScenarioCatalog.EnumerateCurrentProfile(),
			cancellationToken);

	internal BuildTimeCacheGenerationResult GenerateToFiles(
		string destinationPath,
		string diagnosticsPath,
		IEnumerable<TerminalLobbyGenerationScenario> scenarios,
		CancellationToken cancellationToken = default)
	{
		var fullDestinationPath = NormalizePath(destinationPath, nameof(destinationPath));
		var fullDiagnosticsPath = NormalizePath(diagnosticsPath, nameof(diagnosticsPath));
		ValidateDistinctPublicationPaths(fullDestinationPath, fullDiagnosticsPath);

		return GenerateAndPublish(
			fullDestinationPath,
			fullDiagnosticsPath,
			scenarios,
			cancellationToken);
	}

	private static void ValidateDistinctPublicationPaths(
		string fullDestinationPath,
		string fullDiagnosticsPath)
	{
		var pathComparer = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
			? StringComparer.OrdinalIgnoreCase
			: StringComparer.Ordinal;
		if (pathComparer.Equals(
			PhysicalPathIdentity(fullDestinationPath),
			PhysicalPathIdentity(fullDiagnosticsPath)))
		{
			throw new ArgumentException(
				"The artifact and diagnostics destinations must be different files.",
				"diagnosticsPath");
		}
	}

	internal BuildTimeCacheGenerationResult PublishCompletedResult(
		string destinationPath,
		string? diagnosticsPath,
		BuildTimeCacheGenerationResult completedResult,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(completedResult);
		if (completedResult.Status != BuildTimeCacheGenerationStatus.Completed)
		{
			throw new ArgumentException(
				"Only a completed generation result can enter publication.",
				nameof(completedResult));
		}

		var fullDestinationPath = NormalizePath(destinationPath, nameof(destinationPath));
		var fullDiagnosticsPath = diagnosticsPath is null
			? null
			: NormalizePath(diagnosticsPath, nameof(diagnosticsPath));
		if (fullDiagnosticsPath is not null)
		{
			ValidateDistinctPublicationPaths(fullDestinationPath, fullDiagnosticsPath);
		}

		return Publish(
			fullDestinationPath,
			fullDiagnosticsPath,
			() => completedResult,
			status => CreateTerminalResult(completedResult, status),
			cancellationToken);
	}

	private BuildTimeCacheGenerationResult Generate(
		TerminalLobbyGenerationScenario[] selected,
		GenerationObservation observation,
		CancellationToken cancellationToken)
	{
		var records = new List<TerminalLobbyCacheRecord>();
		for (var scenarioIndex = 0; scenarioIndex < selected.Length; scenarioIndex++)
		{
			var entry = selected[scenarioIndex];
			cancellationToken.ThrowIfCancellationRequested();
			BuildTimeBatchPhase? lastIncompletePhase = null;
			var evaluator = new TerminalLobbyEvaluator((scenario, identity, count, token) =>
			{
				var phase = count switch
				{
					TerminalLobbyEvaluator.ScreeningAttemptCount => BuildTimeBatchPhase.Screening,
					TerminalLobbyEvaluator.ProbabilityAttemptCount => BuildTimeBatchPhase.Probability,
					_ => throw new InvalidOperationException("Unknown generator batch phase.")
				};
				var batch = _executeBatch(scenario, identity, count, token);
				observation.ObserveBatch(phase, batch);
				if (batch.IncompleteRunCount > 0)
				{
					lastIncompletePhase = phase;
				}

				return batch;
			});
			var evaluation = evaluator.Evaluate(entry.Scenario, cancellationToken);

			if (evaluation is CouldNotEvaluateLobbyEvaluation)
			{
				observation.Omit(lastIncompletePhase switch
				{
					BuildTimeBatchPhase.Screening => BuildTimeCacheOmissionCode.ScreeningIncomplete,
					BuildTimeBatchPhase.Probability => BuildTimeCacheOmissionCode.ProbabilityIncomplete,
					_ => BuildTimeCacheOmissionCode.CouldNotEvaluate
				});
				ReportProgress();
				continue;
			}

			if (evaluation is not TerminalLobbyEvaluation terminal)
			{
				observation.Omit(BuildTimeCacheOmissionCode.CouldNotEvaluate);
				ReportProgress();
				continue;
			}

			if (entry.IsAlreadyDecided != (terminal is AlreadyDecidedTerminalEvaluation))
			{
				observation.Suspect(BuildTimeCacheSuspicionCode.TerminalKindMismatch);
			}

			try
			{
				records.Add(TerminalLobbyCache.Capture(entry.Identity, terminal));
				observation.Record(terminal);
			}
			catch (ArgumentException)
			{
				observation.Omit(BuildTimeCacheOmissionCode.TerminalRecordRejected);
			}

			ReportProgress();

			void ReportProgress()
			{
				_progress?.Invoke(new BuildTimeCacheGenerationProgress(
					scenarioIndex + 1,
					selected.Length,
					entry.Identity.ToString()));
				cancellationToken.ThrowIfCancellationRequested();
			}
		}

		cancellationToken.ThrowIfCancellationRequested();
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
		var diagnostics = observation.CreateDiagnostics(
			BuildTimeCacheGenerationStatus.Completed,
			artifact);
		return new BuildTimeCacheGenerationResult(
			BuildTimeCacheGenerationStatus.Completed,
			bytes,
			document,
			diagnostics);
	}

	private BuildTimeCacheGenerationResult GenerateAndPublish(
		string fullDestinationPath,
		string? fullDiagnosticsPath,
		IEnumerable<TerminalLobbyGenerationScenario>? scenarios,
		CancellationToken cancellationToken)
	{
		var selected = SelectScenarios(scenarios);
		var observation = new GenerationObservation(selected.Length);
		return Publish(
			fullDestinationPath,
			fullDiagnosticsPath,
			() => Generate(selected, observation, cancellationToken),
			observation.CreateTerminalResult,
			cancellationToken);
	}

	private BuildTimeCacheGenerationResult Publish(
		string fullDestinationPath,
		string? fullDiagnosticsPath,
		Func<BuildTimeCacheGenerationResult> createCompletedResult,
		Func<BuildTimeCacheGenerationStatus, BuildTimeCacheGenerationResult> createTerminalResult,
		CancellationToken cancellationToken)
	{
		var destinationDirectory = ParentDirectory(fullDestinationPath, nameof(fullDestinationPath));
		var diagnosticsDirectory = fullDiagnosticsPath is null
			? null
			: ParentDirectory(fullDiagnosticsPath, nameof(fullDiagnosticsPath));
		var artifactTemporaryPath = TemporaryPath(destinationDirectory, fullDestinationPath);
		var diagnosticsTemporaryPath = fullDiagnosticsPath is null
			? null
			: TemporaryPath(diagnosticsDirectory!, fullDiagnosticsPath);
		var diagnosticsCommitted = false;
		var artifactCommitted = false;
		BuildTimeCacheGenerationResult? completedResult = null;
		using var commitDecision = new PublicationCommitDecision(cancellationToken);
		try
		{
			completedResult = createCompletedResult();
			var diagnosticsBytes = SerializeAndValidateDiagnostics(
				completedResult.Diagnostics,
				completedResult.ArtifactBytes);
			cancellationToken.ThrowIfCancellationRequested();

			_fileSystem.CreateDirectory(destinationDirectory);
			if (diagnosticsDirectory is not null)
			{
				_fileSystem.CreateDirectory(diagnosticsDirectory);
			}

			cancellationToken.ThrowIfCancellationRequested();
			_fileSystem.WriteDurable(artifactTemporaryPath, completedResult.ArtifactBytes!);
			_fileSystem.ReachBoundary(BuildTimeCachePublicationBoundary.ArtifactStaged);
			cancellationToken.ThrowIfCancellationRequested();

			if (diagnosticsTemporaryPath is not null)
			{
				_fileSystem.WriteDurable(diagnosticsTemporaryPath, diagnosticsBytes);
				_fileSystem.ReachBoundary(BuildTimeCachePublicationBoundary.DiagnosticsStaged);
				cancellationToken.ThrowIfCancellationRequested();
			}

			_fileSystem.ReachBoundary(BuildTimeCachePublicationBoundary.BeforeCommitDecision);
			commitDecision.CommitOrThrow();

			if (diagnosticsTemporaryPath is not null)
			{
				_fileSystem.MoveReplace(diagnosticsTemporaryPath, fullDiagnosticsPath!);
				diagnosticsCommitted = true;
				_fileSystem.ReachBoundary(BuildTimeCachePublicationBoundary.DiagnosticsCommitted);
			}

			_fileSystem.ReachBoundary(BuildTimeCachePublicationBoundary.BeforeArtifactCommit);
			_fileSystem.MoveReplace(artifactTemporaryPath, fullDestinationPath);
			artifactCommitted = true;
			_fileSystem.ReachBoundary(BuildTimeCachePublicationBoundary.ArtifactCommitted);
			return completedResult;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			if (artifactCommitted)
			{
				return completedResult!;
			}

			RollbackCompletedDiagnostics(fullDiagnosticsPath, diagnosticsCommitted);
			var result = createTerminalResult(BuildTimeCacheGenerationStatus.Cancelled);
			PublishTerminalDiagnostics(fullDiagnosticsPath, result.Diagnostics);
			return result;
		}
		catch
		{
			if (artifactCommitted)
			{
				return completedResult!;
			}

			RollbackCompletedDiagnostics(fullDiagnosticsPath, diagnosticsCommitted);
			var result = createTerminalResult(BuildTimeCacheGenerationStatus.Failed);
			PublishTerminalDiagnostics(fullDiagnosticsPath, result.Diagnostics);
			return result;
		}
		finally
		{
			TryDelete(artifactTemporaryPath);
			if (diagnosticsTemporaryPath is not null)
			{
				TryDelete(diagnosticsTemporaryPath);
			}

			try
			{
				_fileSystem.ReachBoundary(BuildTimeCachePublicationBoundary.CleanupCompleted);
			}
			catch
			{
				// Publication is already terminal; cleanup instrumentation cannot change it.
			}
		}
	}

	private static BuildTimeCacheGenerationResult CreateTerminalResult(
		BuildTimeCacheGenerationResult completedResult,
		BuildTimeCacheGenerationStatus status) => new(
		status,
		ArtifactBytes: null,
		Document: null,
		completedResult.Diagnostics with
		{
			Status = status,
			Artifact = null
		});

	private byte[] SerializeAndValidateDiagnostics(
		BuildTimeCacheGenerationDiagnostics diagnostics,
		byte[]? artifactBytes)
	{
		var diagnosticsBytes = _diagnosticsSerializer(diagnostics, artifactBytes);
		var read = BuildTimeCacheDiagnosticsJson.Read(diagnosticsBytes, artifactBytes);
		if (read.Diagnostics is null)
		{
			throw new InvalidOperationException(
				$"Generated diagnostics failed semantic validation: {read.Rejection}");
		}

		return diagnosticsBytes;
	}

	private void PublishTerminalDiagnostics(
		string? fullDiagnosticsPath,
		BuildTimeCacheGenerationDiagnostics diagnostics)
	{
		if (fullDiagnosticsPath is null)
		{
			return;
		}

		var directory = ParentDirectory(fullDiagnosticsPath, nameof(fullDiagnosticsPath));
		var temporaryPath = TemporaryPath(directory, fullDiagnosticsPath);
		try
		{
			_fileSystem.CreateDirectory(directory);
			_fileSystem.WriteDurable(
				temporaryPath,
				SerializeAndValidateDiagnostics(diagnostics, artifactBytes: null));
			_fileSystem.MoveReplace(temporaryPath, fullDiagnosticsPath);
		}
		finally
		{
			TryDelete(temporaryPath);
		}
	}

	private void RollbackCompletedDiagnostics(string? path, bool diagnosticsCommitted)
	{
		if (diagnosticsCommitted && path is not null)
		{
			TryDelete(path);
		}
	}

	private void TryDelete(string path)
	{
		try
		{
			if (_fileSystem.FileExists(path))
			{
				_fileSystem.DeleteFile(path);
			}
		}
		catch
		{
			// Best-effort cleanup must not reverse an already decided publication outcome.
		}
	}

	private static TerminalLobbyGenerationScenario[] SelectScenarios(
		IEnumerable<TerminalLobbyGenerationScenario>? scenarios) =>
		(scenarios ?? TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()).ToArray();

	private static string NormalizePath(string path, string parameterName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path, parameterName);
		return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
	}

	internal static string ResolvePhysicalPath(string fullPath) =>
		ResolvePhysicalPath(fullPath, resolutionDepth: 0);

	private static string PhysicalPathIdentity(string fullPath)
	{
		var resolved = ResolvePhysicalPath(fullPath);
		return OperatingSystem.IsMacOS()
			? resolved.Normalize(NormalizationForm.FormC)
			: resolved;
	}

	private static string ResolvePhysicalPath(string fullPath, int resolutionDepth)
	{
		if (resolutionDepth > 40)
		{
			throw new IOException("Too many symbolic-link resolutions in destination path.");
		}

		var root = Path.GetPathRoot(fullPath)
			?? throw new ArgumentException("Destination must have a rooted path.", nameof(fullPath));
		var current = root;
		var relative = fullPath[root.Length..];
		foreach (var component in relative.Split(
			Path.DirectorySeparatorChar,
			Path.AltDirectorySeparatorChar,
			StringSplitOptions.RemoveEmptyEntries))
		{
			var candidate = Path.Combine(current, component);
			FileSystemInfo information = Directory.Exists(candidate)
				? new DirectoryInfo(candidate)
				: new FileInfo(candidate);
			if (information.LinkTarget is null)
			{
				current = candidate;
				continue;
			}

			var resolved = information.ResolveLinkTarget(returnFinalTarget: true);
			if (resolved is not null)
			{
				current = ResolvePhysicalPath(resolved.FullName, resolutionDepth + 1);
				continue;
			}

			current = ResolvePhysicalPath(Path.GetFullPath(
				Path.IsPathRooted(information.LinkTarget)
					? information.LinkTarget
					: Path.Combine(Path.GetDirectoryName(candidate)!, information.LinkTarget)),
				resolutionDepth + 1);
		}

		return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
	}

	private static string ParentDirectory(string path, string parameterName) =>
		Path.GetDirectoryName(path)
		?? throw new ArgumentException("Destination must have a parent directory.", parameterName);

	private static string TemporaryPath(string directory, string destinationPath) => Path.Combine(
		directory,
		$".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

	private sealed class GenerationObservation
	{
		private readonly Dictionary<BuildTimeCacheOmissionCode, int> _omissions = [];
		private readonly Dictionary<BuildTimeCacheSuspicionCode, int> _suspicions = [];
		private readonly List<BuildTimeIncompleteRunDiagnostic> _incompleteSeeds = [];

		public int EnumeratedScenarioCount { get; }

		public int AlreadyDecidedCount { get; private set; }

		public int DegenerateCount { get; private set; }

		public int ProbabilityCount { get; private set; }

		public GenerationObservation(int enumeratedScenarioCount) =>
			EnumeratedScenarioCount = enumeratedScenarioCount;

		public void ObserveBatch(
			BuildTimeBatchPhase phase,
			SimulationBatchSourceEvidence evidence)
		{
			foreach (var run in evidence.Records.OfType<IncompleteSimulationRun>())
			{
				_incompleteSeeds.Add(new BuildTimeIncompleteRunDiagnostic(
					phase,
					run.RunSeedMaterial));
				Suspect(BuildTimeCacheSuspicionCode.IncompleteRun);
			}
		}

		public void Record(TerminalLobbyEvaluation terminal)
		{
			switch (terminal)
			{
				case AlreadyDecidedTerminalEvaluation: AlreadyDecidedCount++; break;
				case DegenerateTerminalEvaluation: DegenerateCount++; break;
				case ProbabilityTerminalEvaluation: ProbabilityCount++; break;
			}
		}

		public void Omit(BuildTimeCacheOmissionCode code) =>
			_omissions[code] = _omissions.GetValueOrDefault(code) + 1;

		public void Suspect(BuildTimeCacheSuspicionCode code) =>
			_suspicions[code] = _suspicions.GetValueOrDefault(code) + 1;

		public BuildTimeCacheGenerationDiagnostics CreateDiagnostics(
			BuildTimeCacheGenerationStatus status,
			BuildTimeCacheArtifactDiagnostics? artifact) => new(
			GeneratorIdentifier,
			GeneratorVersion,
			status,
			TerminalLobbyScenarioCatalog.EnumerateCurrentProfile().Count,
			EnumeratedScenarioCount,
			AlreadyDecidedCount,
			DegenerateCount,
			ProbabilityCount,
			_omissions.Values.Sum(),
			new Dictionary<BuildTimeCacheOmissionCode, int>(_omissions),
			new Dictionary<BuildTimeCacheSuspicionCode, int>(_suspicions),
			_incompleteSeeds
				.OrderBy(value => value.RunSeedMaterial.CompatibilityIdentity.ToString(), StringComparer.Ordinal)
				.ThenBy(value => value.BatchPhase)
				.ThenBy(value => value.RunSeedMaterial.RunNumber)
				.ToArray(),
			artifact);

		public BuildTimeCacheGenerationResult CreateTerminalResult(
			BuildTimeCacheGenerationStatus status) => new(
			status,
			ArtifactBytes: null,
			Document: null,
			CreateDiagnostics(status, artifact: null));
	}

	private sealed class PublicationCommitDecision : IDisposable
	{
		private readonly object _gate = new();
		private readonly CancellationToken _cancellationToken;
		private readonly CancellationTokenRegistration _registration;
		private bool _cancellationWon;
		private bool _commitWon;

		public PublicationCommitDecision(CancellationToken cancellationToken)
		{
			_cancellationToken = cancellationToken;
			_registration = cancellationToken.Register(() =>
			{
				lock (_gate)
				{
					if (!_commitWon)
					{
						_cancellationWon = true;
					}
				}
			});
		}

		public void CommitOrThrow()
		{
			lock (_gate)
			{
				if (_cancellationWon || _cancellationToken.IsCancellationRequested)
				{
					throw new OperationCanceledException(_cancellationToken);
				}

				_commitWon = true;
			}
		}

		public void Dispose() => _registration.Dispose();
	}

	private sealed class PhysicalBuildTimeCachePublicationFileSystem
		: IBuildTimeCachePublicationFileSystem
	{
		public static PhysicalBuildTimeCachePublicationFileSystem Instance { get; } = new();

		public void CreateDirectory(string path) => Directory.CreateDirectory(path);

		public void WriteDurable(string path, byte[] bytes)
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

		public void MoveReplace(string sourcePath, string destinationPath) =>
			File.Move(sourcePath, destinationPath, overwrite: true);

		public bool FileExists(string path) => File.Exists(path);

		public void DeleteFile(string path) => File.Delete(path);

		public void ReachBoundary(BuildTimeCachePublicationBoundary boundary)
		{
		}
	}
}
