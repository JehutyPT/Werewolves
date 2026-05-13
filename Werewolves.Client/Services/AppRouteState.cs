namespace Werewolves.Client.Services;

public enum AppScreen
{
	Roster,
	RoleSelection,
	Dashboard
}

public class AppRouteState
{
	private readonly GameClientManager _gameClientManager;

	public AppRouteState(GameClientManager gameClientManager)
	{
		_gameClientManager = gameClientManager;
	}

	public event Action? StateChanged;

	public AppScreen CurrentScreen { get; private set; } = AppScreen.Roster;

	public void ShowRoleSelection()
	{
		CurrentScreen = AppScreen.RoleSelection;
		StateChanged?.Invoke();
	}

	public void ShowRoster()
	{
		CurrentScreen = AppScreen.Roster;
		StateChanged?.Invoke();
	}

	public void StartGame(LobbySetupState lobby)
	{
		_gameClientManager.StartGame(lobby);
		CurrentScreen = AppScreen.Dashboard;
		StateChanged?.Invoke();
	}
}
