using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Models.Simulation;

namespace Werewolves.Core.Tests.Unit;

internal static class BuildTimeCacheTestFixtures
{
	private static readonly Lazy<BuildTimeCacheGenerationResult> CompleteProductionResult =
		new(CreateCompleteProductionResult);
	private static readonly Lazy<BuildTimeCacheGenerationResult> IncompleteEvidenceResult =
		new(CreateIncompleteEvidenceResult);

	public static BuildTimeCacheGenerationResult Complete => CompleteProductionResult.Value;

	public static BuildTimeCacheGenerationResult WithIncompleteEvidence =>
		IncompleteEvidenceResult.Value;

	private static BuildTimeCacheGenerationResult CreateCompleteProductionResult()
	{
		var bytes = File.ReadAllBytes(ProductionArtifactPath());
		var document = TerminalLobbyCache.ReadDocument(bytes).Document
			?? throw new InvalidOperationException("The packaged cache fixture is invalid.");
		return CreateResult(
			document,
			bytes,
			omissions: new Dictionary<BuildTimeCacheOmissionCode, int>(),
			suspicions: new Dictionary<BuildTimeCacheSuspicionCode, int>(),
			incompleteSeeds: []);
	}

	private static BuildTimeCacheGenerationResult CreateIncompleteEvidenceResult()
	{
		var complete = Complete;
		var incompleteEntry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(entry => !entry.IsAlreadyDecided);
		var document = TerminalLobbyCache.CreateDocument(complete.Document!.Records.Where(record =>
			!record.CompatibilityIdentity.Equals(incompleteEntry.Identity)));
		var bytes = TerminalLobbyCache.Write(document);
		var incompleteSeed = new BuildTimeIncompleteRunDiagnostic(
			BuildTimeBatchPhase.Screening,
			new RunSeedMaterial(
				incompleteEntry.Identity,
				BaselineRandomDecisionStrategy.Identity,
				runNumber: 0));
		return CreateResult(
			document,
			bytes,
			omissions: new Dictionary<BuildTimeCacheOmissionCode, int>
			{
				[BuildTimeCacheOmissionCode.ScreeningIncomplete] = 1
			},
			suspicions: new Dictionary<BuildTimeCacheSuspicionCode, int>
			{
				[BuildTimeCacheSuspicionCode.IncompleteRun] = 1
			},
			incompleteSeeds: [incompleteSeed]);
	}

	private static BuildTimeCacheGenerationResult CreateResult(
		TerminalLobbyCacheDocument document,
		byte[] bytes,
		IReadOnlyDictionary<BuildTimeCacheOmissionCode, int> omissions,
		IReadOnlyDictionary<BuildTimeCacheSuspicionCode, int> suspicions,
		IReadOnlyList<BuildTimeIncompleteRunDiagnostic> incompleteSeeds)
	{
		var records = document.Records;
		var artifact = new BuildTimeCacheArtifactDiagnostics(
			BuildTimeTerminalLobbyCacheGenerator.ArtifactLogicalName,
			TerminalLobbyCache.SchemaIdentifier,
			TerminalLobbyCache.SchemaVersion,
			SimulatorProfile.Active.Identity.ProfileId,
			SimulatorProfile.Active.Identity.Version,
			records.Count,
			Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
			bytes.Length);
		var diagnostics = new BuildTimeCacheGenerationDiagnostics(
			BuildTimeTerminalLobbyCacheGenerator.GeneratorIdentifier,
			BuildTimeTerminalLobbyCacheGenerator.GeneratorVersion,
			BuildTimeCacheGenerationStatus.Completed,
			TotalScenarioCount: 1_664,
			EnumeratedScenarioCount: 1_664,
			AlreadyDecidedCount: records.OfType<AlreadyDecidedTerminalCacheRecord>().Count(),
			DegenerateCount: records.OfType<DegenerateTerminalCacheRecord>().Count(),
			ProbabilityCount: records.OfType<ProbabilityTerminalCacheRecord>().Count(),
			OmittedCount: omissions.Values.Sum(),
			omissions,
			suspicions,
			incompleteSeeds,
			artifact);
		return new BuildTimeCacheGenerationResult(
			BuildTimeCacheGenerationStatus.Completed,
			bytes,
			document,
			diagnostics);
	}

	private static string ProductionArtifactPath(
		[CallerFilePath] string sourcePath = "") => Path.GetFullPath(Path.Combine(
		Path.GetDirectoryName(sourcePath)!,
		"..",
		"..",
		"..",
		"Werewolves.Client",
		"Resources",
		"Raw",
		BuildTimeTerminalLobbyCacheGenerator.ArtifactLogicalName));
}
