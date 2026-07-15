using FluentAssertions;
using System.Text;
using System.Text.Json;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Core.Tests.Unit;

public sealed class BuildTimeCachePublicationTests
{
	[Fact]
	public void GenerateToFiles_SuccessfullyStagesValidatesAndCommitsBothFiles()
	{
		var fileSystem = new RecordingPublicationFileSystem();
		var generator = CreateGenerator(fileSystem: fileSystem);
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => value.IsAlreadyDecided);
		var directory = Directory.CreateTempSubdirectory("werewolves-cache-success-");
		var artifactPath = Path.Combine(directory.FullName, "cache.json");
		var diagnosticsPath = Path.Combine(directory.FullName, "diagnostics.json");
		try
		{
			var result = generator.GenerateToFiles(artifactPath, diagnosticsPath, [entry]);

			result.Status.Should().Be(BuildTimeCacheGenerationStatus.Completed);
			File.ReadAllBytes(artifactPath).Should().Equal(result.ArtifactBytes!);
			BuildTimeCacheDiagnosticsJson.Read(
				File.ReadAllBytes(diagnosticsPath),
				result.ArtifactBytes).Rejection.Should().BeNull();
			fileSystem.Boundaries.Should().Equal(
				BuildTimeCachePublicationBoundary.ArtifactStaged,
				BuildTimeCachePublicationBoundary.DiagnosticsStaged,
				BuildTimeCachePublicationBoundary.BeforeCommitDecision,
				BuildTimeCachePublicationBoundary.DiagnosticsCommitted,
				BuildTimeCachePublicationBoundary.BeforeArtifactCommit,
				BuildTimeCachePublicationBoundary.ArtifactCommitted,
				BuildTimeCachePublicationBoundary.CleanupCompleted);
		}
		finally
		{
			directory.Delete(recursive: true);
		}
	}

	[Theory]
	[InlineData("completed")]
	[InlineData("cancelled")]
	[InlineData("failed")]
	public void GenerateToFiles_WithEquivalentPaths_RejectsBeforeGenerationOrFileSystemAccess(
		string prospectiveOutcome)
	{
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => prospectiveOutcome == "completed" || !value.IsAlreadyDecided);
		using var cancellation = new CancellationTokenSource();
		var executionCount = 0;
		var fileSystem = new RecordingPublicationFileSystem();
		var generator = CreateGenerator(
			(_, _, _, _) =>
			{
				executionCount++;
				if (prospectiveOutcome == "cancelled")
				{
					cancellation.Cancel();
					throw new OperationCanceledException(cancellation.Token);
				}

				throw new InvalidOperationException("controlled failure");
			},
			fileSystem: fileSystem);
		var directory = Directory.CreateTempSubdirectory("werewolves-cache-alias-");
		var path = Path.Combine(directory.FullName, "cache.json");
		File.WriteAllText(path, "previous");
		try
		{
			var equivalentPath = Path.Combine(directory.FullName, ".", "cache.json");

			var act = () => generator.GenerateToFiles(
				path,
				equivalentPath,
				[entry],
				cancellation.Token);

			act.Should().Throw<ArgumentException>();
			File.ReadAllText(path).Should().Be("previous");
			executionCount.Should().Be(0);
			fileSystem.Operations.Should().BeEmpty();
		}
		finally
		{
			directory.Delete(recursive: true);
		}
	}

	[Fact]
	public void GenerateToFiles_OnCaseInsensitivePlatforms_RejectsCaseOnlyPathAlias()
	{
		if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows())
		{
			return;
		}

		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => value.IsAlreadyDecided);
		var fileSystem = new RecordingPublicationFileSystem();
		var generator = CreateGenerator(fileSystem: fileSystem);
		var directory = Directory.CreateTempSubdirectory("werewolves-cache-case-alias-");
		var path = Path.Combine(directory.FullName, "cache.json");
		File.WriteAllText(path, "previous");
		try
		{
			var act = () => generator.GenerateToFiles(
				path,
				Path.Combine(directory.FullName, "CACHE.JSON"),
				[entry]);

			act.Should().Throw<ArgumentException>();
			File.ReadAllText(path).Should().Be("previous");
			fileSystem.Operations.Should().BeEmpty();
		}
		finally
		{
			directory.Delete(recursive: true);
		}
	}

	[Theory]
	[InlineData("completed")]
	[InlineData("cancelled")]
	[InlineData("failed")]
	public void GenerateToFiles_WithSymlinkedParentAlias_RejectsBeforeGenerationOrWrites(
		string prospectiveOutcome)
	{
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => prospectiveOutcome == "completed" || !value.IsAlreadyDecided);
		using var cancellation = new CancellationTokenSource();
		var executionCount = 0;
		var fileSystem = new RecordingPublicationFileSystem();
		var generator = CreateGenerator(
			(_, _, _, _) =>
			{
				executionCount++;
				if (prospectiveOutcome == "cancelled")
				{
					cancellation.Cancel();
					throw new OperationCanceledException(cancellation.Token);
				}

				throw new InvalidOperationException("controlled failure");
			},
			fileSystem: fileSystem);
		var directory = Directory.CreateTempSubdirectory("werewolves-cache-symlink-alias-");
		var physicalDirectory = Directory.CreateDirectory(
			Path.Combine(directory.FullName, "physical"));
		var linkedDirectory = Path.Combine(directory.FullName, "linked");
		Directory.CreateSymbolicLink(linkedDirectory, physicalDirectory.FullName);
		var physicalPath = Path.Combine(physicalDirectory.FullName, "cache.json");
		var linkedPath = Path.Combine(linkedDirectory, "cache.json");
		File.WriteAllText(physicalPath, "previous");
		try
		{
			BuildTimeTerminalLobbyCacheGenerator.ResolvePhysicalPath(linkedPath)
				.Should().Be(BuildTimeTerminalLobbyCacheGenerator.ResolvePhysicalPath(physicalPath));
			var act = () => generator.GenerateToFiles(
				physicalPath,
				linkedPath,
				[entry],
				cancellation.Token);

			act.Should().Throw<ArgumentException>();
			File.ReadAllText(physicalPath).Should().Be("previous");
			executionCount.Should().Be(0);
			fileSystem.Operations.Should().BeEmpty();
		}
		finally
		{
			directory.Delete(recursive: true);
		}
	}

	[Theory]
	[InlineData("completed")]
	[InlineData("cancelled")]
	[InlineData("failed")]
	public void GenerateToFiles_OnMacOSWithUnicodeEquivalentParents_RejectsBeforeGenerationOrWrites(
		string prospectiveOutcome)
	{
		if (!OperatingSystem.IsMacOS())
		{
			return;
		}

		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => prospectiveOutcome == "completed" || !value.IsAlreadyDecided);
		using var cancellation = new CancellationTokenSource();
		var executionCount = 0;
		var fileSystem = new RecordingPublicationFileSystem();
		var generator = CreateGenerator(
			(_, _, _, _) =>
			{
				executionCount++;
				if (prospectiveOutcome == "cancelled")
				{
					cancellation.Cancel();
					throw new OperationCanceledException(cancellation.Token);
				}

				throw new InvalidOperationException("controlled failure");
			},
			fileSystem: fileSystem);
		var directory = Directory.CreateTempSubdirectory("werewolves-cache-unicode-alias-");
		var composedDirectory = Path.Combine(directory.FullName, "caf\u00e9");
		var decomposedDirectory = Path.Combine(directory.FullName, "cafe\u0301");
		Directory.CreateDirectory(composedDirectory);
		var composedPath = Path.Combine(composedDirectory, "cache.json");
		var decomposedPath = Path.Combine(decomposedDirectory, "cache.json");
		File.WriteAllText(composedPath, "previous");
		try
		{
			File.Exists(decomposedPath).Should().BeTrue(
				"macOS canonical Unicode spellings target the same filesystem entry");
			var act = () => generator.GenerateToFiles(
				composedPath,
				decomposedPath,
				[entry],
				cancellation.Token);

			act.Should().Throw<ArgumentException>();
			File.ReadAllText(composedPath).Should().Be("previous");
			executionCount.Should().Be(0);
			fileSystem.Operations.Should().BeEmpty();
		}
		finally
		{
			directory.Delete(recursive: true);
		}
	}

	[Fact]
	public void Generate_WhenFinalProgressCallbackCancels_ObservesCancellationBeforeFinalization()
	{
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => value.IsAlreadyDecided);
		using var cancellation = new CancellationTokenSource();
		var generator = CreateGenerator(
			progress: progress =>
			{
				if (progress.CompletedScenarioCount == progress.TotalScenarioCount)
				{
					cancellation.Cancel();
				}
			});

		var act = () => generator.Generate([entry], cancellation.Token);

		act.Should().Throw<OperationCanceledException>();
	}

	[Fact]
	public void GenerateToFiles_WhenFinalProgressCallbackCancels_PreservesPreviousArtifact()
	{
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => value.IsAlreadyDecided);
		using var cancellation = new CancellationTokenSource();
		var generator = CreateGenerator(
			progress: progress =>
			{
				if (progress.CompletedScenarioCount == progress.TotalScenarioCount)
				{
					cancellation.Cancel();
				}
			});
		var directory = Directory.CreateTempSubdirectory("werewolves-cache-final-progress-");
		var artifactPath = Path.Combine(directory.FullName, "cache.json");
		var diagnosticsPath = Path.Combine(directory.FullName, "diagnostics.json");
		File.WriteAllText(artifactPath, "previous");
		try
		{
			var result = generator.GenerateToFiles(
				artifactPath,
				diagnosticsPath,
				[entry],
				cancellation.Token);

			result.Status.Should().Be(BuildTimeCacheGenerationStatus.Cancelled);
			File.ReadAllText(artifactPath).Should().Be("previous");
			var read = BuildTimeCacheDiagnosticsJson.Read(
				File.ReadAllBytes(diagnosticsPath),
				artifactBytes: null);
			read.Rejection.Should().BeNull();
			read.Diagnostics!.Status.Should().Be(BuildTimeCacheGenerationStatus.Cancelled);
		}
		finally
		{
			directory.Delete(recursive: true);
		}
	}

	[Fact]
	public void GenerateToFiles_WhenCancellationFollowsReturnedIncompleteBatch_RetainsObservedEvidence()
	{
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => !value.IsAlreadyDecided);
		using var cancellation = new CancellationTokenSource();
		var generator = CreateGenerator((scenario, identity, count, _) =>
		{
			var batch = Batch(scenario, identity, count, incompleteLastRun: true);
			cancellation.Cancel();
			return batch;
		});
		var directory = Directory.CreateTempSubdirectory("werewolves-cache-partial-evidence-");
		var artifactPath = Path.Combine(directory.FullName, "cache.json");
		var diagnosticsPath = Path.Combine(directory.FullName, "diagnostics.json");
		File.WriteAllText(artifactPath, "previous");
		try
		{
			var result = generator.GenerateToFiles(
				artifactPath,
				diagnosticsPath,
				[entry],
				cancellation.Token);

			result.Status.Should().Be(BuildTimeCacheGenerationStatus.Cancelled);
			result.Diagnostics.SuspicionsByCode.Should().Contain(
				BuildTimeCacheSuspicionCode.IncompleteRun, 1);
			result.Diagnostics.IncompleteRunSeedMaterial.Should().ContainSingle()
				.Which.Should().Match<BuildTimeIncompleteRunDiagnostic>(value =>
					value.BatchPhase == BuildTimeBatchPhase.Screening
					&& value.RunSeedMaterial.RunNumber
						== TerminalLobbyEvaluator.ScreeningAttemptCount - 1);
			File.ReadAllText(artifactPath).Should().Be("previous");
			BuildTimeCacheDiagnosticsJson.Read(File.ReadAllBytes(diagnosticsPath), null)
				.Rejection.Should().BeNull();
		}
		finally
		{
			directory.Delete(recursive: true);
		}
	}

	[Fact]
	public void GenerateToFiles_WhenPublicationFails_RetainsAllPreviouslyObservedEvidence()
	{
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => !value.IsAlreadyDecided);
		var fileSystem = new RecordingPublicationFileSystem
		{
			BoundaryAction = boundary =>
			{
				if (boundary == BuildTimeCachePublicationBoundary.ArtifactStaged)
				{
					throw new IOException("controlled staging failure");
				}
			}
		};
		var generator = CreateGenerator(
			(scenario, identity, count, _) =>
				Batch(scenario, identity, count, incompleteLastRun: true),
			fileSystem: fileSystem);
		var directory = Directory.CreateTempSubdirectory("werewolves-cache-failed-evidence-");
		var artifactPath = Path.Combine(directory.FullName, "cache.json");
		var diagnosticsPath = Path.Combine(directory.FullName, "diagnostics.json");
		File.WriteAllText(artifactPath, "previous");
		try
		{
			var result = generator.GenerateToFiles(artifactPath, diagnosticsPath, [entry]);

			result.Status.Should().Be(BuildTimeCacheGenerationStatus.Failed);
			result.Diagnostics.OmissionsByCode.Should().Contain(
				BuildTimeCacheOmissionCode.ScreeningIncomplete,
				1);
			result.Diagnostics.SuspicionsByCode.Should().Contain(
				BuildTimeCacheSuspicionCode.IncompleteRun,
				1);
			result.Diagnostics.IncompleteRunSeedMaterial.Should().ContainSingle();
			File.ReadAllText(artifactPath).Should().Be("previous");
			BuildTimeCacheDiagnosticsJson.Read(File.ReadAllBytes(diagnosticsPath), null)
				.Rejection.Should().BeNull();
		}
		finally
		{
			directory.Delete(recursive: true);
		}
	}

	[Theory]
	[InlineData((int)BuildTimeCachePublicationBoundary.ArtifactStaged, false)]
	[InlineData((int)BuildTimeCachePublicationBoundary.DiagnosticsStaged, false)]
	[InlineData((int)BuildTimeCachePublicationBoundary.BeforeCommitDecision, false)]
	[InlineData((int)BuildTimeCachePublicationBoundary.DiagnosticsCommitted, true)]
	[InlineData((int)BuildTimeCachePublicationBoundary.BeforeArtifactCommit, true)]
	[InlineData((int)BuildTimeCachePublicationBoundary.ArtifactCommitted, true)]
	[InlineData((int)BuildTimeCachePublicationBoundary.CleanupCompleted, true)]
	public void GenerateToFiles_CancellationIsLinearizedAtCommitDecision(
		int cancellationBoundaryValue,
		bool expectCommitted)
	{
		var cancellationBoundary = (BuildTimeCachePublicationBoundary)cancellationBoundaryValue;
		using var cancellation = new CancellationTokenSource();
		var fileSystem = new RecordingPublicationFileSystem
		{
			BoundaryAction = boundary =>
			{
				if (boundary == cancellationBoundary)
				{
					cancellation.Cancel();
				}
			}
		};

		AssertBoundaryOutcome(
			fileSystem,
			cancellation.Token,
			expectCommitted,
			expectCancellation: !expectCommitted,
			cancellationBoundary);
	}

	[Theory]
	[InlineData((int)BuildTimeCachePublicationBoundary.ArtifactStaged, false)]
	[InlineData((int)BuildTimeCachePublicationBoundary.DiagnosticsStaged, false)]
	[InlineData((int)BuildTimeCachePublicationBoundary.BeforeCommitDecision, false)]
	[InlineData((int)BuildTimeCachePublicationBoundary.DiagnosticsCommitted, false)]
	[InlineData((int)BuildTimeCachePublicationBoundary.BeforeArtifactCommit, false)]
	[InlineData((int)BuildTimeCachePublicationBoundary.ArtifactCommitted, true)]
	[InlineData((int)BuildTimeCachePublicationBoundary.CleanupCompleted, true)]
	public void GenerateToFiles_FailureAtEveryPublicationBoundaryHasTruthfulOutcome(
		int failureBoundaryValue,
		bool expectCommitted)
	{
		var failureBoundary = (BuildTimeCachePublicationBoundary)failureBoundaryValue;
		var fileSystem = new RecordingPublicationFileSystem
		{
			BoundaryAction = boundary =>
			{
				if (boundary == failureBoundary)
				{
					throw new IOException("controlled publication boundary failure");
				}
			}
		};

		AssertBoundaryOutcome(
			fileSystem,
			CancellationToken.None,
			expectCommitted,
			expectCancellation: false,
			failureBoundary);
	}

	[Theory]
	[InlineData("unknown-status")]
	[InlineData("broken-count")]
	[InlineData("generator-identity")]
	[InlineData("artifact-profile")]
	[InlineData("artifact-hash")]
	[InlineData("artifact-length")]
	[InlineData("artifact-kind-counts")]
	[InlineData("status-artifact-combination")]
	public void GenerateToFiles_WhenCompletedDiagnosticsAreMutated_RejectsBeforePublication(
		string mutation)
	{
		var fileSystem = new RecordingPublicationFileSystem();
		var generator = CreateGenerator(
			(scenario, identity, count, _) =>
				Batch(scenario, identity, count, incompleteLastRun: false),
			fileSystem: fileSystem,
			diagnosticsSerializer: (diagnostics, artifactBytes) =>
			{
				var bytes = BuildTimeCacheDiagnosticsJson.Write(diagnostics, artifactBytes);
				return diagnostics.Status == BuildTimeCacheGenerationStatus.Completed
					? Mutate(bytes, mutation)
					: bytes;
			});
		var catalog = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile();
		var entries = mutation == "artifact-kind-counts"
			? new[]
			{
				catalog.First(value => value.IsAlreadyDecided),
				catalog.First(value => !value.IsAlreadyDecided)
			}
			: [catalog.First(value => value.IsAlreadyDecided)];
		var directory = Directory.CreateTempSubdirectory("werewolves-cache-mutated-diagnostics-");
		var artifactPath = Path.Combine(directory.FullName, "cache.json");
		var diagnosticsPath = Path.Combine(directory.FullName, "diagnostics.json");
		File.WriteAllText(artifactPath, "previous");
		try
		{
			var result = generator.GenerateToFiles(artifactPath, diagnosticsPath, entries);

			result.Status.Should().Be(BuildTimeCacheGenerationStatus.Failed);
			File.ReadAllText(artifactPath).Should().Be("previous");
			fileSystem.Boundaries.Should().NotContain(
				BuildTimeCachePublicationBoundary.ArtifactStaged);
			var read = BuildTimeCacheDiagnosticsJson.Read(
				File.ReadAllBytes(diagnosticsPath),
				artifactBytes: null);
			read.Rejection.Should().BeNull();
			read.Diagnostics!.Status.Should().Be(BuildTimeCacheGenerationStatus.Failed);
		}
		finally
		{
			directory.Delete(recursive: true);
		}
	}

	[Fact]
	public void DiagnosticsJson_Read_RejectsUnknownTypedCodesAndNonCanonicalSeedOrder()
	{
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => !value.IsAlreadyDecided);
		var result = CreateGenerator((scenario, identity, count, _) =>
			Batch(scenario, identity, count, incompleteLastRun: true))
			.Generate([entry]);
		var canonical = Encoding.UTF8.GetString(
			BuildTimeCacheDiagnosticsJson.Write(result.Diagnostics, result.ArtifactBytes));

		BuildTimeCacheDiagnosticsJson.Read(
			Encoding.UTF8.GetBytes(canonical.Replace(
				"screening-incomplete",
				"unknown-omission-x",
				StringComparison.Ordinal)),
			result.ArtifactBytes).Rejection.Should().NotBeNull();
		BuildTimeCacheDiagnosticsJson.Read(
			Encoding.UTF8.GetBytes(canonical.Replace(
				"incomplete-run",
				"unknown-suspicion",
				StringComparison.Ordinal)),
			result.ArtifactBytes).Rejection.Should().NotBeNull();
		BuildTimeCacheDiagnosticsJson.Read(
			Encoding.UTF8.GetBytes(canonical.Replace(
				"\"batchPhase\":\"screening\"",
				"\"batchPhase\":\"unknownxx\"",
				StringComparison.Ordinal)),
			result.ArtifactBytes).Rejection.Should().NotBeNull();
	}

	[Theory]
	[InlineData("diagnostics-schema")]
	[InlineData("cache-schema")]
	[InlineData("simulator-profile")]
	[InlineData("decision-strategy")]
	public void DiagnosticsJson_Read_RejectsMismatchedBoundaryIdentities(string mutation)
	{
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => value.IsAlreadyDecided);
		var result = CreateGenerator().Generate([entry]);
		var canonical = Encoding.UTF8.GetString(
			BuildTimeCacheDiagnosticsJson.Write(result.Diagnostics, result.ArtifactBytes));
		var mutated = mutation switch
		{
			"diagnostics-schema" => canonical.Replace(
				$"\"schema\":\"{BuildTimeCacheDiagnosticsJson.SchemaIdentifier}\"",
				"\"schema\":\"unknown-diagnostics-schema\"",
				StringComparison.Ordinal),
			"cache-schema" => canonical.Replace(
				$"\"cache\":{{\"schema\":\"{TerminalLobbyCache.SchemaIdentifier}\"",
				"\"cache\":{\"schema\":\"unknown-cache-schema\"",
				StringComparison.Ordinal),
			"simulator-profile" => canonical.Replace(
				$"\"simulator\":{{\"profile\":\"{SimulatorProfile.Active.Identity.ProfileId}\"",
				"\"simulator\":{\"profile\":\"unknown-profile\"",
				StringComparison.Ordinal),
			"decision-strategy" => canonical.Replace(
				$"\"decisionStrategy\":\"{BaselineRandomDecisionStrategy.Identity}\"",
				"\"decisionStrategy\":\"unknown-strategy@1\"",
				StringComparison.Ordinal),
			_ => throw new ArgumentOutOfRangeException(nameof(mutation))
		};
		mutated.Should().NotBe(canonical);

		BuildTimeCacheDiagnosticsJson.Read(
			Encoding.UTF8.GetBytes(mutated),
			result.ArtifactBytes).Rejection.Should().NotBeNull();
	}

	[Fact]
	public void DiagnosticsJson_Read_RejectsDuplicateAndOutOfOrderIncompleteSeeds()
	{
		var entries = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.Where(value => !value.IsAlreadyDecided)
			.Take(2)
			.ToArray();
		var result = CreateGenerator((scenario, identity, count, _) =>
			Batch(scenario, identity, count, incompleteLastRun: true))
			.Generate(entries);
		var canonical = Encoding.UTF8.GetString(
			BuildTimeCacheDiagnosticsJson.Write(result.Diagnostics, result.ArtifactBytes));
		var seedArray = ExtractArray(canonical, "incompleteRunSeedMaterial");
		var separator = seedArray.IndexOf("},{", StringComparison.Ordinal);
		separator.Should().BePositive();
		var first = seedArray[..(separator + 1)];
		var second = seedArray[(separator + 2)..];

		var outOfOrder = ReplaceArray(
			canonical,
			"incompleteRunSeedMaterial",
			second + "," + first);
		var duplicated = ReplaceArray(
			canonical,
			"incompleteRunSeedMaterial",
			first + "," + first);

		BuildTimeCacheDiagnosticsJson.Read(
			Encoding.UTF8.GetBytes(outOfOrder),
			result.ArtifactBytes).Rejection.Should().NotBeNull();
		BuildTimeCacheDiagnosticsJson.Read(
			Encoding.UTF8.GetBytes(duplicated),
			result.ArtifactBytes).Rejection.Should().NotBeNull();
	}

	[Fact]
	public void DiagnosticsJson_Read_RejectsDuplicateCodesAndOutOfOrderFields()
	{
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => !value.IsAlreadyDecided);
		var result = CreateGenerator((scenario, identity, count, _) =>
			Batch(scenario, identity, count, incompleteLastRun: true))
			.Generate([entry]);
		var canonical = Encoding.UTF8.GetString(
			BuildTimeCacheDiagnosticsJson.Write(result.Diagnostics, result.ArtifactBytes));
		var omissionArray = ExtractArray(canonical, "omissions");
		var duplicateCode = ReplaceArray(
			canonical,
			"omissions",
			omissionArray + "," + omissionArray);
		var outOfOrderFields = canonical.Replace(
			$"{{\"schema\":\"{BuildTimeCacheDiagnosticsJson.SchemaIdentifier}\",\"version\":1",
			$"{{\"version\":1,\"schema\":\"{BuildTimeCacheDiagnosticsJson.SchemaIdentifier}\"",
			StringComparison.Ordinal);

		BuildTimeCacheDiagnosticsJson.Read(
			Encoding.UTF8.GetBytes(duplicateCode),
			result.ArtifactBytes).Rejection.Should().NotBeNull();
		BuildTimeCacheDiagnosticsJson.Read(
			Encoding.UTF8.GetBytes(outOfOrderFields),
			result.ArtifactBytes).Rejection.Should().NotBeNull();
	}

	[Fact]
	public void DiagnosticsJson_Write_RejectsEnumeratedCountBeyondCatalogTotal()
	{
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => value.IsAlreadyDecided);
		var result = CreateGenerator().Generate([entry]);
		var extraOmissions = result.Diagnostics.TotalScenarioCount;
		var invalid = result.Diagnostics with
		{
			EnumeratedScenarioCount = result.Diagnostics.TotalScenarioCount + 1,
			OmittedCount = extraOmissions,
			OmissionsByCode = new Dictionary<BuildTimeCacheOmissionCode, int>
			{
				[BuildTimeCacheOmissionCode.TerminalRecordRejected] = extraOmissions
			}
		};

		var act = () => BuildTimeCacheDiagnosticsJson.Write(invalid, result.ArtifactBytes);

		act.Should().Throw<FormatException>();
	}

	[Fact]
	public void DiagnosticsJson_Write_RejectsIncompleteSeedOutsideCurrentCatalog()
	{
		var result = GenerateIncompleteDiagnostics();
		var scenario = new SimulationScenario(
			4,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager
			]);
		var invalidSeed = new BuildTimeIncompleteRunDiagnostic(
			BuildTimeBatchPhase.Screening,
			new RunSeedMaterial(
				new SimulationCompatibilityIdentity(
					scenario.ToCanonical(),
					SimulatorProfile.Active.Identity),
				BaselineRandomDecisionStrategy.Identity,
				runNumber: 0));
		var invalid = result.Diagnostics with
		{
			IncompleteRunSeedMaterial = [invalidSeed]
		};

		var act = () => BuildTimeCacheDiagnosticsJson.Write(invalid, result.ArtifactBytes);

		act.Should().Throw<FormatException>();
	}

	[Theory]
	[InlineData(BuildTimeBatchPhase.Screening, TerminalLobbyEvaluator.ScreeningAttemptCount)]
	[InlineData(BuildTimeBatchPhase.Probability, TerminalLobbyEvaluator.ProbabilityAttemptCount)]
	public void DiagnosticsJson_Write_RejectsRunNumberOutsideBatchPhase(
		BuildTimeBatchPhase phase,
		int runNumber)
	{
		var result = GenerateIncompleteDiagnostics();
		var original = result.Diagnostics.IncompleteRunSeedMaterial.Single().RunSeedMaterial;
		var invalidSeed = new BuildTimeIncompleteRunDiagnostic(
			phase,
			new RunSeedMaterial(
				original.CompatibilityIdentity,
				original.DecisionStrategyIdentity,
				runNumber));
		var invalid = result.Diagnostics with
		{
			IncompleteRunSeedMaterial = [invalidSeed]
		};

		var act = () => BuildTimeCacheDiagnosticsJson.Write(invalid, result.ArtifactBytes);

		act.Should().Throw<FormatException>();
	}

	private static void AssertBoundaryOutcome(
		RecordingPublicationFileSystem fileSystem,
		CancellationToken cancellationToken,
		bool expectCommitted,
		bool expectCancellation,
		BuildTimeCachePublicationBoundary expectedBoundary)
	{
		var generator = CreateGenerator(fileSystem: fileSystem);
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => value.IsAlreadyDecided);
		var directory = Directory.CreateTempSubdirectory("werewolves-cache-boundary-");
		var artifactPath = Path.Combine(directory.FullName, "cache.json");
		var diagnosticsPath = Path.Combine(directory.FullName, "diagnostics.json");
		File.WriteAllText(artifactPath, "previous");
		try
		{
			var result = generator.GenerateToFiles(
				artifactPath,
				diagnosticsPath,
				[entry],
				cancellationToken);

			fileSystem.Boundaries.Should().Contain(expectedBoundary);
			result.Status.Should().Be(expectCommitted
				? BuildTimeCacheGenerationStatus.Completed
				: expectCancellation
					? BuildTimeCacheGenerationStatus.Cancelled
					: BuildTimeCacheGenerationStatus.Failed);
			if (expectCommitted)
			{
				File.ReadAllBytes(artifactPath).Should().Equal(result.ArtifactBytes!);
			}
			else
			{
				File.ReadAllText(artifactPath).Should().Be("previous");
			}

			var diagnosticsRead = BuildTimeCacheDiagnosticsJson.Read(
				File.ReadAllBytes(diagnosticsPath),
				expectCommitted ? result.ArtifactBytes : null);
			diagnosticsRead.Rejection.Should().BeNull();
			diagnosticsRead.Diagnostics!.Status.Should().Be(result.Status);
			Directory.EnumerateFiles(directory.FullName, "*.tmp").Should().BeEmpty();
		}
		finally
		{
			directory.Delete(recursive: true);
		}
	}

	private static byte[] Mutate(byte[] canonical, string mutation)
	{
		var json = Encoding.UTF8.GetString(canonical);
		var mutated = mutation switch
		{
			"unknown-status" => json.Replace(
				"\"status\":\"completed\"",
				"\"status\":\"mysteryxx\"",
				StringComparison.Ordinal),
			"broken-count" => json.Replace(
				"\"enumerated\":1",
				"\"enumerated\":2",
				StringComparison.Ordinal),
			"generator-identity" => json.Replace(
				BuildTimeTerminalLobbyCacheGenerator.GeneratorIdentifier,
				"unknown-terminal-lobby-cache-generator",
				StringComparison.Ordinal),
			"artifact-profile" => json.Replace(
				$"\"profileVersion\":\"{SimulatorProfile.Active.Identity.Version}\"",
				$"\"profileVersion\":\"{SimulatorProfile.Active.Identity.Version}-other\"",
				StringComparison.Ordinal),
			"artifact-hash" => ReplaceArtifactHash(json),
			"artifact-length" => ReplaceArtifactLength(json),
			"artifact-kind-counts" => json.Replace(
				"\"alreadyDecided\":1,\"degenerate\":1",
				"\"alreadyDecided\":0,\"degenerate\":2",
				StringComparison.Ordinal),
			"status-artifact-combination" => json.Replace(
				"\"status\":\"completed\"",
				"\"status\":\"cancelled\"",
				StringComparison.Ordinal),
			_ => throw new ArgumentOutOfRangeException(nameof(mutation))
		};
		mutated.Should().NotBe(json);
		return Encoding.UTF8.GetBytes(mutated);
	}

	private static string ReplaceArtifactHash(string json)
	{
		const string marker = "\"sha256\":\"";
		var index = json.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
		return json[..index] + (json[index] == '0' ? '1' : '0') + json[(index + 1)..];
	}

	private static string ReplaceArtifactLength(string json)
	{
		const string marker = "\"bytes\":";
		var start = json.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
		var end = json.IndexOf('}', start);
		var value = int.Parse(json[start..end]);
		return string.Concat(json.AsSpan(0, start), (value + 1).ToString(), json.AsSpan(end));
	}

	private static string ExtractArray(string json, string propertyName)
	{
		using var document = JsonDocument.Parse(json);
		var raw = document.RootElement.GetProperty(propertyName).GetRawText();
		return raw[1..^1];
	}

	private static string ReplaceArray(string json, string propertyName, string replacement)
	{
		using var document = JsonDocument.Parse(json);
		var raw = document.RootElement.GetProperty(propertyName).GetRawText();
		return json.Replace(
			$"\"{propertyName}\":{raw}",
			$"\"{propertyName}\":[{replacement}]",
			StringComparison.Ordinal);
	}

	private static BuildTimeTerminalLobbyCacheGenerator CreateGenerator(
		Func<SimulationScenario, SimulationCompatibilityIdentity, int, CancellationToken,
			SimulationBatchSourceEvidence>? executeBatch = null,
		Action<BuildTimeCacheGenerationProgress>? progress = null,
		IBuildTimeCachePublicationFileSystem? fileSystem = null,
		Func<BuildTimeCacheGenerationDiagnostics, byte[]?, byte[]>? diagnosticsSerializer = null) =>
		new(
			executeBatch ?? ((_, _, _, _) => throw new InvalidOperationException()),
			progress,
			fileSystem ?? new RecordingPublicationFileSystem(),
		diagnosticsSerializer ?? BuildTimeCacheDiagnosticsJson.Write);

	private static BuildTimeCacheGenerationResult GenerateIncompleteDiagnostics()
	{
		var entry = TerminalLobbyScenarioCatalog.EnumerateCurrentProfile()
			.First(value => !value.IsAlreadyDecided);
		return CreateGenerator((scenario, identity, count, _) =>
			Batch(scenario, identity, count, incompleteLastRun: true))
			.Generate([entry]);
	}

	private static SimulationBatchSourceEvidence Batch(
		SimulationScenario scenario,
		SimulationCompatibilityIdentity identity,
		int count,
		bool incompleteLastRun)
	{
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
				return incompleteLastRun && run == count - 1
					? (SimulationRun)new IncompleteSimulationRun(material)
					: new CompletedSimulationRun(
						material,
						new SingleFactionGameResult(Faction.Villager),
						endingTurn: 1,
						VictoryCheckWindow.Dawn);
			}));
	}

	private sealed class RecordingPublicationFileSystem : IBuildTimeCachePublicationFileSystem
	{
		public List<string> Operations { get; } = [];

		public List<BuildTimeCachePublicationBoundary> Boundaries { get; } = [];

		public Action<BuildTimeCachePublicationBoundary>? BoundaryAction { get; init; }

		public void CreateDirectory(string path)
		{
			Operations.Add($"mkdir:{path}");
			Directory.CreateDirectory(path);
		}

		public void WriteDurable(string path, byte[] bytes)
		{
			Operations.Add($"write:{path}");
			File.WriteAllBytes(path, bytes);
		}

		public void MoveReplace(string sourcePath, string destinationPath)
		{
			Operations.Add($"move:{sourcePath}->{destinationPath}");
			File.Move(sourcePath, destinationPath, overwrite: true);
		}

		public bool FileExists(string path)
		{
			Operations.Add($"exists:{path}");
			return File.Exists(path);
		}

		public void DeleteFile(string path)
		{
			Operations.Add($"delete:{path}");
			File.Delete(path);
		}

		public void ReachBoundary(BuildTimeCachePublicationBoundary boundary)
		{
			Operations.Add($"boundary:{boundary}");
			Boundaries.Add(boundary);
			BoundaryAction?.Invoke(boundary);
		}
	}
}
