using Microsoft.AspNetCore.WebUtilities;
using Werewolves.Client.Services;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models.Instructions;

namespace Werewolves.Client.BrowserQaHost;

public enum BrowserQaScenario
{
	Lobby,
	Probability,
	Dashboard,
	Victory
}

public static class BrowserQaScenarioSelection
{
	public static BrowserQaScenario FromUri(string uri)
	{
		var query = QueryHelpers.ParseQuery(new Uri(uri).Query);
		var rawScenario = query.TryGetValue("qa", out var qaValue)
			? qaValue.ToString()
			: query.TryGetValue("scenario", out var scenarioValue)
				? scenarioValue.ToString()
				: null;

		return Enum.TryParse<BrowserQaScenario>(rawScenario, ignoreCase: true, out var scenario)
			? scenario
			: BrowserQaScenario.Lobby;
	}
}

public static class BrowserQaScenarioSeeder
{
	public static void Apply(BrowserQaScenario scenario, LobbySetupState lobby, GameClientManager game)
	{
		switch (scenario)
		{
			case BrowserQaScenario.Dashboard:
				StartDashboardScenario(lobby, game);
				break;
			case BrowserQaScenario.Victory:
				StartVictoryScenario(lobby, game);
				break;
			default:
				ResetToDefaultLobby(lobby);
				game.ClearSession();
				break;
		}
	}

	private static void StartDashboardScenario(LobbySetupState lobby, GameClientManager game)
	{
		ResetToDefaultLobby(lobby);
		game.ClearSession();

		var startInstruction = game.StartGame(
			lobby.PlayerNames,
			lobby.GetSelectedRoles());
		game.ProcessInput(startInstruction.CreateResponse(true));
	}

	private static void StartVictoryScenario(LobbySetupState lobby, GameClientManager game)
	{
		ResetToDefaultLobby(lobby);
		game.ClearSession();

		var startInstruction = game.StartGame(
			lobby.PlayerNames,
			lobby.GetSelectedRoles());
		game.ProcessInput(startInstruction.CreateResponse(true));

		var players = game.CurrentSession!.GetPlayers().ToList();
		var werewolfIds = players.Take(2).Select(player => player.Id).ToHashSet();
		var victimId = players[2].Id;

		ConfirmCurrentInstruction(game);
		SelectCurrentPlayers(game, werewolfIds);
		SelectCurrentPlayers(game, [victimId]);
		ConfirmCurrentInstruction(game);
		ConfirmCurrentInstruction(game);

		for (var step = 0; step < 20; step++)
		{
			switch (game.CurrentInstruction)
			{
				case FinishedGameConfirmationInstruction:
					return;
				case AssignRolesInstruction assignRoles:
					var assignments = assignRoles.PlayersForAssignment.ToDictionary(
						playerId => playerId,
						_ => MainRoleType.SimpleVillager);
					game.ProcessInput(assignRoles.CreateResponse(assignments));
					break;
				case ConfirmationInstruction confirmation:
					game.ProcessInput(confirmation.CreateResponse(true));
					break;
				default:
					throw new InvalidOperationException(
						$"Unexpected instruction while seeding browser QA victory: {game.CurrentInstruction?.GetType().Name}");
			}
		}

		throw new InvalidOperationException("Browser QA victory scenario did not reach a finished game.");
	}

	private static void ResetToDefaultLobby(LobbySetupState lobby)
	{
		lobby.Reset();

		foreach (var playerName in BrowserQaFixtures.DefaultPlayerNames)
		{
			lobby.AddPlayer(playerName);
		}

		foreach (var role in BrowserQaFixtures.DefaultRoles)
		{
			lobby.IncrementRole(role);
		}
	}

	private static void ConfirmCurrentInstruction(GameClientManager game)
	{
		if (game.CurrentInstruction is not ConfirmationInstruction confirmation)
		{
			throw new InvalidOperationException(
				$"Expected confirmation instruction while seeding browser QA scenario, but found {game.CurrentInstruction?.GetType().Name}.");
		}

		game.ProcessInput(confirmation.CreateResponse(true));
	}

	private static void SelectCurrentPlayers(GameClientManager game, HashSet<Guid> playerIds)
	{
		if (game.CurrentInstruction is not SelectPlayersInstruction selectPlayers)
		{
			throw new InvalidOperationException(
				$"Expected player selection instruction while seeding browser QA scenario, but found {game.CurrentInstruction?.GetType().Name}.");
		}

		game.ProcessInput(selectPlayers.CreateResponse(playerIds));
	}
}
