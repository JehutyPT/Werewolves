using FluentAssertions;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public sealed class BuildTimeTerminalLobbyCacheGeneratorTests
{
	[Fact]
	public void PackagedProductionArtifact_HasReviewedCanonicalIdentityAndTerminalInventory()
	{
		var bytes = File.ReadAllBytes(ProductionArtifactPath());

		bytes.Should().HaveCount(2_337_001);
		Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant().Should().Be(
			"95797d40dfb3ac0b389c6f004956cdf19faeefb77c1f32f35d8071405d9a9253");
		var read = TerminalLobbyCache.ReadDocument(bytes);
		read.Rejection.Should().BeNull();
		read.Document!.Records.Should().HaveCount(1_664);
		read.Document.Records.OfType<AlreadyDecidedTerminalCacheRecord>().Should().HaveCount(832);
		read.Document.Records.OfType<DegenerateTerminalCacheRecord>().Should().HaveCount(52);
		read.Document.Records.OfType<ProbabilityTerminalCacheRecord>().Should().HaveCount(780);
	}

	[Fact]
	public void GenerateToFile_WithCompleteResult_AtomicallyPublishesCanonicalBytes()
	{
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => value.IsAlreadyDecided);
		var generator = new BuildTimeTerminalLobbyCacheGenerator(
			(_, _, _, _) => throw new InvalidOperationException());
		var directory = Directory.CreateTempSubdirectory("werewolves-cache-generator-");
		var path = Path.Combine(directory.FullName, "terminal-lobby-cache.json");
		File.WriteAllText(path, "previous");
		try
		{
			var result = generator.GenerateToFile(path, [entry]);

			result.StatusCode.Should().Be("completed");
			File.ReadAllBytes(path).Should().Equal(result.ArtifactBytes!);
			Directory.EnumerateFiles(directory.FullName, "*.tmp").Should().BeEmpty();
		}
		finally
		{
			directory.Delete(recursive: true);
		}
	}

	[Fact]
	public void GenerateToFiles_WhenCancelled_PreservesPreviousArtifactAndReturnsCancelledDiagnostics()
	{
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => !value.IsAlreadyDecided);
		using var cancellation = new CancellationTokenSource();
		var generator = new BuildTimeTerminalLobbyCacheGenerator((_, _, _, _) =>
		{
			cancellation.Cancel();
			throw new OperationCanceledException(cancellation.Token);
		});
		var directory = Directory.CreateTempSubdirectory("werewolves-cache-generator-");
		var path = Path.Combine(directory.FullName, "terminal-lobby-cache.json");
		var diagnosticsPath = Path.Combine(directory.FullName, "diagnostics.json");
		File.WriteAllText(path, "previous");
		try
		{
			var result = generator.GenerateToFiles(
				path,
				diagnosticsPath,
				[entry],
				cancellation.Token);

			result.StatusCode.Should().Be("cancelled");
			result.ArtifactBytes.Should().BeNull();
			result.Diagnostics.StatusCode.Should().Be("cancelled");
			File.ReadAllText(path).Should().Be("previous");
			using (var diagnostics = JsonDocument.Parse(File.ReadAllBytes(diagnosticsPath)))
			{
				diagnostics.RootElement.GetProperty("status").GetString().Should().Be("cancelled");
				diagnostics.RootElement.GetProperty("cache").GetProperty("schema").GetString()
					.Should().Be(TerminalLobbyCache.SchemaIdentifier);
				diagnostics.RootElement.GetProperty("cache").GetProperty("version").GetInt32()
					.Should().Be(TerminalLobbyCache.SchemaVersion);
			}
			Directory.EnumerateFiles(directory.FullName, "*.tmp").Should().BeEmpty();
		}
		finally
		{
			directory.Delete(recursive: true);
		}
	}

	[Fact]
	public void GenerateToFiles_WhenDocumentConstructionFails_PreservesPreviousArtifactAndReturnsFailedDiagnostics()
	{
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => value.IsAlreadyDecided);
		var generator = new BuildTimeTerminalLobbyCacheGenerator(
			(_, _, _, _) => throw new InvalidOperationException());
		var directory = Directory.CreateTempSubdirectory("werewolves-cache-generator-");
		var path = Path.Combine(directory.FullName, "terminal-lobby-cache.json");
		var diagnosticsPath = Path.Combine(directory.FullName, "diagnostics.json");
		File.WriteAllText(path, "previous");
		try
		{
			var result = generator.GenerateToFiles(path, diagnosticsPath, [entry, entry]);

			result.StatusCode.Should().Be("failed");
			result.ArtifactBytes.Should().BeNull();
			result.Diagnostics.StatusCode.Should().Be("failed");
			File.ReadAllText(path).Should().Be("previous");
			using (var diagnostics = JsonDocument.Parse(File.ReadAllBytes(diagnosticsPath)))
			{
				diagnostics.RootElement.GetProperty("status").GetString().Should().Be("failed");
				diagnostics.RootElement.GetProperty("cache").GetProperty("schema").GetString()
					.Should().Be(TerminalLobbyCache.SchemaIdentifier);
				diagnostics.RootElement.GetProperty("cache").GetProperty("version").GetInt32()
					.Should().Be(TerminalLobbyCache.SchemaVersion);
			}
			Directory.EnumerateFiles(directory.FullName, "*.tmp").Should().BeEmpty();
		}
		finally
		{
			directory.Delete(recursive: true);
		}
	}

	[Fact]
	public void Generate_WhenProbabilityEvaluationCompletes_InvokesScreeningThenProbabilityOnce()
	{
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => !value.IsAlreadyDecided);
		var attemptCounts = new List<int>();
		var generator = new BuildTimeTerminalLobbyCacheGenerator(
			(scenario, identity, count, _) =>
			{
				attemptCounts.Add(count);
				return Batch(scenario, identity, count, endingTurn: 2);
			});

		var result = generator.Generate([entry]);

		attemptCounts.Should().Equal(
			TerminalLobbyEvaluator.ScreeningAttemptCount,
			TerminalLobbyEvaluator.ProbabilityAttemptCount);
		result.Diagnostics.ProbabilityCount.Should().Be(1);
		result.Diagnostics.DegenerateCount.Should().Be(0);
		result.Diagnostics.OmittedCount.Should().Be(0);
		result.Document!.Records.Should().ContainSingle()
			.Which.Should().BeOfType<ProbabilityTerminalCacheRecord>();
	}

	[Fact]
	public void Generate_WhenBatchExecutionThrows_OmitsScenarioAsCouldNotEvaluate()
	{
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => !value.IsAlreadyDecided);
		var callCount = 0;
		var generator = new BuildTimeTerminalLobbyCacheGenerator((_, _, _, _) =>
		{
			callCount++;
			throw new InvalidOperationException("controlled execution failure");
		});

		var result = generator.Generate([entry]);

		callCount.Should().Be(1);
		result.StatusCode.Should().Be("completed");
		result.Document!.Records.Should().BeEmpty();
		result.Diagnostics.OmissionsByCode.Should().ContainSingle()
			.Which.Should().Be(new KeyValuePair<string, int>("could-not-evaluate", 1));
		result.Diagnostics.IncompleteRunSeedMaterial.Should().BeEmpty();
	}

	[Fact]
	public void Generate_WhenTerminalRecordIdentityIsRejected_OmitsScenarioWithStableCode()
	{
		var entries = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.Where(value => !value.IsAlreadyDecided)
			.Take(2)
			.ToArray();
		var mismatched = entries[0] with { Identity = entries[1].Identity };
		var generator = new BuildTimeTerminalLobbyCacheGenerator(
			(scenario, identity, count, _) => Batch(scenario, identity, count));

		var result = generator.Generate([mismatched]);

		result.Document!.Records.Should().BeEmpty();
		result.Diagnostics.DegenerateCount.Should().Be(0);
		result.Diagnostics.OmissionsByCode.Should().ContainSingle()
			.Which.Should().Be(new KeyValuePair<string, int>("terminal-record-rejected", 1));
	}

	[Fact]
	public void Generate_WithMultipleIncompleteRuns_OrdersReplayMaterialByIdentityThenRunNumber()
	{
		var entries = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.Where(value => !value.IsAlreadyDecided)
			.Take(2)
			.OrderByDescending(value => value.Identity.ToString(), StringComparer.Ordinal)
			.ToArray();
		var generator = new BuildTimeTerminalLobbyCacheGenerator(
			(scenario, identity, count, _) => Batch(
				scenario,
				identity,
				count,
				incompleteRunNumbers: [2, 10]));

		var result = generator.Generate(entries);

		var expected = entries
			.OrderBy(value => value.Identity.ToString(), StringComparer.Ordinal)
			.SelectMany(value => new[] { 2, 10 }
				.Select(run => $"{value.Identity}|screening|{run}"));
		result.Diagnostics.IncompleteRunSeedMaterial
			.Select(value => $"{value.RunSeedMaterial.CompatibilityIdentity}|{value.BatchPhase}|{value.RunSeedMaterial.RunNumber}")
			.Should().Equal(expected);
		result.Diagnostics.OmissionsByCode.Should().Contain("screening-incomplete", 2);
		result.Diagnostics.SuspicionsByCode.Should().Contain("incomplete-run", 4);
	}

	[Fact]
	public void Generate_WithShortIncompleteScreeningBatch_UsesRequestedPhaseForDiagnostics()
	{
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => !value.IsAlreadyDecided);
		var generator = new BuildTimeTerminalLobbyCacheGenerator(
			(scenario, identity, count, _) => Batch(
				scenario,
				identity,
				count - 1,
				incompleteLastRun: true));

		var result = generator.Generate([entry]);

		result.Diagnostics.OmissionsByCode.Should().ContainSingle()
			.Which.Should().Be(new KeyValuePair<string, int>("screening-incomplete", 1));
		result.Diagnostics.IncompleteRunSeedMaterial.Should().ContainSingle()
			.Which.BatchPhase.Should().Be("screening");
	}

	[Fact]
	public void Generate_WithCompleteTerminalResults_ProducesCanonicalArtifactAndDiagnostics()
	{
		var catalog = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile();
		var scenarios = new[]
		{
			catalog.First(entry => entry.IsAlreadyDecided),
			catalog.First(entry => !entry.IsAlreadyDecided)
		};
		var generator = new BuildTimeTerminalLobbyCacheGenerator(
			(scenario, identity, count, _) => Batch(scenario, identity, count));

		var result = generator.Generate(scenarios);
		var repeated = generator.Generate(scenarios);

		result.StatusCode.Should().Be("completed");
		result.ArtifactBytes.Should().NotBeNull();
		var read = TerminalLobbyCache.ReadDocument(result.ArtifactBytes!);
		read.Rejection.Should().BeNull();
		read.Document!.Records.Should().HaveCount(2);
		result.Diagnostics.TotalScenarioCount.Should().Be(1_664);
		result.Diagnostics.EnumeratedScenarioCount.Should().Be(2);
		result.Diagnostics.AlreadyDecidedCount.Should().Be(1);
		result.Diagnostics.DegenerateCount.Should().Be(1);
		result.Diagnostics.ProbabilityCount.Should().Be(0);
		result.Diagnostics.OmittedCount.Should().Be(0);
		result.Diagnostics.Artifact!.ByteLength.Should().Be(result.ArtifactBytes!.Length);
		result.Diagnostics.Artifact.Sha256.Should().MatchRegex("^[0-9a-f]{64}$");
		repeated.ArtifactBytes.Should().Equal(result.ArtifactBytes!);
		BuildTimeCacheDiagnosticsJson.Write(repeated.Diagnostics)
			.Should().Equal(BuildTimeCacheDiagnosticsJson.Write(result.Diagnostics));
	}

	[Fact]
	public void DiagnosticsJson_Write_IsDeterministicMachineReadableAndSeparateFromArtifact()
	{
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => value.IsAlreadyDecided);
		var result = new BuildTimeTerminalLobbyCacheGenerator(
			(_, _, _, _) => throw new InvalidOperationException())
			.Generate([entry]);

		var first = BuildTimeCacheDiagnosticsJson.Write(result.Diagnostics);
		var second = BuildTimeCacheDiagnosticsJson.Write(result.Diagnostics);

		first.Should().Equal(second);
		using var json = JsonDocument.Parse(first);
		json.RootElement.GetProperty("status").GetString().Should().Be("completed");
		json.RootElement.GetProperty("generator").GetProperty("id").GetString()
			.Should().Be("terminal-lobby-cache-generator");
		json.RootElement.GetProperty("artifact").GetProperty("sha256").GetString()
			.Should().Be(result.Diagnostics.Artifact!.Sha256);
		Encoding.UTF8.GetString(result.ArtifactBytes!).Should().NotContain("generator");
		Encoding.UTF8.GetString(result.ArtifactBytes!).Should().NotContain("runSeed");
	}

	[Theory]
	[InlineData(false, "screening-incomplete", 1_000)]
	[InlineData(true, "probability-incomplete", 10_000)]
	public void Generate_WithIncompleteRequiredBatch_OmitsRecordAndReportsReplayMaterial(
		bool completeScreening,
		string expectedCode,
		int expectedIncompleteRunNumber)
	{
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => !value.IsAlreadyDecided);
		var generator = new BuildTimeTerminalLobbyCacheGenerator(
			(scenario, identity, count, _) => Batch(
				scenario,
				identity,
				count,
				endingTurn: completeScreening && count == 1_000 ? 2 : 1,
				incompleteLastRun: count == (completeScreening ? 10_000 : 1_000)));

		var result = generator.Generate([entry]);

		result.Document!.Records.Should().BeEmpty();
		result.Diagnostics.OmittedCount.Should().Be(1);
		result.Diagnostics.OmissionsByCode.Should().ContainSingle()
			.Which.Should().Be(new KeyValuePair<string, int>(expectedCode, 1));
		result.Diagnostics.SuspicionsByCode.Should().Contain(
			"incomplete-run", 1);
		result.Diagnostics.IncompleteRunSeedMaterial.Should().ContainSingle()
			.Which.Should().Match<BuildTimeIncompleteRunDiagnostic>(value =>
				value.BatchPhase == (completeScreening ? "probability" : "screening")
				&& value.RunSeedMaterial.RunNumber == expectedIncompleteRunNumber - 1);
	}

	private static SimulationBatchSourceEvidence Batch(
		SimulationScenario scenario,
		SimulationCompatibilityIdentity identity,
		int count,
		int endingTurn = 1,
		bool incompleteLastRun = false,
		IEnumerable<int>? incompleteRunNumbers = null)
	{
		var incomplete = incompleteRunNumbers?.ToHashSet() ?? [];
		return new SimulationBatchSourceEvidence(
			scenario.ToCanonical(),
			identity.Profile,
			BaselineRandomDecisionStrategy.Identity,
			Enumerable.Range(0, count).Select(run =>
			{
				var material = new RunSeedMaterial(
					identity,
					BaselineRandomDecisionStrategy.Identity,
					run);
				return (incompleteLastRun && run == count - 1) || incomplete.Contains(run)
					? (SimulationRun)new IncompleteSimulationRun(material)
					: new CompletedSimulationRun(
						material,
						new SingleFactionGameResult(Faction.Villager),
						endingTurn,
						VictoryCheckWindow.Dawn);
			}));
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
