using FluentAssertions;
using Werewolves.Client.Services;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Enums;
using Xunit;

namespace Werewolves.Client.Tests.Services;

public class AppRouteStateTests
{
	[Fact]
	public void StartGame_WithValidLobby_CreatesSessionAndNavigatesToDashboard()
	{
		var lobby = CreateValidLobby();
		var manager = new GameClientManager(new GameService());
		var routeState = new AppRouteState(manager);

		routeState.ShowRoleSelection();
		routeState.StartGame(lobby);

		routeState.CurrentScreen.Should().Be(AppScreen.Dashboard);
		manager.HasActiveSession.Should().BeTrue();
		manager.ActiveSession!.GetPlayers().Should().HaveCount(5);
	}

	[Fact]
	public void StartGame_WithInvalidLobby_DoesNotLeaveRoleSelection()
	{
		var lobby = new LobbySetupState();
		var manager = new GameClientManager(new GameService());
		var routeState = new AppRouteState(manager);
		routeState.ShowRoleSelection();

		var act = () => routeState.StartGame(lobby);

		act.Should().Throw<InvalidOperationException>();
		routeState.CurrentScreen.Should().Be(AppScreen.RoleSelection);
		manager.HasActiveSession.Should().BeFalse();
	}

	private static LobbySetupState CreateValidLobby()
	{
		var lobby = new LobbySetupState();
		foreach (var playerName in new[] { "Ana", "Bruno", "Catarina", "Diana", "Eduardo" })
		{
			lobby.AddPlayer(playerName);
		}

		lobby.IncrementRole(MainRoleType.SimpleWerewolf);
		lobby.IncrementRole(MainRoleType.Seer);
		lobby.IncrementRole(MainRoleType.SimpleVillager);
		lobby.IncrementRole(MainRoleType.SimpleVillager);
		lobby.IncrementRole(MainRoleType.SimpleVillager);

		return lobby;
	}
}
