using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public sealed class LobbyExitAttemptTests
{
	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void ReplacementAttempt_PreservesEachSuccessfullyDurableCommitment(bool failImplicitWrite)
	{
		var lobby = CreateLobby();
		using var evaluation = new LobbyEvaluationCoordinator(
			lobby, new InMemoryTerminalLobbyCacheStore(), DisabledLobbyTerminalEvaluator.Instance,
			new LobbyEvaluationSettings(SimulatorCapability.SafetyScreening, LobbyEvaluationDepth.DegenerateScreeningOnly),
			new ManualTimeProvider(), (_, _) => new LobbyScenarioSupport(true, true, false));
		var store = new ActiveWriteStore();
		var manager = new GameClientManager(new GameService(), saveStore: store, lobbySetupState: lobby, lobbyEvaluation: evaluation);
		manager.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();
		var previous = lobby.AcceptedRoleLockIn;
		var durable = store.Load();
		lobby.DecrementRole(MainRoleType.SimpleVillager);
		lobby.IncrementRole(MainRoleType.Seer);
		store.FailStagedWrite = failImplicitWrite;

		var outcome = manager.AttemptLobbyExit();

		manager.HasActiveSession.Should().BeFalse();
		lobby.GetSelectedRoles().Should().Contain(MainRoleType.Seer);
		if (failImplicitWrite)
		{
			outcome.Should().BeOfType<LobbyExitOutcome.SetupAcceptanceFailed>();
			lobby.AcceptedRoleLockIn.Should().BeSameAs(previous);
			store.Load().Should().Be(durable);
		}
		else
		{
			outcome.Should().BeOfType<LobbyExitOutcome.ActiveRecoveryWriteFailed>();
			lobby.AcceptedRoleLockIn!.Version.Should().Be(2);
			lobby.AcceptedRoleLockIn.RoleComposition.Select(card => card.PrintedRole).Should().Contain(MainRoleType.Seer);
			var restoredLobby = LobbySetupMetadataFixture.DefaultState();
			var restored = new GameClientManager(new GameService(), saveStore: store, lobbySetupState: restoredLobby);
			restored.HasActiveSession.Should().BeFalse();
			restoredLobby.AcceptedRoleLockIn!.Version.Should().Be(2);
			restoredLobby.GetSelectedRoles().Should().Contain(MainRoleType.Seer);
		}
	}

	[Fact]
	public async Task EachAttemptUsesCurrentIdentity_EquivalentAcceptanceKeepsEvidenceAndChangedDraftRequiresNewScreening()
	{
		var lobby = CreateLobby();
		var evaluator = new ControlledEvaluator();
		using var evaluation = new LobbyEvaluationCoordinator(
			lobby, new InMemoryTerminalLobbyCacheStore(), evaluator,
			new LobbyEvaluationSettings(SimulatorCapability.SafetyScreening, LobbyEvaluationDepth.DegenerateScreeningOnly),
			new ManualTimeProvider());
		var store = new ActiveWriteStore();
		var manager = new GameClientManager(new GameService(), saveStore: store, lobbySetupState: lobby, lobbyEvaluation: evaluation);
		var first = manager.AttemptLobbyExit().Should().BeOfType<LobbyExitOutcome.EvaluationBlocked>().Which;
		manager.AttemptLobbyExit().Should().Be(first);
		var call = await evaluator.NextCallAsync();
		call.SetResult(new ScreeningPassedLobbyEvaluation());
		await WaitForStateAsync(evaluation, LobbyEvaluationStateKind.ScreeningPassed);
		manager.HasActiveSession.Should().BeFalse("evaluation completion cannot start a session");
		manager.AttemptLobbyExit().Should().BeOfType<LobbyExitOutcome.ActiveRecoveryWriteFailed>();

		var replacement = RoleLockIn.CreateFromPrintedRoles(2, lobby.PlayerRoster.Count, lobby.GetSelectedRoles());
		manager.TryReplaceStagedRoleLockIn(lobby, 1, replacement).Should().BeTrue();
		manager.AttemptLobbyExit().Should().BeOfType<LobbyExitOutcome.ActiveRecoveryWriteFailed>();
		evaluation.State.Identity.Should().Be(first.Evaluation.Identity);
		evaluator.CallCount.Should().Be(1);

		lobby.DecrementRole(MainRoleType.SimpleVillager);
		lobby.IncrementRole(MainRoleType.Seer);
		var changed = manager.AttemptLobbyExit().Should().BeOfType<LobbyExitOutcome.EvaluationBlocked>().Which;
		changed.Evaluation.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		changed.Evaluation.Identity.Should().NotBe(first.Evaluation.Identity);
		changed.Evaluation.Identity.Should().Be(SimulatorCapability.SafetyScreening.CreateCompatibilityIdentity(lobby.CreateSimulationScenario()));
		manager.HasActiveSession.Should().BeFalse();
		var next = await evaluator.NextCallAsync();
		next.SetResult(new ScreeningPassedLobbyEvaluation());
		await WaitForStateAsync(evaluation, LobbyEvaluationStateKind.ScreeningPassed);
		manager.HasActiveSession.Should().BeFalse();
		store.FailActiveWrite = false;
		var started = manager.AttemptLobbyExit().Should().BeOfType<LobbyExitOutcome.Started>().Which;
		var session = manager.CurrentSession;
		var durable = store.Load();
		var accepted = lobby.AcceptedRoleLockIn;
		manager.AttemptLobbyExit().Should().Be(new LobbyExitOutcome.AlreadyActive(started.GameId));
		manager.CurrentSession.Should().BeSameAs(session);
		lobby.AcceptedRoleLockIn.Should().BeSameAs(accepted);
		store.Load().Should().Be(durable);
	}

	private static LobbySetupState CreateLobby()
	{
		var lobby = LobbySetupMetadataFixture.DefaultState();
		for (var index = 0; index < 5; index++)
		{
			lobby.AddPlayer($"Player {index}");
		}
		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		for (var index = 0; index < 4; index++)
		{
			lobby.IncrementRole(MainRoleType.SimpleVillager);
		}
		return lobby;
	}

	private static async Task WaitForStateAsync(LobbyEvaluationCoordinator evaluation, LobbyEvaluationStateKind kind)
	{
		var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		void Check(object? sender, EventArgs args) { if (evaluation.State.Kind == kind) reached.TrySetResult(); }
		evaluation.StateChanged += Check;
		try
		{
			Check(null, EventArgs.Empty);
			await reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
		}
		finally { evaluation.StateChanged -= Check; }
	}

	private sealed class ControlledEvaluator : ILobbyTerminalEvaluator
	{
		private readonly System.Threading.Channels.Channel<TaskCompletionSource<LobbyEvaluationResult>> _calls =
			System.Threading.Channels.Channel.CreateUnbounded<TaskCompletionSource<LobbyEvaluationResult>>();
		public int CallCount { get; private set; }
		public Task<LobbyEvaluationResult> EvaluateAsync(SimulationScenario scenario, SimulatorCapability capability,
			LobbyEvaluationDepth depth, CancellationToken cancellationToken = default)
		{
			capability.Should().BeSameAs(SimulatorCapability.SafetyScreening);
			depth.Should().Be(LobbyEvaluationDepth.DegenerateScreeningOnly);
			CallCount++;
			var result = new TaskCompletionSource<LobbyEvaluationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
			_calls.Writer.TryWrite(result);
			return result.Task;
		}
		public async Task<TaskCompletionSource<LobbyEvaluationResult>> NextCallAsync() =>
			await _calls.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
	}

	[Fact]
	public void UnexpectedSaveException_IsNotRelabeledAsActiveRecoveryWriteFailure()
	{
		var lobby = CreateLobby();
		using var evaluation = new LobbyEvaluationCoordinator(
			lobby, new InMemoryTerminalLobbyCacheStore(), DisabledLobbyTerminalEvaluator.Instance,
			new LobbyEvaluationSettings(SimulatorCapability.SafetyScreening, LobbyEvaluationDepth.DegenerateScreeningOnly),
			new ManualTimeProvider(), (_, _) => new LobbyScenarioSupport(true, true, false));
		var store = new ActiveWriteStore { ActiveFailure = new NullReferenceException("programming failure") };
		var manager = new GameClientManager(new GameService(), saveStore: store, lobbySetupState: lobby, lobbyEvaluation: evaluation);

		var attempt = () => manager.AttemptLobbyExit();

		attempt.Should().Throw<NullReferenceException>().Which.Should().BeSameAs(store.ActiveFailure);
		manager.HasActiveSession.Should().BeFalse();
		LocalRecoveryPayloadCodec.Deserialize(store.Load()!).Should().BeOfType<StagedLobbyRecoveryPayload>();
	}

	[Theory]
	[InlineData(MainRoleType.Thief, LobbyConfigurationStep.RoleLockIn)]
	[InlineData(MainRoleType.Actor, LobbyConfigurationStep.ActorSetupCards)]
	[InlineData(MainRoleType.PrejudicedManipulator, LobbyConfigurationStep.PublicGroupPartition)]
	public void RequiredConfiguration_ReturnsExistingStepWithoutStarting(MainRoleType role, LobbyConfigurationStep expected)
	{
		var lobby = LobbySetupMetadataFixture.DefaultState();
		for (var index = 0; index < 5; index++) lobby.AddPlayer($"Player {index}");
		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		lobby.IncrementRole(role);
		for (var index = 0; index < (role == MainRoleType.Thief ? 5 : 3); index++) lobby.IncrementRole(MainRoleType.SimpleVillager);
		using var evaluation = new LobbyEvaluationCoordinator(
			lobby, new InMemoryTerminalLobbyCacheStore(), DisabledLobbyTerminalEvaluator.Instance,
			new LobbyEvaluationSettings(SimulatorCapability.SafetyScreening, LobbyEvaluationDepth.DegenerateScreeningOnly),
			new ManualTimeProvider());
		var manager = new GameClientManager(new GameService(), lobbySetupState: lobby, lobbyEvaluation: evaluation);

		manager.AttemptLobbyExit().Should().Be(new LobbyExitOutcome.ConfigurationRequired(expected));

		manager.HasActiveSession.Should().BeFalse();
		if (role == MainRoleType.Thief) lobby.AcceptedRoleLockIn.Should().BeNull();
		else lobby.AcceptedRoleLockIn.Should().NotBeNull();
	}

	[Fact]
	public async Task ActiveWriteFailure_PreservesSuccessfulImplicitStagingAndOrdinaryRetryStarts()
	{
		var lobby = CreateLobby();
		using var evaluation = new LobbyEvaluationCoordinator(
			lobby, new InMemoryTerminalLobbyCacheStore(), DisabledLobbyTerminalEvaluator.Instance,
			new LobbyEvaluationSettings(SimulatorCapability.SafetyScreening, LobbyEvaluationDepth.DegenerateScreeningOnly),
			new ManualTimeProvider());
		var store = new ActiveWriteStore();
		var manager = new GameClientManager(new GameService(), saveStore: store, lobbySetupState: lobby, lobbyEvaluation: evaluation);
		var resolved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		evaluation.StateChanged += (_, _) => { if (!evaluation.State.BlocksLobbyExit) resolved.TrySetResult(); };
		manager.AttemptLobbyExit().Should().BeOfType<LobbyExitOutcome.EvaluationBlocked>();
		await resolved.Task.WaitAsync(TimeSpan.FromSeconds(5));
		var staged = lobby.AcceptedRoleLockIn;
		var stagedPayload = store.Load();

		manager.AttemptLobbyExit().Should().BeOfType<LobbyExitOutcome.ActiveRecoveryWriteFailed>();
		manager.AttemptLobbyExit().Should().BeOfType<LobbyExitOutcome.ActiveRecoveryWriteFailed>();

		manager.HasActiveSession.Should().BeFalse();
		lobby.AcceptedRoleLockIn.Should().BeSameAs(staged);
		store.Load().Should().Be(stagedPayload);
		LocalRecoveryPayloadCodec.Deserialize(stagedPayload!).Should().BeOfType<StagedLobbyRecoveryPayload>();
		store.FailActiveWrite = false;
		manager.AttemptLobbyExit().Should().BeOfType<LobbyExitOutcome.Started>();
		manager.HasActiveSession.Should().BeTrue();
		manager.CurrentSession!.RoleLockIn.Should().BeSameAs(staged);
		LocalRecoveryPayloadCodec.Deserialize(store.Load()!).Should().BeOfType<ActiveGameRecoveryPayload>();
		var resumed = new GameClientManager(new GameService(), saveStore: store);
		resumed.ActiveGameId.Should().Be(manager.ActiveGameId);
	}

	private sealed class ActiveWriteStore : IGameSessionSaveStore
	{
		private string? _payload;
		public bool FailActiveWrite { get; set; } = true;
		public bool FailStagedWrite { get; set; }
		public Exception ActiveFailure { get; set; } = new IOException("active write failed");
		public string? Load() => _payload;
		public void Clear() => _payload = null;
		public void Save(string payload)
		{
			if (FailStagedWrite && LocalRecoveryPayloadCodec.Deserialize(payload) is StagedLobbyRecoveryPayload)
				throw new IOException("staged write failed");
			if (FailActiveWrite && LocalRecoveryPayloadCodec.Deserialize(payload) is ActiveGameRecoveryPayload)
				throw ActiveFailure;
			_payload = payload;
		}
	}

	[Fact]
	public void ValidSetup_WhenEvaluationPending_AcceptsStagingAndReturnsBlockingIdentity()
	{
		var lobby = CreateLobby();
		using var evaluation = new LobbyEvaluationCoordinator(
			lobby, new InMemoryTerminalLobbyCacheStore(), DisabledLobbyTerminalEvaluator.Instance,
			new LobbyEvaluationSettings(SimulatorCapability.SafetyScreening, LobbyEvaluationDepth.DegenerateScreeningOnly),
			new ManualTimeProvider());
		var manager = new GameClientManager(new GameService(), lobbySetupState: lobby, lobbyEvaluation: evaluation);

		var result = manager.AttemptLobbyExit().Should().BeOfType<LobbyExitOutcome.EvaluationBlocked>().Which;

		result.Evaluation.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		result.Evaluation.Identity.Should().Be(SimulatorCapability.SafetyScreening.CreateCompatibilityIdentity(lobby.CreateSimulationScenario()));
		lobby.AcceptedRoleLockIn.Should().NotBeNull();
		manager.HasActiveSession.Should().BeFalse();
	}

	[Fact]
	public void InvalidSetup_ReturnsRejectionWithoutAcceptingOrStarting()
	{
		var lobby = LobbySetupMetadataFixture.DefaultState();
		using var evaluation = new LobbyEvaluationCoordinator(
			lobby, new InMemoryTerminalLobbyCacheStore(), DisabledLobbyTerminalEvaluator.Instance,
			new LobbyEvaluationSettings(SimulatorCapability.SafetyScreening, LobbyEvaluationDepth.DegenerateScreeningOnly),
			new ManualTimeProvider());
		var manager = new GameClientManager(new GameService(), lobbySetupState: lobby, lobbyEvaluation: evaluation);

		manager.AttemptLobbyExit().Should().BeOfType<LobbyExitOutcome.InvalidSetup>();

		lobby.AcceptedRoleLockIn.Should().BeNull();
		manager.HasActiveSession.Should().BeFalse();
	}
}
