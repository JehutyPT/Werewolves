using Bunit;
using FluentAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Werewolves.Client.Components.Game.Views;
using Werewolves.Client.Components.Pages;
using Werewolves.Client.Resources;
using Werewolves.Client.Services;
using Werewolves.Client.Testing;
using Werewolves.Client.Tests.Helpers;
using Werewolves.Core.StateModels.Enums;
using Xunit;

namespace Werewolves.Client.Tests.Components;

public sealed class LobbyScopedResetBunitTests
{
	[Fact]
	public async Task RosterReset_EarlyReleaseAndLeaveAreInertThenCompletedHoldClearsPlayersOnce()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		lobby.AddPlayer("Ana");
		lobby.AddPlayer("Bruno");
		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		var rosterBeforeReset = lobby.PlayerRoster
			.Select(player => (player.Id, player.Name))
			.ToArray();
		var lobbyNotifications = 0;
		var backCalls = 0;
		var continueCalls = 0;
		lobby.SimulationScenarioChanged += (_, _) => lobbyNotifications++;
		var cut = context.RenderModeratorComponent<LobbyRosterPage>(parameters => parameters
			.Add(component => component.OnBack,
				EventCallback.Factory.Create(this, () => backCalls++))
			.Add(component => component.OnContinue,
				EventCallback.Factory.Create(this, () => continueCalls++)));

		var resetSurface = cut.Find(TestId(ModeratorUiTestIds.LobbyRosterReset));
		resetSurface.TextContent.Should().Contain(ClientStrings.LobbyRoster_ResetButton);
		resetSurface.TextContent.Should().Contain(ClientStrings.Common_HoldToConfirm);
		var holdButton = cut.Find(
			$"{TestId(ModeratorUiTestIds.LobbyRosterReset)} {TestId(ModeratorUiTestIds.HoldButton)}");
		var shortHold = RenderedHoldButtonDriver.StartHoldAsync(holdButton);
		await RenderedHoldButtonDriver.FlushAsync(cut);
		timing.AdvanceBy(
			RenderedHoldButtonDriver.HoldDuration - TimeSpan.FromMilliseconds(1));
		await RenderedHoldButtonDriver.ReleaseHoldAsync(holdButton);
		await shortHold;
		lobby.PlayerRoster.Select(player => (player.Id, player.Name))
			.Should().Equal(rosterBeforeReset);
		lobbyNotifications.Should().Be(0);

		holdButton = cut.Find(
			$"{TestId(ModeratorUiTestIds.LobbyRosterReset)} {TestId(ModeratorUiTestIds.HoldButton)}");
		var canceledHold = RenderedHoldButtonDriver.StartHoldAsync(holdButton);
		await RenderedHoldButtonDriver.FlushAsync(cut);
		timing.AdvanceBy(TimeSpan.FromMilliseconds(200));
		await RenderedHoldButtonDriver.LeaveHoldAsync(holdButton);
		await canceledHold;
		timing.AdvanceBy(
			RenderedHoldButtonDriver.HoldDuration +
			RenderedHoldButtonDriver.SuccessFlashDuration);
		await RenderedHoldButtonDriver.FlushAsync(cut);
		lobby.PlayerRoster.Select(player => (player.Id, player.Name))
			.Should().Equal(rosterBeforeReset);
		lobbyNotifications.Should().Be(0);

		holdButton = cut.Find(
			$"{TestId(ModeratorUiTestIds.LobbyRosterReset)} {TestId(ModeratorUiTestIds.HoldButton)}");
		await RenderedHoldButtonDriver.CompleteHoldAsync(cut, holdButton, timing);

		lobby.PlayerRoster.Should().BeEmpty();
		lobby.GetRoleCount(MainRoleType.SimpleWerewolf).Should().Be(1);
		lobbyNotifications.Should().Be(1);
		backCalls.Should().Be(0);
		continueCalls.Should().Be(0);
		cut.Find(TestId(ModeratorUiTestIds.LobbyRosterReset));
	}

	[Fact]
	public async Task RoleSelectionReset_EarlyReleaseAndCancelAreInertThenCompletedHoldClearsRolesOnce()
	{
		var timing = new ControlledHoldButtonTiming();
		using var context = new ModeratorComponentTestContext();
		context.Services.AddSingleton<IHoldButtonTiming>(timing);
		var lobby = context.Services.GetRequiredService<LobbySetupState>();
		foreach (var playerName in new[] { "Ana", "Bruno", "Catarina", "Diana", "Eduardo" })
		{
			lobby.AddPlayer(playerName);
		}
		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		lobby.IncrementRole(MainRoleType.Seer);
		lobby.IncrementRole(MainRoleType.SimpleVillager);
		lobby.IncrementRole(MainRoleType.SimpleVillager);
		lobby.IncrementRole(MainRoleType.SimpleVillager);
		var playerRoster = lobby.PlayerRoster
			.Select(player => (player.Id, player.Name))
			.ToArray();
		var roleCounts = lobby.AvailableRoles.ToDictionary(
			role => role,
			lobby.GetRoleCount);
		var lobbyNotifications = 0;
		var backCalls = 0;
		var startCalls = 0;
		lobby.SimulationScenarioChanged += (_, _) => lobbyNotifications++;
		var cut = context.RenderModeratorComponent<RoleSelectionPage>(parameters => parameters
			.Add(component => component.OnBack,
				EventCallback.Factory.Create(this, () => backCalls++))
			.Add(component => component.OnLobbyExitAttempted,
				EventCallback.Factory.Create<LobbyExitOutcome>(this, _ => startCalls++)));

		var resetSurface = cut.Find(TestId(ModeratorUiTestIds.RoleSelectionReset));
		resetSurface.TextContent.Should().Contain(ClientStrings.RoleSelection_ResetButton);
		resetSurface.TextContent.Should().Contain(ClientStrings.Common_HoldToConfirm);
		var holdButton = cut.Find(
			$"{TestId(ModeratorUiTestIds.RoleSelectionReset)} {TestId(ModeratorUiTestIds.HoldButton)}");
		var shortHold = RenderedHoldButtonDriver.StartHoldAsync(holdButton);
		await RenderedHoldButtonDriver.FlushAsync(cut);
		timing.AdvanceBy(
			RenderedHoldButtonDriver.HoldDuration - TimeSpan.FromMilliseconds(1));
		await RenderedHoldButtonDriver.ReleaseHoldAsync(holdButton);
		await shortHold;
		lobby.AvailableRoles.ToDictionary(role => role, lobby.GetRoleCount)
			.Should().Equal(roleCounts);
		lobbyNotifications.Should().Be(0);

		holdButton = cut.Find(
			$"{TestId(ModeratorUiTestIds.RoleSelectionReset)} {TestId(ModeratorUiTestIds.HoldButton)}");
		var canceledHold = RenderedHoldButtonDriver.StartHoldAsync(holdButton);
		await RenderedHoldButtonDriver.FlushAsync(cut);
		timing.AdvanceBy(TimeSpan.FromMilliseconds(200));
		await holdButton.TriggerEventAsync(
			ClientTestReferences.Html.Events.PointerCancel,
			new PointerEventArgs());
		await canceledHold;
		timing.AdvanceBy(
			RenderedHoldButtonDriver.HoldDuration +
			RenderedHoldButtonDriver.SuccessFlashDuration);
		await RenderedHoldButtonDriver.FlushAsync(cut);
		lobby.AvailableRoles.ToDictionary(role => role, lobby.GetRoleCount)
			.Should().Equal(roleCounts);
		lobbyNotifications.Should().Be(0);

		holdButton = cut.Find(
			$"{TestId(ModeratorUiTestIds.RoleSelectionReset)} {TestId(ModeratorUiTestIds.HoldButton)}");
		await RenderedHoldButtonDriver.CompleteHoldAsync(cut, holdButton, timing);

		lobby.PlayerRoster.Select(player => (player.Id, player.Name))
			.Should().Equal(playerRoster);
		lobby.AvailableRoles.Should().OnlyContain(role => lobby.GetRoleCount(role) == 0);
		lobbyNotifications.Should().Be(1);
		backCalls.Should().Be(0);
		startCalls.Should().Be(0);
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionStartGame))
			.HasAttribute("disabled").Should().BeTrue();
		cut.Find(TestId(ModeratorUiTestIds.RoleSelectionReset));
	}

	private static string TestId(string value) => $"[data-testid='{value}']";
}
