using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Components;
using Werewolves.Client.Components.Pages;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Testing;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public class RoleSelectionEvaluationTests
{
	[Fact]
	public void SupportedSetup_RendersPendingSummaryAfterRoleGroupsAndKeepsStartActivatable()
	{
		using var context = CreateContext(new NeverCompletingByteSource());
		SeedValidLobby(context.Services.GetRequiredService<LobbySetupState>());

		var cut = context.RenderModeratorComponent<RoleSelectionPage>();

		var panel = cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationPanel));
		panel.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_Pending);
		var start = cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame));
		start.HasAttribute("disabled").Should().BeFalse();
		var mainChildren = cut.Find("main").Children.ToList();
		var lastRoleGroupIndex = mainChildren.FindLastIndex(element => element.ClassList.Contains("ww-role-group"));
		var panelIndex = mainChildren.FindIndex(
			element => element.GetAttribute("data-testid") == ModeratorUiTestIds.LobbyEvaluationPanel);
		panelIndex.Should().BeGreaterThan(lastRoleGroupIndex);
		panelIndex.Should().BeLessThan(
			mainChildren.FindIndex(element => element.GetAttribute("data-testid") == ModeratorUiTestIds.RoleSelectionActionBar));
		cut.FindAll("[role='dialog']").Should().BeEmpty();
	}

	[Fact]
	public async Task PendingStartAttempt_AtomicallyAcceleratesFallbackButNeverStartsOrNavigates()
	{
		var evaluator = new ControlledEvaluator();
		using var context = CreateContext(EmptyTerminalLobbyCacheByteSource.Instance, evaluator);
		SeedValidLobby(context.Services.GetRequiredService<LobbySetupState>());
		var starts = 0;
		var cut = context.RenderModeratorComponent<RoleSelectionPage>(parameters => parameters
			.Add(component => component.OnStartGame, EventCallback.Factory.Create(this, () => starts++)));

		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();
		await evaluator.Started.WaitAsync(TimeSpan.FromSeconds(5));

		starts.Should().Be(0);
		context.Services.GetRequiredService<LobbyEvaluationCoordinator>()
			.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		var status = cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationStatus));
		status.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_PendingBlock);
		status.GetAttribute("role").Should().Be("status");
		status.GetAttribute("aria-live").Should().Be("polite");
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame))
			.HasAttribute("disabled").Should().BeFalse();
	}

	[Fact]
	public async Task StartHandler_RechecksOrdinaryValidationBeforeConsultingTheLiveGate()
	{
		using var context = CreateContext(new NeverCompletingByteSource());
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		SeedValidLobby(lobby);
		var starts = 0;
		var cut = context.RenderModeratorComponent<RoleSelectionPage>(parameters => parameters
			.Add(component => component.OnStartGame, EventCallback.Factory.Create(this, () => starts++)));
		lobby.DecrementRole(MainRoleType.SimpleVillager);
		cut.WaitForAssertion(() => cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame))
			.HasAttribute("disabled").Should().BeTrue());
		var start = cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame));

		await start.TriggerEventAsync(
			ClientTestReferences.Html.Events.Click,
			new MouseEventArgs());

		starts.Should().Be(0);
		cut.Find("[role='alert']").TextContent.Should().NotBeNullOrWhiteSpace();
	}

	[Fact]
	public void AlreadyDecidedStartAttempt_IsAtomicallyBlockedAndAnnounced()
	{
		var record = new AlreadyDecidedTerminalCacheRecord(
			CreateIdentity(villagers: 2, werewolves: 3),
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut);
		using var context = CreateContext(new ImmediateByteSource(record));
		SeedValidLobby(context.Services.GetRequiredService<LobbySetupState>(), villagers: 2, werewolves: 3);
		var starts = 0;
		var cut = context.RenderModeratorComponent<RoleSelectionPage>(parameters => parameters
			.Add(component => component.OnStartGame, EventCallback.Factory.Create(this, () => starts++)));
		cut.WaitForAssertion(() =>
			context.Services.GetRequiredService<LobbyEvaluationCoordinator>()
				.State.Kind.Should().Be(LobbyEvaluationStateKind.AlreadyDecided));

		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();

		starts.Should().Be(0);
		cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationStatus))
			.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_AlreadyDecidedBlock);
	}

	[Fact]
	public void DegenerateStartAttempt_IsAtomicallyBlockedAndAnnounced()
	{
		var villager = new SingleFactionGameResult(Faction.Villager);
		var werewolf = new SingleFactionGameResult(Faction.Werewolf);
		var noWinner = new NoWinnerGameResult();
		var record = new DegenerateTerminalCacheRecord(
			CreateIdentity(),
			[
				new(villager, 750, 1_000),
				new(werewolf, 250, 1_000),
				new(noWinner, 0, 1_000)
			],
			[
				new(villager, 1, VictoryCheckWindow.Dawn, 750, 1_000),
				new(werewolf, 1, VictoryCheckWindow.PreNight, 250, 1_000)
			]);
		using var context = CreateContext(new ImmediateByteSource(record));
		SeedValidLobby(context.Services.GetRequiredService<LobbySetupState>());
		var starts = 0;
		var cut = context.RenderModeratorComponent<RoleSelectionPage>(parameters => parameters
			.Add(component => component.OnStartGame, EventCallback.Factory.Create(this, () => starts++)));
		cut.WaitForAssertion(() =>
			context.Services.GetRequiredService<LobbyEvaluationCoordinator>()
				.State.Kind.Should().Be(LobbyEvaluationStateKind.Degenerate));

		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();

		starts.Should().Be(0);
		cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationStatus))
			.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_DegenerateBlock);
	}

	[Fact]
	public void ProbabilityStartAttempt_AtomicallyPermitsExactlyOneRealGameStartAndRouteTransition()
	{
		var villager = new SingleFactionGameResult(Faction.Villager);
		var werewolf = new SingleFactionGameResult(Faction.Werewolf);
		var noWinner = new NoWinnerGameResult();
		var record = new ProbabilityTerminalCacheRecord(
			CreateIdentity(),
			[
				new(villager, 7_000, 10_000),
				new(werewolf, 3_000, 10_000),
				new(noWinner, 0, 10_000)
			],
			[
				new(villager, 1, VictoryCheckWindow.Dawn, 7_000, 10_000),
				new(werewolf, 2, VictoryCheckWindow.PreNight, 3_000, 10_000)
			]);
		using var context = CreateContext(new ImmediateByteSource(record));
		SeedValidLobby(context.Services.GetRequiredService<LobbySetupState>());
		var cut = context.RenderModeratorComponent<Routes>();
		cut.FindAll("button")
			.Single(button => button.TextContent.Contains(ClientStrings.LobbyRoster_ContinueToRolesButton))
			.Click();
		cut.WaitForAssertion(() => cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationSummary))
			.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_Probability));

		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();

		cut.WaitForAssertion(() =>
		{
			context.Services.GetRequiredService<GameClientManager>().HasActiveSession.Should().BeTrue();
			cut.FindAll(TestId(ModeratorUiTestIds.DashboardShell)).Should().ContainSingle();
			cut.FindAll(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Should().BeEmpty();
		});
	}

	[Theory]
	[InlineData(LobbyEvaluationStateKind.NotApplicable)]
	[InlineData(LobbyEvaluationStateKind.SimulatorUnavailable)]
	public void NonEvaluatedPermittedStates_UseOrdinaryValidationThenAllowStart(
		LobbyEvaluationStateKind expectedState)
	{
		var classify = expectedState == LobbyEvaluationStateKind.NotApplicable
			? new Func<SimulationScenario, LobbyScenarioSupport>(_ => new(
				RulesValid: true,
				AppSupported: false,
				SimulatorProfile: null))
			: _ => new(
				RulesValid: true,
				AppSupported: true,
				SimulatorProfile: null);
		using var context = CreateContext(
			EmptyTerminalLobbyCacheByteSource.Instance,
			classify: classify);
		SeedValidLobby(context.Services.GetRequiredService<LobbySetupState>());
		var starts = 0;
		var cut = context.RenderModeratorComponent<RoleSelectionPage>(parameters => parameters
			.Add(component => component.OnStartGame, EventCallback.Factory.Create(this, () => starts++)));

		context.Services.GetRequiredService<LobbyEvaluationCoordinator>()
			.State.Kind.Should().Be(expectedState);
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();

		starts.Should().Be(1);
		if (expectedState == LobbyEvaluationStateKind.NotApplicable)
		{
			cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationPanel)).Should().BeEmpty();
		}
		else
		{
			cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationSummary))
				.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_SimulatorUnavailable);
		}
	}

	[Fact]
	public async Task CouldNotEvaluate_AllowsStartAndCurrentRetrySynchronouslyReturnsToPendingGate()
	{
		var evaluator = new RetrySequenceEvaluator();
		using var context = CreateContext(EmptyTerminalLobbyCacheByteSource.Instance, evaluator);
		SeedValidLobby(context.Services.GetRequiredService<LobbySetupState>());
		var starts = 0;
		var cut = context.RenderModeratorComponent<RoleSelectionPage>(parameters => parameters
			.Add(component => component.OnStartGame, EventCallback.Factory.Create(this, () => starts++)));

		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();
		cut.WaitForAssertion(() =>
			context.Services.GetRequiredService<LobbyEvaluationCoordinator>()
				.State.Kind.Should().Be(LobbyEvaluationStateKind.CouldNotEvaluate));
		starts.Should().Be(0);

		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();
		starts.Should().Be(1);
		cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationRetry)).Click();
		await evaluator.RetryStarted.WaitAsync(TimeSpan.FromSeconds(5));
		cut.WaitForAssertion(() =>
		{
			context.Services.GetRequiredService<LobbyEvaluationCoordinator>()
				.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
			cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationSummary))
				.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_Pending);
			cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationRetry)).Should().BeEmpty();
		});

		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();

		starts.Should().Be(1);
		cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationStatus))
			.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_PendingBlock);
		evaluator.CallCount.Should().Be(2);
		context.Services.GetRequiredService<LobbyEvaluationCoordinator>()
			.RetryCurrent().Should().BeFalse();
		evaluator.CallCount.Should().Be(2);
	}

	[Fact]
	public void IdentityChange_SynchronouslyRemovesTheOldFailureRetry()
	{
		var evaluator = new RetrySequenceEvaluator();
		using var context = CreateContext(EmptyTerminalLobbyCacheByteSource.Instance, evaluator);
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		SeedValidLobby(lobby);
		var cut = context.RenderModeratorComponent<RoleSelectionPage>();
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();
		cut.WaitForAssertion(() => cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationRetry))
			.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_Retry));

		lobby.DecrementRole(MainRoleType.SimpleVillager);
		lobby.IncrementRole(MainRoleType.SimpleWerewolf);

		cut.WaitForAssertion(() =>
		{
			context.Services.GetRequiredService<LobbyEvaluationCoordinator>()
				.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
			cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationSummary))
				.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_Pending);
			cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationRetry)).Should().BeEmpty();
			cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationStatus)).Should().BeEmpty();
			cut.Markup.Should().NotContain(ClientStrings.LobbyEvaluation_CouldNotEvaluate);
		});
	}

	[Fact]
	public void IdentityChange_SynchronouslyRemovesOldTerminalSummaryAndBlockAnnouncement()
	{
		var oldRecord = new AlreadyDecidedTerminalCacheRecord(
			CreateIdentity(villagers: 2, werewolves: 3),
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut);
		using var context = CreateContext(
			new ImmediateByteSource(oldRecord),
			timeProvider: new ManualTimeProvider());
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		SeedValidLobby(lobby, villagers: 2, werewolves: 3);
		var cut = context.RenderModeratorComponent<RoleSelectionPage>();
		cut.WaitForAssertion(() => cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationSummary))
			.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_AlreadyDecided));
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();
		cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationStatus))
			.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_AlreadyDecidedBlock);

		lobby.DecrementRole(MainRoleType.SimpleWerewolf);
		lobby.IncrementRole(MainRoleType.SimpleVillager);

		cut.WaitForAssertion(() =>
		{
			context.Services.GetRequiredService<LobbyEvaluationCoordinator>()
				.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
			cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationSummary))
				.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_Pending);
			cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationStatus)).Should().BeEmpty();
			cut.Markup.Should().NotContain(ClientStrings.LobbyEvaluation_AlreadyDecided);
		});
	}

	[Fact]
	public async Task LateCompletionForOldIdentity_CannotRestoreItsRenderedFailure()
	{
		var evaluator = new LateCompletionEvaluator();
		var time = new ManualTimeProvider();
		using var context = CreateContext(
			EmptyTerminalLobbyCacheByteSource.Instance,
			evaluator,
			timeProvider: time);
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		SeedValidLobby(lobby);
		var cut = context.RenderModeratorComponent<RoleSelectionPage>();

		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();
		await evaluator.FirstStarted.WaitAsync(TimeSpan.FromSeconds(5));
		lobby.DecrementRole(MainRoleType.SimpleVillager);
		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		evaluator.CompleteFirst(new CouldNotEvaluateLobbyEvaluation());

		cut.WaitForAssertion(() =>
		{
			context.Services.GetRequiredService<LobbyEvaluationCoordinator>()
				.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
			cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationSummary))
				.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_Pending);
			cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationRetry)).Should().BeEmpty();
			cut.Markup.Should().NotContain(ClientStrings.LobbyEvaluation_CouldNotEvaluate);
		});
	}

	[Fact]
	public void Disposal_UnsubscribesFromCoordinatorStateChangesWithoutDisposingIt()
	{
		using var context = CreateContext(new NeverCompletingByteSource());
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		SeedValidLobby(lobby);
		var coordinator = context.Services.GetRequiredService<LobbyEvaluationCoordinator>();
		var cut = context.RenderModeratorComponent<RoleSelectionPage>();

		cut.Dispose();
		var renderCountAfterDisposal = cut.RenderCount;
		lobby.DecrementRole(MainRoleType.SimpleVillager);
		lobby.IncrementRole(MainRoleType.SimpleWerewolf);

		coordinator.State.Kind.Should().Be(LobbyEvaluationStateKind.Pending);
		coordinator.TryRequestLobbyExit().Should().BeFalse();
		cut.RenderCount.Should().Be(renderCountAfterDisposal);
	}

	private static ModeratorComponentTestContext CreateContext(
		ITerminalLobbyCacheByteSource bundled,
		ILobbyTerminalEvaluator? evaluator = null,
		Func<SimulationScenario, LobbyScenarioSupport>? classify = null,
		TimeProvider? timeProvider = null)
	{
		var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton(bundled);
		context.Services.AddSingleton<ILocalTerminalLobbyCacheStore, InMemoryTerminalLobbyCacheStore>();
		context.Services.AddSingleton<ILobbyTerminalEvaluator>(
			evaluator ?? DisabledLobbyTerminalEvaluator.Instance);
		context.Services.AddSingleton(timeProvider ?? TimeProvider.System);
		if (classify is null)
		{
			context.Services.AddSingleton<LobbyEvaluationCoordinator>();
		}
		else
		{
			context.Services.AddSingleton(sp => new LobbyEvaluationCoordinator(
				sp.GetRequiredService<LobbySetupState>(),
				sp.GetRequiredService<ITerminalLobbyCacheByteSource>(),
				sp.GetRequiredService<ILocalTerminalLobbyCacheStore>(),
				sp.GetRequiredService<ILobbyTerminalEvaluator>(),
				sp.GetRequiredService<TimeProvider>(),
				classify));
		}
		return context;
	}

	private static void SeedValidLobby(
		LobbySetupState lobby,
		int villagers = 3,
		int werewolves = 2)
	{
		foreach (var name in new[] { "Ana", "Bruno", "Catarina", "Diana", "Eduardo" })
		{
			lobby.AddPlayer(name);
		}

		foreach (var role in Enumerable.Repeat(MainRoleType.SimpleVillager, villagers)
			.Concat(Enumerable.Repeat(MainRoleType.SimpleWerewolf, werewolves)))
		{
			lobby.IncrementRole(role);
		}
	}

	private static SimulationCompatibilityIdentity CreateIdentity(
		int villagers = 3,
		int werewolves = 2)
	{
		var scenario = new SimulationScenario(
			5,
			Enumerable.Repeat(MainRoleType.SimpleVillager, villagers)
				.Concat(Enumerable.Repeat(MainRoleType.SimpleWerewolf, werewolves)));
		return new(scenario.ToCanonical(), SimulatorProfile.Active.Identity);
	}

	private static string TestId(string value) => $"[data-testid='{value}']";

	private sealed class NeverCompletingByteSource : ITerminalLobbyCacheByteSource
	{
		public async ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
			string logicalName,
			CancellationToken cancellationToken = default)
		{
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			return null;
		}
	}

	private sealed class ImmediateByteSource : ITerminalLobbyCacheByteSource
	{
		private readonly ReadOnlyMemory<byte> _bytes;

		public ImmediateByteSource(TerminalLobbyCacheRecord record)
		{
			_bytes = TerminalLobbyCache.Write(TerminalLobbyCache.CreateDocument([record]));
		}

		public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
			string logicalName,
			CancellationToken cancellationToken = default) =>
			ValueTask.FromResult<ReadOnlyMemory<byte>?>(_bytes);
	}

	private sealed class ControlledEvaluator : ILobbyTerminalEvaluator
	{
		private readonly TaskCompletionSource _started =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task Started => _started.Task;

		public async Task<LobbyEvaluationResult> EvaluateAsync(
			SimulationScenario scenario,
			CancellationToken cancellationToken = default)
		{
			_started.TrySetResult();
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			return new CouldNotEvaluateLobbyEvaluation();
		}
	}

	private sealed class RetrySequenceEvaluator : ILobbyTerminalEvaluator
	{
		private readonly TaskCompletionSource _retryStarted =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _callCount;

		public int CallCount => _callCount;
		public Task RetryStarted => _retryStarted.Task;

		public async Task<LobbyEvaluationResult> EvaluateAsync(
			SimulationScenario scenario,
			CancellationToken cancellationToken = default)
		{
			var call = Interlocked.Increment(ref _callCount);
			if (call == 1)
			{
				return new CouldNotEvaluateLobbyEvaluation();
			}

			_retryStarted.TrySetResult();
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			return new CouldNotEvaluateLobbyEvaluation();
		}
	}

	private sealed class LateCompletionEvaluator : ILobbyTerminalEvaluator
	{
		private readonly TaskCompletionSource _firstStarted =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<LobbyEvaluationResult> _firstCompletion =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _callCount;

		public Task FirstStarted => _firstStarted.Task;

		public void CompleteFirst(LobbyEvaluationResult result) =>
			_firstCompletion.TrySetResult(result);

		public async Task<LobbyEvaluationResult> EvaluateAsync(
			SimulationScenario scenario,
			CancellationToken cancellationToken = default)
		{
			if (Interlocked.Increment(ref _callCount) == 1)
			{
				_firstStarted.TrySetResult();
				return await _firstCompletion.Task;
			}

			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			return new CouldNotEvaluateLobbyEvaluation();
		}
	}
}
