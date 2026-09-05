using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Components;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Testing;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.GameLogic.Simulation;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Extensions;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Simulation;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public sealed class LobbyExitFailureBunitTests
{
	// Claim: the complete production attempt presents one accessible save error and
	// ordinary Start retries navigate only after persistence succeeds. Evidence:
	// rendered Routes with the shared service graph and a failing storage adapter.
	// Source scans and callback counters cannot prove this; no allowlist is needed.
	[Fact]
	public void ActiveRecoveryWriteFailure_ShowsOneInlineErrorAndOrdinaryRetryReachesDashboard()
	{
		var store = new FailingSaveStore { FailActive = true };
		using var context = CreateContext(store);
		var cut = OpenRoleSelection(context);

		Start(cut);

		AssertSaveError(cut);
		cut.FindAll(TestId(ModeratorUiTestIds.DashboardShell)).Should().BeEmpty();
		Start(cut);
		AssertSaveError(cut);
		store.FailActive = false;

		Start(cut);

		cut.FindAll(TestId(ModeratorUiTestIds.DashboardShell)).Should().ContainSingle();
		cut.FindAll(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Should().BeEmpty();
		cut.FindAll("[role='alert']").Should().BeEmpty();
	}

	[Fact]
	public void ActiveRecoveryWriteFailure_RoleCompositionEditClearsErrorAndUsesOrdinaryValidity()
	{
		var store = new FailingSaveStore { FailActive = true };
		using var context = CreateContext(store);
		var cut = OpenRoleSelection(context);
		Start(cut);
		AssertSaveError(cut);

		var removeLabel = string.Format(ClientStrings.RoleSelection_RemoveRoleAriaFormat,
			MainRoleType.SimpleVillager.GetPublicName());
		cut.FindAll("button").Single(button => button.GetAttribute("aria-label") == removeLabel).Click();

		cut.FindAll("[role='alert']").Should().NotContain(error =>
			error.TextContent.Contains(ClientStrings.RoleSelection_ActiveRecoveryWriteFailed));
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame))
			.HasAttribute("disabled").Should().BeTrue();
		cut.FindAll(TestId(ModeratorUiTestIds.DashboardShell)).Should().BeEmpty();
	}

	[Fact]
	public void ImplicitRoleLockInWriteFailure_StaysOnRoleSelectionWithoutActiveSaveError()
	{
		var store = new FailingSaveStore { FailStaged = true, FailActive = true };
		using var context = CreateContext(store);
		var cut = OpenRoleSelection(context);

		Start(cut);

		cut.FindAll("[role='alert']").Should().BeEmpty();
		cut.FindAll(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Should().ContainSingle();
		cut.FindAll(TestId(ModeratorUiTestIds.DashboardShell)).Should().BeEmpty();
		store.ActiveWriteAttempts.Should().Be(0);
		store.FailStaged = false;

		Start(cut);

		AssertSaveError(cut);
	}

	[Fact]
	public async Task ActiveRecoveryWriteFailure_EquivalentAcceptedReplacementClearsError()
	{
		var store = new FailingSaveStore { FailActive = true };
		using var context = CreateContext(store);
		var cut = OpenRoleSelection(context);
		Start(cut);
		AssertSaveError(cut);
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var accepted = lobby.AcceptedRoleLockIn!;

		await cut.InvokeAsync(() => context.Services.GetRequiredService<GameClientManager>()
			.TryReplaceStagedRoleLockIn(lobby, accepted.Version, RoleLockIn.CreateFromPrintedRoles(
				accepted.Version + 1, lobby.PlayerRoster.Count, lobby.GetSelectedRoles()))
			.Should().BeTrue());

		cut.FindAll("[role='alert']").Should().BeEmpty();
		cut.FindAll(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Should().ContainSingle();
		cut.FindAll(TestId(ModeratorUiTestIds.DashboardShell)).Should().BeEmpty();
	}

	[Fact]
	public void ActiveRecoveryWriteFailure_LeavingRoleSelectionClearsErrorOnReturn()
	{
		var store = new FailingSaveStore { FailActive = true };
		using var context = CreateContext(store);
		var cut = OpenRoleSelection(context);
		Start(cut);
		AssertSaveError(cut);

		cut.FindAll("button").Single(button => button.TextContent == ClientStrings.Common_Back).Click();
		cut.WaitForElement("#player-name");
		cut.FindAll("button").Single(button => button.TextContent.Contains(
			ClientStrings.LobbyRoster_ContinueToRolesButton)).Click();
		cut.WaitForElement(TestId(ModeratorUiTestIds.RoleSelectionStartGame));

		cut.FindAll("[role='alert']").Should().BeEmpty();
		Start(cut);
		AssertSaveError(cut);
	}

	[Fact]
	public void ActiveRecoveryWriteFailure_EvaluationCompletionDoesNotClearErrorOrRetryPersistence()
	{
		var store = new FailingSaveStore { FailActive = true };
		var evaluator = new RetryCompletionEvaluator();
		using var context = CreateContext(store, evaluator);
		var cut = OpenRoleSelection(context);
		Start(cut);
		cut.WaitForElement(TestId(ModeratorUiTestIds.LobbyEvaluationRetry));
		Start(cut);
		AssertSaveError(cut);

		// The dormant FullProbability retry exercises another completion for the
		// same accepted identity. Production screening exposes no retry control.
		cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationRetry)).Click();
		AssertSaveError(cut);
		evaluator.CompleteRetry();
		cut.WaitForElement(TestId(ModeratorUiTestIds.LobbyEvaluationRetry));

		AssertSaveError(cut);
		store.ActiveWriteAttempts.Should().Be(1);
		cut.FindAll(TestId(ModeratorUiTestIds.DashboardShell)).Should().BeEmpty();
	}

	[Fact]
	public void ActiveRecoveryWriteFailure_LaterPendingAttemptReplacesErrorWithCurrentStatus()
	{
		var store = new FailingSaveStore { FailActive = true };
		var evaluator = new RetryCompletionEvaluator();
		using var context = CreateContext(store, evaluator);
		var cut = OpenRoleSelection(context);
		Start(cut);
		cut.WaitForElement(TestId(ModeratorUiTestIds.LobbyEvaluationRetry));
		Start(cut);
		AssertSaveError(cut);
		cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationRetry)).Click();

		Start(cut);

		cut.FindAll("[role='alert']").Should().BeEmpty();
		cut.Find(TestId(ModeratorUiTestIds.LobbyEvaluationStatus)).TextContent
			.Should().Contain(ClientStrings.LobbyEvaluation_PendingBlock);
		store.ActiveWriteAttempts.Should().Be(1);
		cut.FindAll(TestId(ModeratorUiTestIds.DashboardShell)).Should().BeEmpty();
	}

	private static void AssertSaveError(IRenderedComponent<Routes> cut)
	{
		var actionBar = cut.Find(TestId(ModeratorUiTestIds.RoleSelectionActionBar));
		var error = actionBar.QuerySelectorAll("[role='alert']").Should().ContainSingle().Subject;
		error.TextContent.Should().Be(ClientStrings.RoleSelection_ActiveRecoveryWriteFailed);
		cut.FindAll("[role='alert']").Should().ContainSingle();
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame))
			.HasAttribute("disabled").Should().BeFalse();
	}

	private static ModeratorComponentTestContext CreateContext(
		FailingSaveStore store,
		ILobbyTerminalEvaluator? evaluator = null)
	{
		var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IGameSessionSaveStore>(store);
		context.Services.AddSingleton(sp => new LobbyEvaluationCoordinator(
			sp.GetRequiredService<LobbySetupState>(),
			sp.GetRequiredService<ILocalTerminalLobbyCacheStore>(),
			evaluator ?? DisabledLobbyTerminalEvaluator.Instance,
			evaluator is null
				? new LobbyEvaluationSettings(SimulatorCapability.SafetyScreening, LobbyEvaluationDepth.DegenerateScreeningOnly)
				: new LobbyEvaluationSettings(SimulatorCapability.FullProbability, LobbyEvaluationDepth.FullProbability),
			new ManualTimeProvider(),
			(_, _) => new LobbyScenarioSupport(true, true, evaluator is not null)));
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		for (var index = 0; index < 5; index++) lobby.AddPlayer($"Player {index}");
		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		for (var index = 0; index < 4; index++) lobby.IncrementRole(MainRoleType.SimpleVillager);
		return context;
	}

	private static IRenderedComponent<Routes> OpenRoleSelection(ModeratorComponentTestContext context)
	{
		var cut = context.RenderModeratorComponent<Routes>();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();
		cut.WaitForElement("#player-name");
		cut.FindAll("button").Single(button => button.TextContent.Contains(
			ClientStrings.LobbyRoster_ContinueToRolesButton)).Click();
		cut.WaitForElement(TestId(ModeratorUiTestIds.RoleSelectionStartGame));
		return cut;
	}

	private static void Start(IRenderedComponent<Routes> cut) =>
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame)).Click();

	private static string TestId(string value) => $"[data-testid='{value}']";

	private sealed class RetryCompletionEvaluator : ILobbyTerminalEvaluator
	{
		private readonly TaskCompletionSource<LobbyEvaluationResult> _retryCompletion =
			new(TaskCreationOptions.RunContinuationsAsynchronously);
		private int _calls;

		public void CompleteRetry() => _retryCompletion.TrySetResult(new CouldNotEvaluateLobbyEvaluation());

		public Task<LobbyEvaluationResult> EvaluateAsync(
			SimulationScenario scenario,
			SimulatorCapability capability,
			LobbyEvaluationDepth depth,
			CancellationToken cancellationToken = default) =>
			Interlocked.Increment(ref _calls) == 1
				? Task.FromResult<LobbyEvaluationResult>(new CouldNotEvaluateLobbyEvaluation())
				: _retryCompletion.Task.WaitAsync(cancellationToken);
	}

	private sealed class FailingSaveStore : IGameSessionSaveStore
	{
		private string? _payload;
		public bool FailActive { get; set; }
		public bool FailStaged { get; set; }
		public int ActiveWriteAttempts { get; private set; }
		public string? Load() => _payload;
		public void Save(string serializedSession)
		{
			var payload = LocalRecoveryPayloadCodec.Deserialize(serializedSession);
			if (payload is ActiveGameRecoveryPayload)
			{
				ActiveWriteAttempts++;
				if (FailActive) throw new IOException("Injected active recovery save failure.");
			}
			if (FailStaged && payload is StagedLobbyRecoveryPayload)
			{
				throw new IOException("Injected staged recovery save failure.");
			}
			_payload = serializedSession;
		}
		public void Clear() => _payload = null;
	}
}
