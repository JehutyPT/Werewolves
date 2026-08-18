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
		using var context = CreateContext(evaluator: new ControlledEvaluator());
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
		using var context = CreateContext(
			evaluator: evaluator,
			depth: LobbyEvaluationDepth.DegenerateScreeningOnly,
			capability: SimulatorCapability.SafetyScreening);
		SeedValidLobby(context.Services.GetRequiredService<LobbySetupState>());
		var starts = 0;
		var cut = context.RenderModeratorComponent<RoleSelectionPage>(parameters => parameters
			.Add(component => component.OnStartGame, EventCallback.Factory.Create(this, () => starts++)));
		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationPanel)).Should().BeEmpty();

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
		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationPanel)).Should().BeEmpty();
	}

	[Fact]
	public async Task PendingStartAttempt_WhenSameCheckCompletes_AnnouncesThatStartIsAvailable()
	{
		var evaluator = new CompletingEvaluator();
		using var context = CreateContext(
			evaluator: evaluator,
			depth: LobbyEvaluationDepth.DegenerateScreeningOnly,
			capability: SimulatorCapability.SafetyScreening);
		SeedValidLobby(context.Services.GetRequiredService<LobbySetupState>());
		var starts = 0;
		var cut = context.RenderModeratorComponent<RoleSelectionPage>(parameters => parameters
			.Add(component => component.OnStartGame, EventCallback.Factory.Create(this, () => starts++)));

		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();
		await evaluator.Started.WaitAsync(TimeSpan.FromSeconds(5));
		cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationStatus))
			.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_PendingBlock);

		evaluator.Complete(new ScreeningPassedLobbyEvaluation());

		cut.WaitForAssertion(() =>
		{
			context.Services.GetRequiredService<LobbyEvaluationCoordinator>()
				.State.Kind.Should().Be(LobbyEvaluationStateKind.ScreeningPassed);
			var status = cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationStatus));
			status.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_CheckComplete);
			status.GetAttribute("role").Should().Be("status");
			status.GetAttribute("aria-live").Should().Be("polite");
		});
		starts.Should().Be(0);

		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();

		cut.WaitForAssertion(() =>
		{
			starts.Should().Be(1);
			cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationStatus)).Should().BeEmpty();
		});
	}

	[Fact]
	public async Task StartHandler_RechecksOrdinaryValidationBeforeConsultingTheLiveGate()
	{
		using var context = CreateContext(evaluator: new ControlledEvaluator());
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
			CreateIdentity(
				villagers: 2,
				werewolves: 3,
				capability: SimulatorCapability.SafetyScreening),
			new SingleFactionGameResult(Faction.Werewolf),
			AlreadyDecidedReason.WerewolfControlShortcut,
			SimulatorCapability.SafetyScreening);
		using var context = CreateContext(
			new SeededLocalStore(record),
			depth: LobbyEvaluationDepth.DegenerateScreeningOnly,
			capability: SimulatorCapability.SafetyScreening);
		SeedValidLobby(context.Services.GetRequiredService<LobbySetupState>(), villagers: 2, werewolves: 3);
		var starts = 0;
		var cut = context.RenderModeratorComponent<RoleSelectionPage>(parameters => parameters
			.Add(component => component.OnStartGame, EventCallback.Factory.Create(this, () => starts++)));
		cut.WaitForAssertion(() =>
			context.Services.GetRequiredService<LobbyEvaluationCoordinator>()
				.State.Kind.Should().Be(LobbyEvaluationStateKind.AlreadyDecided));
		cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationSummary))
			.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_AlreadyDecided);

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
			CreateIdentity(capability: SimulatorCapability.SafetyScreening),
			[
				new(villager, 750, 1_000),
				new(werewolf, 250, 1_000),
				new(noWinner, 0, 1_000)
			],
			[
				new(villager, 1, VictoryCheckWindow.Dawn, 750, 1_000),
				new(werewolf, 1, VictoryCheckWindow.PreNight, 250, 1_000)
			],
			SimulatorCapability.SafetyScreening);
		using var context = CreateContext(
			new SeededLocalStore(record),
			depth: LobbyEvaluationDepth.DegenerateScreeningOnly,
			capability: SimulatorCapability.SafetyScreening);
		SeedValidLobby(context.Services.GetRequiredService<LobbySetupState>());
		var starts = 0;
		var cut = context.RenderModeratorComponent<RoleSelectionPage>(parameters => parameters
			.Add(component => component.OnStartGame, EventCallback.Factory.Create(this, () => starts++)));
		cut.WaitForAssertion(() =>
			context.Services.GetRequiredService<LobbyEvaluationCoordinator>()
				.State.Kind.Should().Be(LobbyEvaluationStateKind.Degenerate));
		cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationSummary))
			.TextContent.Should().Contain(ClientStrings.LobbyEvaluation_Degenerate);

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
			],
			SimulatorCapability.FullProbability);
		using var context = CreateContext(
			new SeededLocalStore(record),
			depth: LobbyEvaluationDepth.FullProbability,
			capability: SimulatorCapability.FullProbability);
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
			? new Func<SimulationScenario, SimulatorCapability, LobbyScenarioSupport>((_, _) => new(
				RulesValid: true,
				AppSupported: false,
				SimulatorSupported: false))
			: (_, _) => new(
				RulesValid: true,
				AppSupported: true,
				SimulatorSupported: false);
		using var context = CreateContext(classify: classify);
		SeedValidLobby(context.Services.GetRequiredService<LobbySetupState>());
		var starts = 0;
		var cut = context.RenderModeratorComponent<RoleSelectionPage>(parameters => parameters
			.Add(component => component.OnStartGame, EventCallback.Factory.Create(this, () => starts++)));

		context.Services.GetRequiredService<LobbyEvaluationCoordinator>()
			.State.Kind.Should().Be(expectedState);
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();

		starts.Should().Be(1);
		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationPanel)).Should().BeEmpty();
	}

	[Fact]
	public void DegenerateScreeningOnly_SimulatorUnavailableStaysRenderlessAndAllowsStart()
	{
		using var context = CreateContext(
			classify: (_, _) => new(
				RulesValid: true,
				AppSupported: true,
				SimulatorSupported: false),
			depth: LobbyEvaluationDepth.DegenerateScreeningOnly,
			capability: SimulatorCapability.SafetyScreening);
		SeedValidLobby(context.Services.GetRequiredService<LobbySetupState>());
		var starts = 0;
		var cut = context.RenderModeratorComponent<RoleSelectionPage>(parameters => parameters
			.Add(component => component.OnStartGame, EventCallback.Factory.Create(this, () => starts++)));

		context.Services.GetRequiredService<LobbyEvaluationCoordinator>()
			.State.Kind.Should().Be(LobbyEvaluationStateKind.SimulatorUnavailable);
		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationPanel)).Should().BeEmpty();

		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();

		starts.Should().Be(1);
	}

	[Fact]
	public void DegenerateScreeningOnly_EvaluationFailureStaysRenderlessAndAllowsNextStartAttempt()
	{
		using var context = CreateContext(
			depth: LobbyEvaluationDepth.DegenerateScreeningOnly,
			capability: SimulatorCapability.SafetyScreening);
		SeedValidLobby(context.Services.GetRequiredService<LobbySetupState>());
		var starts = 0;
		var cut = context.RenderModeratorComponent<RoleSelectionPage>(parameters => parameters
			.Add(component => component.OnStartGame, EventCallback.Factory.Create(this, () => starts++)));

		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();
		cut.WaitForAssertion(() => context.Services.GetRequiredService<LobbyEvaluationCoordinator>()
			.State.Kind.Should().Be(LobbyEvaluationStateKind.CouldNotEvaluate));

		starts.Should().Be(0);
		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationPanel)).Should().BeEmpty();
		cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationRetry)).Should().BeEmpty();

		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();

		starts.Should().Be(1);
	}

	[Fact]
	public async Task CouldNotEvaluate_AllowsStartAndCurrentRetrySynchronouslyReturnsToPendingGate()
	{
		var evaluator = new RetrySequenceEvaluator();
		using var context = CreateContext(
			evaluator: evaluator,
			depth: LobbyEvaluationDepth.FullProbability,
			capability: SimulatorCapability.FullProbability);
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
		using var context = CreateContext(
			evaluator: evaluator,
			depth: LobbyEvaluationDepth.FullProbability,
			capability: SimulatorCapability.FullProbability);
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
			AlreadyDecidedReason.WerewolfControlShortcut,
			SimulatorCapability.FullProbability);
		using var context = CreateContext(
			new SeededLocalStore(oldRecord),
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
			evaluator: evaluator,
			timeProvider: time,
			depth: LobbyEvaluationDepth.FullProbability,
			capability: SimulatorCapability.FullProbability);
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
			cut.FindAll(TestId(ModeratorUiTestIds.LobbyEvaluationStatus)).Should().BeEmpty();
			cut.Markup.Should().NotContain(ClientStrings.LobbyEvaluation_CouldNotEvaluate);
		});
	}

	[Fact]
	public void Disposal_UnsubscribesFromCoordinatorStateChangesWithoutDisposingIt()
	{
		using var context = CreateContext(evaluator: new ControlledEvaluator());
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
		ILocalTerminalLobbyCacheStore? localStore = null,
		ILobbyTerminalEvaluator? evaluator = null,
		Func<SimulationScenario, SimulatorCapability, LobbyScenarioSupport>? classify = null,
		TimeProvider? timeProvider = null,
		LobbyEvaluationDepth depth = LobbyEvaluationDepth.FullProbability,
		SimulatorCapability? capability = null)
	{
		var settings = new LobbyEvaluationSettings(
			capability ?? SimulatorCapability.FullProbability,
			depth);
		var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<ILocalTerminalLobbyCacheStore>(
			localStore ?? new InMemoryTerminalLobbyCacheStore());
		context.Services.AddSingleton<ILobbyTerminalEvaluator>(
			evaluator ?? DisabledLobbyTerminalEvaluator.Instance);
		context.Services.AddSingleton(timeProvider ?? TimeProvider.System);
		if (classify is null)
		{
			context.Services.AddSingleton(sp => new LobbyEvaluationCoordinator(
				sp.GetRequiredService<LobbySetupState>(),
				sp.GetRequiredService<ILocalTerminalLobbyCacheStore>(),
				sp.GetRequiredService<ILobbyTerminalEvaluator>(),
				settings,
				sp.GetRequiredService<TimeProvider>()));
		}
		else
		{
			context.Services.AddSingleton(sp => new LobbyEvaluationCoordinator(
				sp.GetRequiredService<LobbySetupState>(),
				sp.GetRequiredService<ILocalTerminalLobbyCacheStore>(),
				sp.GetRequiredService<ILobbyTerminalEvaluator>(),
				settings,
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
		int werewolves = 2,
		SimulatorCapability? capability = null)
	{
		var scenario = new SimulationScenario(
			5,
			Enumerable.Repeat(MainRoleType.SimpleVillager, villagers)
				.Concat(Enumerable.Repeat(MainRoleType.SimpleWerewolf, werewolves)));
		return new(
			scenario.ToCanonical(),
			(capability ?? SimulatorCapability.FullProbability).Identity);
	}

	private static string TestId(string value) => $"[data-testid='{value}']";

	private sealed class SeededLocalStore : ILocalTerminalLobbyCacheStore
	{
		private readonly InMemoryTerminalLobbyCacheStore _store = new();

		public SeededLocalStore(TerminalLobbyCacheRecord record)
		{
			var bytes = TerminalLobbyCache.Write(TerminalLobbyCache.CreateDocument([record]));
			var staged = _store.StageWriteAsync(bytes).GetAwaiter().GetResult();
			try
			{
				staged.TryCommit(commit =>
				{
					commit();
					return true;
				});
			}
			finally
			{
				staged.DisposeAsync().GetAwaiter().GetResult();
			}
		}

		public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
			CancellationToken cancellationToken = default) =>
			_store.ReadAsync(cancellationToken);

		public ValueTask<ILocalTerminalLobbyCacheWrite> StageWriteAsync(
			ReadOnlyMemory<byte> bytes,
			CancellationToken cancellationToken = default) =>
			_store.StageWriteAsync(bytes, cancellationToken);
	}

	private sealed class ControlledEvaluator : ILobbyTerminalEvaluator
	{
		private readonly TaskCompletionSource _started =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task Started => _started.Task;

		public async Task<LobbyEvaluationResult> EvaluateAsync(
			SimulationScenario scenario,
			SimulatorCapability capability,
			LobbyEvaluationDepth depth,
			CancellationToken cancellationToken = default)
		{
			_started.TrySetResult();
			await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
			return new CouldNotEvaluateLobbyEvaluation();
		}
	}

	private sealed class CompletingEvaluator : ILobbyTerminalEvaluator
	{
		private readonly TaskCompletionSource _started =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private readonly TaskCompletionSource<LobbyEvaluationResult> _completion =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Task Started => _started.Task;

		public void Complete(LobbyEvaluationResult result) =>
			_completion.TrySetResult(result);

		public async Task<LobbyEvaluationResult> EvaluateAsync(
			SimulationScenario scenario,
			SimulatorCapability capability,
			LobbyEvaluationDepth depth,
			CancellationToken cancellationToken = default)
		{
			_started.TrySetResult();
			return await _completion.Task.WaitAsync(cancellationToken);
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
			SimulatorCapability capability,
			LobbyEvaluationDepth depth,
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
			SimulatorCapability capability,
			LobbyEvaluationDepth depth,
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
