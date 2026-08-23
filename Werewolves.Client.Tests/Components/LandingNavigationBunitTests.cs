using Bunit;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Components;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Testing;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Instructions;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public sealed class LandingNavigationBunitTests
{
	[Fact]
	public void EmptyRecovery_RendersLandingBeforeAnyLobbySurface()
	{
		using var context = new ModeratorComponentTestContext();

		var cut = context.RenderModeratorComponent<Routes>();

		cut.FindAll(TestId(ModeratorUiTestIds.LandingShell)).Should().ContainSingle();
	}

	[Fact]
	public void EmptyRecovery_NewGameRendersCurrentLobbyRoster()
	{
		using var context = new ModeratorComponentTestContext();
		var cut = context.RenderModeratorComponent<Routes>();
		var newGameButtons = cut.FindAll(TestId(ModeratorUiTestIds.LandingNewGameButton));

		newGameButtons.Should().ContainSingle();
		newGameButtons.Single().TextContent.Trim().Should().Be(ClientStrings.Landing_NewGameButton);
		newGameButtons.Single().Click();

		cut.FindAll("h1").Should().ContainSingle()
			.Which.TextContent.Trim().Should().Be(ClientStrings.LobbyRoster_Title);
	}

	[Fact]
	public void ActiveRecovery_ContinueRendersExistingDashboardDestination()
	{
		var store = new RecordingSaveStore();
		using (var seedContext = CreateContext(store))
		{
			StartRecoverableSession(seedContext);
		}

		using var recoveredContext = CreateContext(store);
		var cut = recoveredContext.RenderModeratorComponent<Routes>();
		var manager = recoveredContext.Services.GetRequiredService<GameClientManager>();

		manager.HasActiveSession.Should().BeTrue();
		cut.FindAll(TestId(ModeratorUiTestIds.LandingShell)).Should().ContainSingle();
		var continueButtons = cut.FindAll(TestId(ModeratorUiTestIds.LandingContinueButton));
		continueButtons.Should().ContainSingle();
		continueButtons.Single().TextContent.Trim().Should().Be(ClientStrings.Landing_ContinueButton);

		continueButtons.Single().Click();

		cut.FindAll(TestId(ModeratorUiTestIds.DashboardShell)).Should().ContainSingle();
	}

	[Fact]
	public void FinishedRecovery_ContinueRendersExistingVictoryDestination()
	{
		var store = new RecordingSaveStore();
		using (var seedContext = CreateContext(store))
		{
			var seedManager = seedContext.Services.GetRequiredService<GameClientManager>();
			var startInstruction = StartRecoverableSession(seedContext);
			PostGameLobbyPrefillBunitTests.PlayToWerewolfVictoryAtDawn(
				seedManager,
				startInstruction);
		}

		using var recoveredContext = CreateContext(store);
		var cut = recoveredContext.RenderModeratorComponent<Routes>();
		var recoveredManager = recoveredContext.Services.GetRequiredService<GameClientManager>();

		recoveredManager.CurrentInstruction.Should().BeOfType<FinishedGameConfirmationInstruction>();
		cut.Find(TestId(ModeratorUiTestIds.LandingContinueButton)).Click();

		cut.FindAll("h1").Should().ContainSingle()
			.Which.TextContent.Trim().Should().Be(ClientStrings.Victory_Title);
	}

	[Fact]
	public void StagedLobbyRecovery_NewGameRendersRecoveredLobbyWithoutContinueOrResave()
	{
		var store = new RecordingSaveStore();
		using (var seedContext = CreateContext(store))
		{
			StageRecoverableLobby(seedContext);
		}
		var savesBeforeRecovery = store.SaveCount;

		using var recoveredContext = CreateContext(store);
		var cut = recoveredContext.RenderModeratorComponent<Routes>();

		cut.FindAll(TestId(ModeratorUiTestIds.LandingContinueButton)).Should().BeEmpty();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();

		AssertRenderedPlayerOrder(cut, PlayerNames);
		store.SaveCount.Should().Be(savesBeforeRecovery);
	}

	[Fact]
	public void UnreadableRecovery_NewGameRendersBlankLobbyWithoutContinue()
	{
		var store = new RecordingSaveStore("not-a-recovery-payload");
		using var context = CreateContext(store);
		var cut = context.RenderModeratorComponent<Routes>();

		cut.FindAll(TestId(ModeratorUiTestIds.LandingContinueButton)).Should().BeEmpty();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();

		context.Services.GetRequiredService<LobbySetupState>().PlayerRoster.Should().BeEmpty();
		store.ClearCount.Should().Be(1);
		cut.FindAll("h1").Should().ContainSingle()
			.Which.TextContent.Trim().Should().Be(ClientStrings.LobbyRoster_Title);
	}

	[Fact]
	public void ActiveRecovery_NewGameCancelIsInertAndConfirmUsesExistingClearBoundaryOnce()
	{
		var store = new RecordingSaveStore();
		using (var seedContext = CreateContext(store))
		{
			StartRecoverableSession(seedContext);
		}

		using var recoveredContext = CreateContext(store);
		var cut = recoveredContext.RenderModeratorComponent<Routes>();
		var manager = recoveredContext.Services.GetRequiredService<GameClientManager>();
		var lobby = recoveredContext.Services.GetRequiredService<LobbySetupState>();
		var saveCountBeforeChoice = store.SaveCount;
		var clearCountBeforeChoice = store.ClearCount;

		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();

		var dialogs = cut.FindAll("[role='dialog']");
		dialogs.Should().ContainSingle();
		dialogs.Single().TextContent.Should().Contain(ClientStrings.Landing_NewGameDialogTitle);
		dialogs.Single().TextContent.Should().Contain(ClientStrings.Landing_NewGameDialogDescription);
		dialogs.Single().TextContent.Should().Contain(ClientStrings.Landing_NewGameCancelButton);
		dialogs.Single().TextContent.Should().Contain(ClientStrings.Landing_NewGameConfirmButton);
		store.SaveCount.Should().Be(saveCountBeforeChoice);
		store.ClearCount.Should().Be(clearCountBeforeChoice);
		manager.HasActiveSession.Should().BeTrue();

		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameCancel)).Click();

		cut.FindAll("[role='dialog']").Should().BeEmpty();
		cut.FindAll(TestId(ModeratorUiTestIds.LandingShell)).Should().ContainSingle();
		store.SaveCount.Should().Be(saveCountBeforeChoice);
		store.ClearCount.Should().Be(clearCountBeforeChoice);
		manager.HasActiveSession.Should().BeTrue();

		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameConfirm)).Click();

		manager.HasActiveSession.Should().BeFalse();
		store.ClearCount.Should().Be(clearCountBeforeChoice + 1);
		store.SaveCount.Should().Be(saveCountBeforeChoice);
		store.Load().Should().BeNull();
		lobby.PlayerRoster.Select(player => player.Name).Should().Equal(PlayerNames);
		lobby.GetRoleCount(MainRoleType.SimpleWerewolf).Should().Be(1);
		lobby.GetRoleCount(MainRoleType.SimpleVillager).Should().Be(4);
		cut.FindAll("h1").Should().ContainSingle()
			.Which.TextContent.Trim().Should().Be(ClientStrings.LobbyRoster_Title);
	}

	[Fact]
	public void RosterBackThenNewGamePreservesCompleteInProcessLobby()
	{
		var store = new RecordingSaveStore();
		using var context = CreateContext(store);
		StageRecoverableLobby(context);
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		var expectedRoster = lobby.PlayerRoster.ToArray();
		var expectedRoleLockIn = lobby.AcceptedRoleLockIn;
		var saveCountBeforeNavigation = store.SaveCount;
		var cut = context.RenderModeratorComponent<Routes>();

		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();
		var backButtons = cut.FindAll(TestId(ModeratorUiTestIds.LobbyRosterBack));

		backButtons.Should().ContainSingle();
		backButtons.Single().TextContent.Trim().Should().Be(ClientStrings.LobbyRoster_BackButton);
		backButtons.Single().Click();
		cut.FindAll(TestId(ModeratorUiTestIds.LandingShell)).Should().ContainSingle();
		cut.Find(TestId(ModeratorUiTestIds.LandingNewGameButton)).Click();

		lobby.PlayerRoster.Should().Equal(expectedRoster);
		lobby.GetRoleCount(MainRoleType.SimpleWerewolf).Should().Be(1);
		lobby.GetRoleCount(MainRoleType.SimpleVillager).Should().Be(4);
		lobby.AcceptedRoleLockIn.Should().Be(expectedRoleLockIn);
		store.SaveCount.Should().Be(saveCountBeforeNavigation);
		AssertRenderedPlayerOrder(cut, PlayerNames);
	}

	[Fact]
	public void ColdLaunch_LandingUsesNoWakeLock()
	{
		var wakeLock = new RecordingScreenWakeLock { KeepScreenOn = true };
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IScreenWakeLock>(wakeLock);

		context.RenderModeratorComponent<Routes>();

		wakeLock.KeepScreenOn.Should().BeFalse();
	}

	private static ModeratorComponentTestContext CreateContext(RecordingSaveStore store)
	{
		var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IGameSessionSaveStore>(store);
		return context;
	}

	private static StartGameConfirmationInstruction StartRecoverableSession(
		ModeratorComponentTestContext context)
	{
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		SeedLobby(lobby);

		return context.Services.GetRequiredService<GameClientManager>().StartGame(lobby);
	}

	private static void StageRecoverableLobby(ModeratorComponentTestContext context)
	{
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		SeedLobby(lobby);
		context.Services.GetRequiredService<GameClientManager>()
			.TryEnsureStagedRoleLockIn(lobby).Should().BeTrue();
	}

	private static void SeedLobby(LobbySetupState lobby)
	{
		foreach (var playerName in PlayerNames)
		{
			lobby.AddPlayer(playerName);
		}

		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		for (var index = 0; index < 4; index++)
		{
			lobby.IncrementRole(MainRoleType.SimpleVillager);
		}

	}

	private static void AssertRenderedPlayerOrder(
		Bunit.IRenderedComponent<Routes> cut,
		IReadOnlyList<string> expectedPlayerNames)
	{
		var roster = cut.FindAll("[aria-label]")
			.Single(element => element.GetAttribute("aria-label") == ClientStrings.LobbyRoster_SeatOrderLabel);
		var rows = roster.QuerySelectorAll("li");

		rows.Should().HaveCount(expectedPlayerNames.Count);
		rows.Select(row => expectedPlayerNames.Single(name => row.TextContent.Contains(name, StringComparison.Ordinal)))
			.Should().Equal(expectedPlayerNames);
	}

	private static string[] PlayerNames =>
	[
		ClientTestReferences.PlayerNames.Ana,
		ClientTestReferences.PlayerNames.Bruno,
		ClientTestReferences.PlayerNames.Catarina,
		ClientTestReferences.PlayerNames.Diana,
		ClientTestReferences.PlayerNames.Eduardo
	];

	private static string TestId(string value) => $"[data-testid='{value}']";

	private sealed class RecordingSaveStore(string? initialPayload = null) : IGameSessionSaveStore
	{
		private string? _payload = initialPayload;

		public int SaveCount { get; private set; }
		public int ClearCount { get; private set; }

		public string? Load() => _payload;

		public void Save(string serializedSession)
		{
			SaveCount++;
			_payload = serializedSession;
		}

		public void Clear()
		{
			ClearCount++;
			_payload = null;
		}
	}

	private sealed class RecordingScreenWakeLock : IScreenWakeLock
	{
		public bool KeepScreenOn { get; set; }
	}
}
