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
		var cells10000 = AggregateCells(10_000, 7_000, 3_000, turnOneOnly: false);
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

			coordinator.State.TerminalRecord.Should().BeEquivalentTo(@case.Record);
			coordinator.EvaluationBlocksLobbyExit.Should().Be(@case.Blocks);
			coordinator.TryRequestLobbyExit().Should().Be(!@case.Blocks);
		}
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

		coordinator.State.TerminalRecord.Should().BeOfType<AlreadyDecidedTerminalCacheRecord>();
		coordinator.EvaluationBlocksLobbyExit.Should().BeTrue();
	}

	[Fact]
	public async Task Dispose_CancelsOutstandingWorkAndPreventsLatePublicationAndPersistence()
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
		var call = await evaluator.NextCallAsync();

		coordinator.Dispose();
		call.CancellationToken.IsCancellationRequested.Should().BeTrue();
		call.Complete(new AlreadyDecidedTerminalEvaluation(
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut));
		await Task.Yield();

		transitions.Should().Be(0);
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		local.Writes.Should().BeEmpty();
		coordinator.TryRequestLobbyExit().Should().BeFalse();
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

		coordinator.State.TerminalRecord.Should().BeEquivalentTo(record);
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
	public async Task ScenarioChange_PreventsLateResultFromPublishingOrPersisting()
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
		var first = await evaluator.NextCallAsync();

		lobby.DecrementRole(MainRoleType.SimpleWerewolf);
		lobby.IncrementRole(MainRoleType.SimpleVillager);
		var currentIdentity = coordinator.State.Identity;
		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		currentIdentity.Should().NotBe(first.Identity);
		first.CancellationToken.IsCancellationRequested.Should().BeTrue();
		coordinator.TryRequestLobbyExit().Should().BeFalse(
			"the atomic exit decision must apply to the new pending snapshot");

		first.Complete(new AlreadyDecidedTerminalEvaluation(
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut));
		await first.Completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
		await Task.Yield();

		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.State.Identity.Should().Be(currentIdentity);
		local.Writes.Should().BeEmpty();
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

		coordinator.State.TerminalRecord.Should().BeEquivalentTo(record);
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

		public ValueTask WriteAsync(
			ReadOnlyMemory<byte> value,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Writes.Add(value.ToArray());
			return ValueTask.CompletedTask;
		}
	}

	private sealed class ThrowingLocalStore : ILocalTerminalLobbyCacheStore
	{
		public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
			CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);

		public ValueTask WriteAsync(
			ReadOnlyMemory<byte> bytes,
			CancellationToken cancellationToken = default) =>
			ValueTask.FromException(new IOException("injected persistence failure"));
	}

	private sealed class RecordingEvaluator(LobbyEvaluationResult result)
		: ILobbyTerminalEvaluator
	{
		public int CallCount { get; private set; }

		public Task<LobbyEvaluationResult> EvaluateAsync(
			SimulationScenario scenario,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			CallCount++;
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

		public Task<LobbyEvaluationResult> EvaluateAsync(
			SimulationScenario scenario,
			CancellationToken cancellationToken = default)
		{
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
			return call.Result.Task;
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

	private sealed class ControlledCall(
		SimulationCompatibilityIdentity identity,
		CancellationToken cancellationToken)
	{
		public SimulationCompatibilityIdentity Identity { get; } = identity;
		public CancellationToken CancellationToken { get; } = cancellationToken;
		public TaskCompletionSource<LobbyEvaluationResult> Result { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource Completed { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public void Complete(LobbyEvaluationResult result)
		{
			Result.TrySetResult(result);
			Completed.TrySetResult();
		}
	}
}
