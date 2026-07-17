using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class LobbyEvaluationCoordinatorTests
{
	[Fact]
	public async Task Dispose_CancelsTheSingleBundledLoadBeforeAnyDownstreamWork()
	{
		var bundled = new ControlledByteSource();
		var local = new RecordingLocalStore(bytes: null);
		var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());
		var coordinator = new LobbyEvaluationCoordinator(
			CreateLobby(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager),
			bundled,
			local,
			evaluator,
			TimeProvider.System);
		var read = await bundled.NextReadAsync();

		coordinator.Dispose();
		read.Complete(bytes: null);
		await Task.Yield();

		bundled.LastCancellationToken.IsCancellationRequested.Should().BeTrue();
		local.ReadCount.Should().Be(0);
		evaluator.CallCount.Should().Be(0);
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
	}

	[Fact]
	public async Task EffectiveSetupChanges_LoadBundledCacheOnceButReadMutableLocalCacheEachTime()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var firstRecord = AlreadyDecidedRecord(lobby.CreateSimulationScenario());
		var secondScenario = new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager
			]);
		var secondRecord = AlreadyDecidedRecord(secondScenario);
		var bundled = new RecordingByteSource(DocumentBytes());
		var local = new RecordingLocalStore(DocumentBytes(firstRecord, secondRecord));
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			bundled,
			local,
			new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation()),
			TimeProvider.System);
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		ChangeToFourWerewolves(lobby);
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		coordinator.State.Identity.Should().Be(secondRecord.CompatibilityIdentity);
		bundled.LogicalNames.Should().Equal(LobbyEvaluationCoordinator.BundledCacheLogicalName);
		local.ReadCount.Should().Be(2);
	}

	[Fact]
	public async Task ThrowingReentrantCancellationCallback_CannotBlockReplacementProgress()
	{
		var pump = new ControlledContinuationPump();
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		LobbyEvaluationCoordinator coordinator = null!;
		var evaluator = new ReentrantCancellationEvaluator(() =>
			coordinator.TryRequestLobbyExit());
		pump.Run(() =>
		{
			coordinator = new LobbyEvaluationCoordinator(
				lobby,
				new RecordingByteSource(bytes: null),
				new RecordingLocalStore(bytes: null),
				evaluator,
				TimeProvider.System,
				_ => new LobbyScenarioSupport(
					RulesValid: true,
					AppSupported: true,
					SimulatorProfile.Active));
			coordinator.TryRequestLobbyExit().Should().BeFalse();
			pump.Drain();
		});
		await evaluator.FirstCallStarted.WaitAsync(TimeSpan.FromSeconds(5));

		var mutation = Task.Run(() =>
			lobby.DecrementRole(MainRoleType.SimpleWerewolf));
		await evaluator.CallbackEntered.WaitAsync(TimeSpan.FromSeconds(5));
		evaluator.CompleteFirstCall();
		var staleDrain = Task.Run(pump.Drain);
		try
		{
			await staleDrain.WaitAsync(TimeSpan.FromSeconds(5));
		}
		finally
		{
			evaluator.ReleaseCancellationCallback();
		}
		await mutation.WaitAsync(TimeSpan.FromSeconds(5));
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.CouldNotEvaluate);

		evaluator.ReentrantExitResult.Should().BeFalse();
		evaluator.CallCount.Should().Be(2);
		coordinator.State.Identity.Should().NotBeNull();
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.CouldNotEvaluate);
		coordinator.Dispose();
	}

	[Fact]
	public void SimulatorUnavailableProjection_ReleasesGateWithoutCacheOrFallback()
	{
		var bundled = new RecordingByteSource(bytes: null);
		var local = new RecordingLocalStore(bytes: null);
		var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());
		using var coordinator = new LobbyEvaluationCoordinator(
			CreateLobby(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager),
			bundled,
			local,
			evaluator,
			TimeProvider.System,
			_ => new LobbyScenarioSupport(
				RulesValid: true,
				AppSupported: true,
				SimulatorProfile: null));

		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.SimulatorUnavailable);
		coordinator.State.Identity.Should().BeNull();
		coordinator.EvaluationBlocksLobbyExit.Should().BeFalse();
		coordinator.TryRequestLobbyExit().Should().BeTrue();
		bundled.LogicalNames.Should().BeEmpty();
		local.ReadCount.Should().Be(0);
		evaluator.CallCount.Should().Be(0);
	}

	[Fact]
	public async Task IdentityChange_ClearsRetainedCouldNotEvaluateAndRejectsOldRetry()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var clock = new ManualTimeProvider();
		var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			new RecordingByteSource(bytes: null),
			new RecordingLocalStore(bytes: null),
			new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation()),
			clock);
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.CouldNotEvaluate);
		var failedIdentity = coordinator.State.Identity;

		ChangeToFourWerewolves(lobby);

		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.State.Identity.Should().NotBe(failedIdentity);
		coordinator.RetryCurrent().Should().BeFalse();
		coordinator.Dispose();
	}

	[Fact]
	public async Task RepeatedExitAttemptsAndSeatingEdit_DoNotDuplicateFallback()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var evaluator = new ControlledEvaluator();
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			new RecordingByteSource(bytes: null),
			new RecordingLocalStore(bytes: null),
			evaluator,
			TimeProvider.System);

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		var call = await evaluator.NextCallAsync();
		lobby.MovePlayerDown(0).Should().BeTrue();
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		evaluator.CallCount.Should().Be(1);

		call.Complete(new AlreadyDecidedTerminalEvaluation(
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut));
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);
		evaluator.CallCount.Should().Be(1);
	}

	[Fact]
	public async Task CacheReadIoFailures_AreMissesAndFallbackStillPersists()
	{
		var local = new ReadFailingLocalStore();
		using var coordinator = new LobbyEvaluationCoordinator(
			CreateLobby(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager),
			new ThrowingByteSource(),
			local,
			new RecordingEvaluator(new AlreadyDecidedTerminalEvaluation(
				new SingleFactionGameResult(Faction.Werewolf),
				AlreadyDecidedReason.WerewolfControlShortcut)),
			TimeProvider.System);

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		local.Writes.Should().ContainSingle();
	}

	[Fact]
	public async Task UnexpectedTerminalCapture_PublishesCouldNotAndPersistsNothing()
	{
		var local = new RecordingLocalStore(bytes: null);
		using var coordinator = new LobbyEvaluationCoordinator(
			CreateLobby(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager),
			new RecordingByteSource(bytes: null),
			local,
			new RecordingEvaluator(new UnexpectedTerminalEvaluation()),
			TimeProvider.System);

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.CouldNotEvaluate);

		local.Writes.Should().BeEmpty();
		coordinator.TryRequestLobbyExit().Should().BeTrue();
	}

	[Fact]
	public async Task ScenarioChange_DuringBundledReadSharesOneLoadAndPublishesOnlyTheCurrentIdentity()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var bundled = new ControlledByteSource();
		var local = new RecordingLocalStore(bytes: null);
		var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby, bundled, local, evaluator, TimeProvider.System);
		var sharedRead = await bundled.NextReadAsync();
		var staleRecord = AlreadyDecidedRecord(lobby.CreateSimulationScenario());

		ChangeToFourWerewolves(lobby);
		var currentIdentity = coordinator.State.Identity;
		var currentRecord = AlreadyDecidedRecord(lobby.CreateSimulationScenario());
		bundled.ReadCount.Should().Be(1);
		sharedRead.Complete(DocumentBytes(staleRecord, currentRecord));
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		local.ReadCount.Should().Be(0);
		evaluator.CallCount.Should().Be(0);
		coordinator.State.Identity.Should().Be(currentIdentity);
	}

	[Fact]
	public void ScenarioChange_DuringLocalReadStopsStalePipelineBeforeFallback()
	{
		var pump = new ControlledContinuationPump();
		pump.Run(() =>
		{
			var clock = new ManualTimeProvider();
			var lobby = CreateLobby(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
			var local = new ControlledReadLocalStore();
			var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());
			var coordinator = new LobbyEvaluationCoordinator(
				lobby,
				new RecordingByteSource(bytes: null),
				local,
				evaluator,
				clock);
			pump.Drain();
			var staleRead = local.NextReadAsync().GetAwaiter().GetResult();

			ChangeToVillagerMajority(lobby);
			pump.Drain();
			var currentRead = local.NextReadAsync().GetAwaiter().GetResult();
			staleRead.Complete(bytes: null);
			pump.Drain();
			clock.Advance(TimeSpan.FromMilliseconds(500));
			pump.Drain();

			evaluator.CallCount.Should().Be(0);
			coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
			coordinator.Dispose();
			currentRead.Complete(bytes: null);
			pump.Drain();
		});
	}

	[Fact]
	public async Task ScenarioChange_AtCommitBoundaryRejectsStaleWriteAndCurrentWriteSurvives()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var local = new ControlledCommitLocalStore();
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			new RecordingByteSource(bytes: null),
			local,
			new RecordingEvaluator(new AlreadyDecidedTerminalEvaluation(
				new SingleFactionGameResult(Faction.Werewolf),
				AlreadyDecidedReason.WerewolfControlShortcut)),
			TimeProvider.System);
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		var staleWrite = await local.NextWriteAsync();
		await staleWrite.CommitBoundary.WaitAsync(TimeSpan.FromSeconds(5));

		ChangeToFourWerewolves(lobby);
		var currentIdentity = coordinator.State.Identity;
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		staleWrite.ReleaseCommitBoundary();
		await staleWrite.Disposed.WaitAsync(TimeSpan.FromSeconds(5));
		var currentWrite = await local.NextWriteAsync();
		await currentWrite.CommitBoundary.WaitAsync(TimeSpan.FromSeconds(5));

		local.CommittedBytes.Should().BeNull("the stale request was denied at the commit boundary");
		currentWrite.ReleaseCommitBoundary();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		var persisted = TerminalLobbyCache.ReadDocument(local.CommittedBytes!.Value.Span);
		persisted.IsUsable.Should().BeTrue();
		persisted.Document!.Records.Should().ContainSingle(record =>
			record.CompatibilityIdentity.Equals(currentIdentity));
	}

	[Fact]
	public async Task ScenarioChange_DuringStagedWritePreventsAnyCommitAttempt()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var clock = new ManualTimeProvider();
		var local = new ControlledStageLocalStore();
		var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			new RecordingByteSource(bytes: null),
			local,
			new RecordingEvaluator(new AlreadyDecidedTerminalEvaluation(
				new SingleFactionGameResult(Faction.Werewolf),
				AlreadyDecidedReason.WerewolfControlShortcut)),
			clock);
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await local.StageStarted.WaitAsync(TimeSpan.FromSeconds(5));

		ChangeToFourWerewolves(lobby);
		local.ReleaseStage();
		await local.StagedWriteDisposed.WaitAsync(TimeSpan.FromSeconds(5));

		local.CommitAttemptCount.Should().Be(0);
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.Dispose();
	}

	[Fact]
	public async Task CompactAggregateRecords_MapToTheirTerminalGateMeanings()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var identity = new SimulationCompatibilityIdentity(
			lobby.CreateSimulationScenario().ToCanonical(),
			SimulatorProfile.Active.Identity);
		var rows1000 = AggregateRows(1_000, 750, 250);
		var cells1000 = AggregateCells(1_000, 750, 250, turnOneOnly: true);
		var rows10000 = AggregateRows(10_000, 7_000, 3_000);
		var villager = new SingleFactionGameResult(Faction.Villager);
		var werewolf = new SingleFactionGameResult(Faction.Werewolf);
		var cells10000 = new TerminalCacheTurnWindowFrequency[]
		{
			new(villager, 1, VictoryCheckWindow.Dawn, 3_000, 10_000),
			new(villager, 1, VictoryCheckWindow.PreNight, 4_000, 10_000),
			new(werewolf, 2, VictoryCheckWindow.PreNight, 3_000, 10_000)
		};
		var cases = new (TerminalLobbyCacheRecord Record, LobbyEvaluationStateKind Kind, bool Blocks)[]
		{
			(new DegenerateTerminalCacheRecord(identity, rows1000, cells1000),
				LobbyEvaluationStateKind.Degenerate, true),
			(new ProbabilityTerminalCacheRecord(identity, rows10000, cells10000),
				LobbyEvaluationStateKind.Probability, false)
		};

		foreach (var @case in cases)
		{
			using var coordinator = new LobbyEvaluationCoordinator(
				lobby,
				new RecordingByteSource(DocumentBytes(@case.Record)),
				new RecordingLocalStore(bytes: null),
				new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation()),
				TimeProvider.System);
			await WaitForStateAsync(coordinator, @case.Kind);
			coordinator.Depth.Should().Be(LobbyEvaluationDepth.FullProbability);

			if (@case.Kind == LobbyEvaluationStateKind.Probability)
			{
				var projectedVillager = coordinator.State.Probability!.Outcomes
					.Single(outcome => outcome.GameResult.Equals(villager));
				projectedVillager.Turns.Should().ContainSingle().Which.Should().Be(
					new LobbyProbabilityTurnData(1, 7_000, 10_000));
			}
			else
			{
				coordinator.State.Probability.Should().BeNull();
			}
			coordinator.EvaluationBlocksLobbyExit.Should().Be(@case.Blocks);
			coordinator.TryRequestLobbyExit().Should().Be(!@case.Blocks);
		}
	}

	[Fact]
	public async Task DegenerateScreeningOnly_CachedProbabilityMeansScreeningPassedWithoutProbabilityData()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var identity = new SimulationCompatibilityIdentity(
			lobby.CreateSimulationScenario().ToCanonical(),
			SimulatorProfile.Active.Identity);
		var cachedProbability = new ProbabilityTerminalCacheRecord(
			identity,
			AggregateRows(10_000, 7_000, 3_000),
			AggregateCells(10_000, 7_000, 3_000, turnOneOnly: false));
		var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());

		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			new RecordingByteSource(DocumentBytes(cachedProbability)),
			new RecordingLocalStore(bytes: null),
			evaluator,
			LobbyEvaluationDepth.DegenerateScreeningOnly,
			TimeProvider.System);
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.ScreeningPassed);

		coordinator.Depth.Should().Be(LobbyEvaluationDepth.DegenerateScreeningOnly);
		coordinator.State.Probability.Should().BeNull();
		coordinator.EvaluationBlocksLobbyExit.Should().BeFalse();
		coordinator.TryRequestLobbyExit().Should().BeTrue();
		evaluator.CallCount.Should().Be(0);
	}

	[Fact]
	public async Task DegenerateScreeningOnly_CachedDegenerateResultStillBlocksWithoutFallback()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var identity = new SimulationCompatibilityIdentity(
			lobby.CreateSimulationScenario().ToCanonical(),
			SimulatorProfile.Active.Identity);
		var cachedDegenerate = new DegenerateTerminalCacheRecord(
			identity,
			AggregateRows(1_000, 750, 250),
			AggregateCells(1_000, 750, 250, turnOneOnly: true));
		var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());

		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			new RecordingByteSource(DocumentBytes(cachedDegenerate)),
			new RecordingLocalStore(bytes: null),
			evaluator,
			LobbyEvaluationDepth.DegenerateScreeningOnly,
			TimeProvider.System);
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.Degenerate);

		coordinator.State.Probability.Should().BeNull();
		coordinator.EvaluationBlocksLobbyExit.Should().BeTrue();
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		evaluator.CallCount.Should().Be(0);
	}

	[Fact]
	public async Task DegenerateScreeningOnly_ScreeningPassedFallbackIsNonblockingAndNotPersisted()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var local = new RecordingLocalStore(bytes: null);
		var screeningPassed = new ScreeningPassedLobbyEvaluation();
		var evaluator = new RecordingEvaluator(screeningPassed);
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			new RecordingByteSource(bytes: null),
			local,
			evaluator,
			LobbyEvaluationDepth.DegenerateScreeningOnly,
			TimeProvider.System);

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.ScreeningPassed);

		coordinator.State.Probability.Should().BeNull();
		coordinator.TryRequestLobbyExit().Should().BeTrue();
		local.Writes.Should().BeEmpty();
		evaluator.Depths.Should().Equal(LobbyEvaluationDepth.DegenerateScreeningOnly);
	}

	[Fact]
	public async Task SuccessfulFallback_PreservesOtherValidLocalRecords()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var other = AlreadyDecidedRecord(new SimulationScenario(
			5,
			[
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager
			]));
		var local = new RecordingLocalStore(DocumentBytes(other));
		var evaluator = new RecordingEvaluator(new AlreadyDecidedTerminalEvaluation(
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut));
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			new RecordingByteSource(bytes: null),
			local,
			evaluator,
			TimeProvider.System);

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);
		evaluator.Depths.Should().Equal(LobbyEvaluationDepth.FullProbability);

		var written = TerminalLobbyCache.ReadDocument(local.Writes.Single());
		written.IsUsable.Should().BeTrue();
		written.Document!.Records.Should().HaveCount(2);
		written.Document.Records.Should().Contain(record =>
			record.CompatibilityIdentity.Equals(other.CompatibilityIdentity));
		written.Document.Records.Should().ContainSingle(record =>
			record.CompatibilityIdentity.Equals(coordinator.State.Identity));
	}

	[Fact]
	public async Task SuccessfulFallback_ReplacesCorruptLocalDocumentWithOneCanonicalRecord()
	{
		var local = new RecordingLocalStore("corrupt"u8.ToArray());
		using var coordinator = new LobbyEvaluationCoordinator(
			CreateLobby(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager),
			new RecordingByteSource(bytes: null),
			local,
			new RecordingEvaluator(new AlreadyDecidedTerminalEvaluation(
				new SingleFactionGameResult(Faction.Werewolf),
				AlreadyDecidedReason.WerewolfControlShortcut)),
			TimeProvider.System);

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		var written = TerminalLobbyCache.ReadDocument(local.Writes.Single());
		written.IsUsable.Should().BeTrue();
		written.Document!.Records.Should().ContainSingle();
	}

	[Fact]
	public async Task StorageFailure_KeepsValidTerminalMeaningInMemory()
	{
		using var coordinator = new LobbyEvaluationCoordinator(
			CreateLobby(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager),
			new RecordingByteSource(bytes: null),
			new ThrowingLocalStore(),
			new RecordingEvaluator(new AlreadyDecidedTerminalEvaluation(
				new SingleFactionGameResult(Faction.Werewolf),
				AlreadyDecidedReason.WerewolfControlShortcut)),
			TimeProvider.System);

		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		coordinator.State.DecidedGameResult.Should().Be(new SingleFactionGameResult(Faction.Werewolf));
		coordinator.State.DecidedReason.Should().Be(AlreadyDecidedReason.WerewolfControlShortcut);
		coordinator.EvaluationBlocksLobbyExit.Should().BeTrue();
	}

	[Fact]
	public void Dispose_CancelsOutstandingWorkAndPreventsLatePublicationAndPersistence()
	{
		var pump = new ControlledContinuationPump();
		pump.Run(() =>
		{
			var evaluator = new ControlledEvaluator();
			var local = new RecordingLocalStore(bytes: null);
			var coordinator = new LobbyEvaluationCoordinator(
				CreateLobby(
					MainRoleType.SimpleWerewolf,
					MainRoleType.SimpleWerewolf,
					MainRoleType.SimpleWerewolf,
					MainRoleType.SimpleVillager,
					MainRoleType.SimpleVillager),
				new RecordingByteSource(bytes: null),
				local,
				evaluator,
				TimeProvider.System);
			var transitions = 0;
			coordinator.StateChanged += (_, _) => transitions++;
			coordinator.TryRequestLobbyExit().Should().BeFalse();
			pump.Drain();
			var call = evaluator.NextCallAsync().GetAwaiter().GetResult();

			coordinator.Dispose();
			call.CancellationToken.IsCancellationRequested.Should().BeTrue();
			call.Complete(new AlreadyDecidedTerminalEvaluation(
				new SingleFactionGameResult(Faction.Werewolf),
				AlreadyDecidedReason.WerewolfControlShortcut));
			pump.Drain();

			transitions.Should().Be(0);
			coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
			local.Writes.Should().BeEmpty();
			coordinator.TryRequestLobbyExit().Should().BeFalse();
		});
	}

	[Fact]
	public async Task LocalHit_PreventsFallbackAndUsesTheSameProvenanceFreeState()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var record = AlreadyDecidedRecord(lobby.CreateSimulationScenario());
		var local = new RecordingLocalStore(DocumentBytes(record));
		var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			new RecordingByteSource("not-json"u8.ToArray()),
			local,
			evaluator,
			TimeProvider.System);

		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		coordinator.State.Identity.Should().Be(record.CompatibilityIdentity);
		coordinator.State.DecidedGameResult.Should().Be(record.GameResult);
		coordinator.State.DecidedReason.Should().Be(record.Reason);
		coordinator.State.Should().NotBeNull();
		local.ReadCount.Should().Be(1);
		evaluator.CallCount.Should().Be(0);
	}

	[Fact]
	public async Task QuietPeriod_UsesInjectedClockAndStartsFallbackAtExactlyFiveHundredMilliseconds()
	{
		var clock = new ManualTimeProvider();
		var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());
		using var coordinator = new LobbyEvaluationCoordinator(
			CreateLobby(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager),
			new RecordingByteSource(bytes: null),
			new RecordingLocalStore(bytes: null),
			evaluator,
			clock);
		await WaitUntilAsync(() => coordinator.State.Kind == LobbyEvaluationStateKind.Pending);

		clock.Advance(TimeSpan.FromMilliseconds(499));
		evaluator.CallCount.Should().Be(0);
		clock.Advance(TimeSpan.FromMilliseconds(1));
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.CouldNotEvaluate);

		evaluator.CallCount.Should().Be(1);
		coordinator.TryRequestLobbyExit().Should().BeTrue();
	}

	[Fact]
	public void ScenarioChange_PreventsLateResultFromPublishingOrPersisting()
	{
		var pump = new ControlledContinuationPump();
		pump.Run(() =>
		{
			var lobby = CreateLobby(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager);
			var evaluator = new ControlledEvaluator();
			var local = new RecordingLocalStore(bytes: null);
			using var coordinator = new LobbyEvaluationCoordinator(
				lobby,
				new RecordingByteSource(bytes: null),
				local,
				evaluator,
				TimeProvider.System);
			coordinator.TryRequestLobbyExit().Should().BeFalse();
			pump.Drain();
			var first = evaluator.NextCallAsync().GetAwaiter().GetResult();

			lobby.DecrementRole(MainRoleType.SimpleWerewolf);
			lobby.IncrementRole(MainRoleType.SimpleVillager);
			var currentIdentity = coordinator.State.Identity;
			coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
			currentIdentity.Should().NotBe(first.Identity);
			first.CancellationToken.IsCancellationRequested.Should().BeTrue();
			coordinator.TryRequestLobbyExit().Should().BeFalse(
				"the atomic exit decision must apply to the new pending snapshot");
			pump.Drain();
			var current = evaluator.NextCallAsync().GetAwaiter().GetResult();

			first.Complete(new AlreadyDecidedTerminalEvaluation(
				new SingleFactionGameResult(Faction.Werewolf),
				AlreadyDecidedReason.WerewolfControlShortcut));
			pump.Drain();

			coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
			coordinator.State.Identity.Should().Be(currentIdentity);
			local.Writes.Should().BeEmpty();
			current.Complete(new CouldNotEvaluateLobbyEvaluation());
			pump.Drain();
			coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.CouldNotEvaluate);
		});
	}

	[Fact]
	public async Task RetryCurrent_IsAcceptedOnlyForCurrentFailureAndImmediatelyReturnsToPending()
	{
		var evaluator = new QueueEvaluator(
			new CouldNotEvaluateLobbyEvaluation(),
			new AlreadyDecidedTerminalEvaluation(
				new SingleFactionGameResult(Faction.Werewolf),
				AlreadyDecidedReason.WerewolfControlShortcut));
		using var coordinator = new LobbyEvaluationCoordinator(
			CreateLobby(
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleWerewolf,
				MainRoleType.SimpleVillager,
				MainRoleType.SimpleVillager),
			new RecordingByteSource(bytes: null),
			new RecordingLocalStore(bytes: null),
			evaluator,
			TimeProvider.System);

		coordinator.RetryCurrent().Should().BeFalse();
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.CouldNotEvaluate);
		var failedIdentity = coordinator.State.Identity;

		coordinator.RetryCurrent().Should().BeTrue();
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.State.Identity.Should().Be(failedIdentity);
		coordinator.RetryCurrent().Should().BeFalse();
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		evaluator.CallCount.Should().Be(2);
	}

	[Fact]
	public async Task TryRequestLobbyExit_PendingMissAcceleratesFallbackButDoesNotPermitExit()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var evaluator = new RecordingEvaluator(new AlreadyDecidedTerminalEvaluation(
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut));
		var local = new RecordingLocalStore(bytes: null);
		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			new RecordingByteSource(bytes: null),
			local,
			evaluator,
			TimeProvider.System);

		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.TryRequestLobbyExit().Should().BeFalse(
			"a pending request accelerates fallback but cannot skip it");
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		evaluator.CallCount.Should().Be(1);
		local.Writes.Should().ContainSingle();
		var persisted = TerminalLobbyCache.ReadDocument(local.Writes.Single());
		persisted.IsUsable.Should().BeTrue();
		TerminalLobbyCache.TryGet(
			persisted.Document!,
			coordinator.State.Identity!,
			out var record).Should().BeTrue();
		record.Should().BeOfType<AlreadyDecidedTerminalCacheRecord>();
		coordinator.TryRequestLobbyExit().Should().BeFalse(
			"already-decided evaluation blocks Lobby Exit");
	}

	[Fact]
	public async Task BundledHit_UsesFrozenLogicalNameAndPreventsLocalLookupAndFallback()
	{
		var lobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var record = AlreadyDecidedRecord(lobby.CreateSimulationScenario());
		var bundled = new RecordingByteSource(DocumentBytes(record));
		var local = new RecordingLocalStore(DocumentBytes(record));
		var evaluator = new RecordingEvaluator(new CouldNotEvaluateLobbyEvaluation());

		using var coordinator = new LobbyEvaluationCoordinator(
			lobby,
			bundled,
			local,
			evaluator,
			TimeProvider.System);
		await WaitForStateAsync(coordinator, LobbyEvaluationStateKind.AlreadyDecided);

		coordinator.State.Identity.Should().Be(record.CompatibilityIdentity);
		coordinator.State.DecidedGameResult.Should().Be(record.GameResult);
		coordinator.State.DecidedReason.Should().Be(record.Reason);
		coordinator.State.BlocksLobbyExit.Should().BeTrue();
		bundled.LogicalNames.Should().Equal(LobbyEvaluationCoordinator.BundledCacheLogicalName);
		local.ReadCount.Should().Be(0);
		evaluator.CallCount.Should().Be(0);
	}

	[Fact]
	public void Construction_AppliesSupportGatesAndBuildsAlreadyDecidedIdentityWithoutCacheability()
	{
		using var invalid = CreateCoordinator(LobbySetupMetadataFixture.DefaultState());
		invalid.State.Kind.Should().Be(LobbyEvaluationStateKind.NotApplicable);
		invalid.State.BlocksLobbyExit.Should().BeFalse();

		var appUnsupportedLobby = CreateLobby(
			MainRoleType.BigBadWolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		using var appUnsupported = CreateCoordinator(appUnsupportedLobby);
		appUnsupported.State.Kind.Should().Be(LobbyEvaluationStateKind.NotApplicable);
		appUnsupported.State.Identity.Should().BeNull();
		appUnsupported.State.BlocksLobbyExit.Should().BeFalse();

		var decidedLobby = CreateLobby(
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleWerewolf,
			MainRoleType.SimpleVillager,
			MainRoleType.SimpleVillager);
		var scenario = decidedLobby.CreateSimulationScenario();
		SimulationScenarioClassifier.Classify(scenario).Cacheability.Should().BeNull();
		using var decided = CreateCoordinator(decidedLobby);

		decided.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		decided.State.Identity.Should().Be(new SimulationCompatibilityIdentity(
			scenario.ToCanonical(),
			SimulatorProfile.Active.Identity));
		decided.State.BlocksLobbyExit.Should().BeTrue();
	}

	private static LobbyEvaluationCoordinator CreateCoordinator(LobbySetupState lobby) =>
		new(
			lobby,
			EmptyTerminalLobbyCacheByteSource.Instance,
			new InMemoryTerminalLobbyCacheStore(),
			DisabledLobbyTerminalEvaluator.Instance,
			TimeProvider.System);

	private static LobbySetupState CreateLobby(params MainRoleType[] roles)
	{
		var lobby = LobbySetupMetadataFixture.StateWithRoles(roles.Distinct().ToArray());
		for (var index = 0; index < 5; index++)
		{
			lobby.AddPlayer($"Player {index + 1}");
		}

		foreach (var role in roles)
		{
			lobby.IncrementRole(role);
		}

		return lobby;
	}

	private static void ChangeToVillagerMajority(LobbySetupState lobby)
	{
		lobby.DecrementRole(MainRoleType.SimpleWerewolf);
		lobby.IncrementRole(MainRoleType.SimpleVillager);
	}

	private static void ChangeToFourWerewolves(LobbySetupState lobby)
	{
		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		lobby.DecrementRole(MainRoleType.SimpleVillager);
	}

	private static AlreadyDecidedTerminalCacheRecord AlreadyDecidedRecord(
		SimulationScenario scenario) =>
		new(
			new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				SimulatorProfile.Active.Identity),
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut);

	private static byte[] DocumentBytes(params TerminalLobbyCacheRecord[] records) =>
		TerminalLobbyCache.Write(TerminalLobbyCache.CreateDocument(records));

	private static TerminalCacheGameResultFrequency[] AggregateRows(
		int denominator,
		int villager,
		int werewolf) =>
	[
		new(new SingleFactionGameResult(Faction.Villager), villager, denominator),
		new(new SingleFactionGameResult(Faction.Werewolf), werewolf, denominator),
		new(new NoWinnerGameResult(), 0, denominator)
	];

	private static TerminalCacheTurnWindowFrequency[] AggregateCells(
		int denominator,
		int villager,
		int werewolf,
		bool turnOneOnly) =>
	[
		new(new SingleFactionGameResult(Faction.Villager), 1, VictoryCheckWindow.Dawn,
			villager, denominator),
		new(new SingleFactionGameResult(Faction.Werewolf), turnOneOnly ? 1 : 2,
			VictoryCheckWindow.PreNight, werewolf, denominator)
	];

	private static Task WaitForStateAsync(
		LobbyEvaluationCoordinator coordinator,
		LobbyEvaluationStateKind expected)
	{
		if (coordinator.State.Kind == expected)
		{
			return Task.CompletedTask;
		}

		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		EventHandler? handler = null;
		handler = (_, _) =>
		{
			if (coordinator.State.Kind != expected)
			{
				return;
			}

			coordinator.StateChanged -= handler;
			completion.TrySetResult();
		};
		coordinator.StateChanged += handler;
		if (coordinator.State.Kind == expected)
		{
			coordinator.StateChanged -= handler;
			completion.TrySetResult();
		}

		return completion.Task.WaitAsync(TimeSpan.FromSeconds(5));
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		for (var attempt = 0; attempt < 100 && !condition(); attempt++)
		{
			await Task.Yield();
		}
		condition().Should().BeTrue();
	}

	private sealed class ControlledContinuationPump : SynchronizationContext
	{
		private readonly object _sync = new();
		private readonly Queue<(SendOrPostCallback Callback, object? State)> _callbacks = new();

		public override SynchronizationContext CreateCopy() => this;

		public override void Post(SendOrPostCallback callback, object? state)
		{
			lock (_sync)
			{
				_callbacks.Enqueue((callback, state));
			}
		}

		public void Run(Action action)
		{
			var previous = Current;
			SetSynchronizationContext(this);
			try
			{
				action();
			}
			finally
			{
				SetSynchronizationContext(previous);
			}
		}

		public void Drain() => Run(() =>
		{
			while (true)
			{
				(SendOrPostCallback Callback, object? State) next;
				lock (_sync)
				{
					if (_callbacks.Count == 0)
					{
						return;
					}
					next = _callbacks.Dequeue();
				}
				next.Callback(next.State);
			}
		});
	}

	private sealed class RecordingByteSource(ReadOnlyMemory<byte>? bytes)
		: ITerminalLobbyCacheByteSource
	{
		public List<string> LogicalNames { get; } = [];

		public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
			string logicalName,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			LogicalNames.Add(logicalName);
			return ValueTask.FromResult(bytes);
		}
	}

	private sealed class ThrowingByteSource : ITerminalLobbyCacheByteSource
	{
		public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
			string logicalName,
			CancellationToken cancellationToken = default) =>
			ValueTask.FromException<ReadOnlyMemory<byte>?>(
				new IOException("injected bundled read failure"));
	}

	private sealed class ControlledByteSource : ITerminalLobbyCacheByteSource
	{
		private readonly Queue<ControlledRead> _reads = new();
		private readonly SemaphoreSlim _available = new(0);
		private int _readCount;

		public int ReadCount => Volatile.Read(ref _readCount);
		public CancellationToken LastCancellationToken { get; private set; }

		public async ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
			string logicalName,
			CancellationToken cancellationToken = default)
		{
			Interlocked.Increment(ref _readCount);
			LastCancellationToken = cancellationToken;
			var read = new ControlledRead();
			lock (_reads)
			{
				_reads.Enqueue(read);
			}
			_available.Release();
			return await read.Result.Task.WaitAsync(cancellationToken);
		}

		public async Task<ControlledRead> NextReadAsync()
		{
			await _available.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
			lock (_reads)
			{
				return _reads.Dequeue();
			}
		}
	}

	private sealed class ControlledRead
	{
		public TaskCompletionSource<ReadOnlyMemory<byte>?> Result { get; } =
			new();

		public void Complete(ReadOnlyMemory<byte>? bytes) => Result.TrySetResult(bytes);
	}

	private sealed class ControlledReadLocalStore : ILocalTerminalLobbyCacheStore
	{
		private readonly Queue<ControlledRead> _reads = new();
		private readonly SemaphoreSlim _available = new(0);

		public async ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
			CancellationToken cancellationToken = default)
		{
			var read = new ControlledRead();
			lock (_reads)
			{
				_reads.Enqueue(read);
			}
			_available.Release();
			return await read.Result.Task;
		}

		public ValueTask<ILocalTerminalLobbyCacheWrite> StageWriteAsync(
			ReadOnlyMemory<byte> bytes,
			CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("The blocked read must not progress to a write.");

		public async Task<ControlledRead> NextReadAsync()
		{
			await _available.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
			lock (_reads)
			{
				return _reads.Dequeue();
			}
		}
	}

	private sealed class ControlledCommitLocalStore : ILocalTerminalLobbyCacheStore
	{
		private readonly Queue<ControlledCommitWrite> _writes = new();
		private readonly SemaphoreSlim _available = new(0);

		public ReadOnlyMemory<byte>? CommittedBytes { get; private set; }

		public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
			CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);

		public ValueTask<ILocalTerminalLobbyCacheWrite> StageWriteAsync(
			ReadOnlyMemory<byte> bytes,
			CancellationToken cancellationToken = default)
		{
			var write = new ControlledCommitWrite(
				bytes,
				value => CommittedBytes = value.ToArray());
			lock (_writes)
			{
				_writes.Enqueue(write);
			}
			_available.Release();
			return ValueTask.FromResult<ILocalTerminalLobbyCacheWrite>(write);
		}

		public async Task<ControlledCommitWrite> NextWriteAsync()
		{
			await _available.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
			lock (_writes)
			{
				return _writes.Dequeue();
			}
		}
	}

	private sealed class ControlledStageLocalStore : ILocalTerminalLobbyCacheStore
	{
		private readonly TaskCompletionSource _stageStarted =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _releaseStage =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _stagedWriteDisposed =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task StageStarted => _stageStarted.Task;
		public Task StagedWriteDisposed => _stagedWriteDisposed.Task;
		public int CommitAttemptCount { get; private set; }

		public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
			CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);

		public async ValueTask<ILocalTerminalLobbyCacheWrite> StageWriteAsync(
			ReadOnlyMemory<byte> bytes,
			CancellationToken cancellationToken = default)
		{
			_stageStarted.TrySetResult();
			await _releaseStage.Task;
			return new RecordingWrite(
				() => CommitAttemptCount++,
				() => _stagedWriteDisposed.TrySetResult());
		}

		public void ReleaseStage() => _releaseStage.TrySetResult();
	}

	private sealed class ControlledCommitWrite(
		ReadOnlyMemory<byte> bytes,
		Action<ReadOnlyMemory<byte>> commit) : ILocalTerminalLobbyCacheWrite
	{
		private readonly ManualResetEventSlim _release = new();
		private readonly TaskCompletionSource _boundary =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _disposed =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task CommitBoundary => _boundary.Task;
		public Task Disposed => _disposed.Task;

		public bool TryCommit(Func<Action, bool> commitIfAuthorized)
		{
			_boundary.TrySetResult();
			_release.Wait();
			return commitIfAuthorized(() => commit(bytes));
		}

		public void ReleaseCommitBoundary() => _release.Set();

		public ValueTask DisposeAsync()
		{
			_release.Dispose();
			_disposed.TrySetResult();
			return ValueTask.CompletedTask;
		}
	}

	private sealed class RecordingLocalStore(ReadOnlyMemory<byte>? bytes)
		: ILocalTerminalLobbyCacheStore
	{
		public ReadOnlyMemory<byte>? Bytes { get; set; } = bytes;
		public int ReadCount { get; private set; }
		public List<byte[]> Writes { get; } = [];

		public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ReadCount++;
			return ValueTask.FromResult(Bytes);
		}

		public ValueTask<ILocalTerminalLobbyCacheWrite> StageWriteAsync(
			ReadOnlyMemory<byte> value,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			return ValueTask.FromResult<ILocalTerminalLobbyCacheWrite>(
				new RecordingWrite(() => Writes.Add(value.ToArray())));
		}
	}

	private sealed class ThrowingLocalStore : ILocalTerminalLobbyCacheStore
	{
		public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
			CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);

		public ValueTask<ILocalTerminalLobbyCacheWrite> StageWriteAsync(
			ReadOnlyMemory<byte> bytes,
			CancellationToken cancellationToken = default) =>
			ValueTask.FromException<ILocalTerminalLobbyCacheWrite>(
				new IOException("injected persistence failure"));
	}

	private sealed class ReadFailingLocalStore : ILocalTerminalLobbyCacheStore
	{
		public List<byte[]> Writes { get; } = [];

		public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
			CancellationToken cancellationToken = default) =>
			ValueTask.FromException<ReadOnlyMemory<byte>?>(
				new IOException("injected local read failure"));

		public ValueTask<ILocalTerminalLobbyCacheWrite> StageWriteAsync(
			ReadOnlyMemory<byte> bytes,
			CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<ILocalTerminalLobbyCacheWrite>(
				new RecordingWrite(() => Writes.Add(bytes.ToArray())));
	}

	private sealed class RecordingWrite(
		Action commit,
		Action? dispose = null) : ILocalTerminalLobbyCacheWrite
	{
		private bool _completed;

		public bool TryCommit(Func<Action, bool> commitIfAuthorized)
		{
			ObjectDisposedException.ThrowIf(_completed, this);
			var committed = false;
			var authorized = commitIfAuthorized(() =>
			{
				commit();
				committed = true;
			});
			authorized.Should().Be(committed);
			_completed = true;
			return committed;
		}

		public ValueTask DisposeAsync()
		{
			_completed = true;
			dispose?.Invoke();
			return ValueTask.CompletedTask;
		}
	}

	private sealed class RecordingEvaluator(LobbyEvaluationResult result)
		: ILobbyTerminalEvaluator
	{
		public int CallCount { get; private set; }
		public List<LobbyEvaluationDepth> Depths { get; } = [];

		public Task<LobbyEvaluationResult> EvaluateAsync(
			SimulationScenario scenario,
			LobbyEvaluationDepth depth,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			CallCount++;
			Depths.Add(depth);
			return Task.FromResult(result);
		}
	}

	private sealed class QueueEvaluator(params LobbyEvaluationResult[] results)
		: ILobbyTerminalEvaluator
	{
		private readonly Queue<LobbyEvaluationResult> _results = new(results);
		public int CallCount { get; private set; }

		public Task<LobbyEvaluationResult> EvaluateAsync(
			SimulationScenario scenario,
			LobbyEvaluationDepth depth,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			CallCount++;
			return Task.FromResult(_results.Dequeue());
		}
	}

	private sealed class ControlledEvaluator : ILobbyTerminalEvaluator
	{
		private readonly Queue<ControlledCall> _calls = new();
		private readonly SemaphoreSlim _available = new(0);
		public int CallCount { get; private set; }

		public async Task<LobbyEvaluationResult> EvaluateAsync(
			SimulationScenario scenario,
			LobbyEvaluationDepth depth,
			CancellationToken cancellationToken = default)
		{
			CallCount++;
			var classification = SimulationScenarioClassifier.Classify(scenario);
			var identity = new SimulationCompatibilityIdentity(
				scenario.ToCanonical(),
				classification.SimulatorSupport!.Profile.Identity);
			var call = new ControlledCall(identity, cancellationToken);
			lock (_calls)
			{
				_calls.Enqueue(call);
			}
			_available.Release();
			return await call.Result.Task;
		}

		public async Task<ControlledCall> NextCallAsync()
		{
			await _available.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
			lock (_calls)
			{
				return _calls.Dequeue();
			}
		}
	}

	private sealed record UnexpectedTerminalEvaluation : TerminalLobbyEvaluation;

	private sealed class ReentrantCancellationEvaluator(Func<bool> reenter)
		: ILobbyTerminalEvaluator
	{
		private readonly TaskCompletionSource<LobbyEvaluationResult> _firstResult =
			new();
		private readonly TaskCompletionSource _firstCallStarted =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _callbackEntered =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource _callbackRelease =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task FirstCallStarted => _firstCallStarted.Task;
		public Task CallbackEntered => _callbackEntered.Task;
		public bool? ReentrantExitResult { get; private set; }
		public int CallCount { get; private set; }

		public async Task<LobbyEvaluationResult> EvaluateAsync(
			SimulationScenario scenario,
			LobbyEvaluationDepth depth,
			CancellationToken cancellationToken = default)
		{
			CallCount++;
			if (CallCount > 1)
			{
				return new CouldNotEvaluateLobbyEvaluation();
			}

			var registration = cancellationToken.Register(() =>
			{
				_callbackEntered.TrySetResult();
				ReentrantExitResult = reenter();
				_callbackRelease.Task.GetAwaiter().GetResult();
				throw new InvalidOperationException("injected cancellation callback failure");
			});
			_firstCallStarted.TrySetResult();
			try
			{
				return await _firstResult.Task;
			}
			finally
			{
				registration.Unregister();
			}
		}

		public void CompleteFirstCall() =>
			_firstResult.TrySetResult(new CouldNotEvaluateLobbyEvaluation());

		public void ReleaseCancellationCallback() =>
			_callbackRelease.TrySetResult();
	}

	private sealed class ControlledCall(
		SimulationCompatibilityIdentity identity,
		CancellationToken cancellationToken)
	{
		public SimulationCompatibilityIdentity Identity { get; } = identity;
		public CancellationToken CancellationToken { get; } = cancellationToken;
		public TaskCompletionSource<LobbyEvaluationResult> Result { get; } =
			new();

		public void Complete(LobbyEvaluationResult result) => Result.TrySetResult(result);
	}
}
