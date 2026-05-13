using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;

namespace Werewolves.Client.Services;

public class GameClientManager
{
	private readonly GameService _gameService;

	public GameClientManager(GameService gameService)
	{
		_gameService = gameService;
	}

	public event Action? StateChanged;

	public Guid? ActiveGameId { get; private set; }

	public IGameSession? ActiveSession { get; private set; }

	public ModeratorInstruction? CurrentInstruction { get; private set; }

	public bool HasActiveSession => ActiveGameId.HasValue && ActiveSession is not null;

	public StartGameConfirmationInstruction StartGame(LobbySetupState lobby)
	{
		var config = new GameSessionConfig(
			lobby.PlayerNames.ToList(),
			lobby.GetSelectedRoles());

		return StartGame(config);
	}

	public StartGameConfirmationInstruction StartGame(GameSessionConfig config)
	{
		var instruction = _gameService.StartNewGame(config);

		ActiveGameId = instruction.GameGuid;
		RefreshState(instruction);

		return instruction;
	}

	public ProcessResult ProcessModeratorResponse(ModeratorResponse response)
	{
		if (ActiveGameId is not { } gameId)
		{
			throw new InvalidOperationException("Cannot process a moderator response before a game session has started.");
		}

		var result = _gameService.ProcessInstruction(gameId, response);
		RefreshState(result.ModeratorInstruction);

		return result;
	}

	private void RefreshState(ModeratorInstruction? instruction)
	{
		if (ActiveGameId is { } gameId)
		{
			ActiveSession = _gameService.GetGameStateView(gameId);
			CurrentInstruction = instruction ?? _gameService.GetCurrentInstruction(gameId);
		}
		else
		{
			ActiveSession = null;
			CurrentInstruction = null;
		}

		StateChanged?.Invoke();
	}
}
