using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;

namespace Werewolves.Client.Services;

public sealed class GameClientManager
{
	private readonly GameService _gameService;

	public GameClientManager()
		: this(new GameService())
	{
	}

	public GameClientManager(GameService gameService)
	{
		_gameService = gameService;
	}

	public event EventHandler? StateChanged;

	public Guid? ActiveGameId { get; private set; }
	public IGameSession? CurrentSession { get; private set; }
	public ModeratorInstruction? CurrentInstruction { get; private set; }
	public bool HasActiveSession => ActiveGameId.HasValue && CurrentSession is not null;
	public GamePhase? CurrentPhase => CurrentSession?.GetCurrentPhase();
	public int? TurnNumber => CurrentSession?.TurnNumber;

	public StartGameConfirmationInstruction StartGame(
		IReadOnlyList<string> playerNamesInOrder,
		IReadOnlyList<MainRoleType> rolesInPlay)
	{
		var config = new GameSessionConfig(playerNamesInOrder.ToList(), rolesInPlay.ToList());
		return StartGame(config);
	}

	public StartGameConfirmationInstruction StartGame(GameSessionConfig config)
	{
		var instruction = _gameService.StartNewGame(config);
		ActiveGameId = instruction.GameGuid;
		RefreshCurrentState(instruction);
		OnStateChanged();
		return instruction;
	}

	public ProcessResult ProcessInput(ModeratorResponse response)
	{
		if (ActiveGameId is not { } gameId)
		{
			throw new InvalidOperationException("Cannot process moderator response without an active game session.");
		}

		var result = _gameService.ProcessInstruction(gameId, response);
		if (result.IsSuccess)
		{
			RefreshCurrentState(result.ModeratorInstruction);
			OnStateChanged();
		}

		return result;
	}

	private void RefreshCurrentState(ModeratorInstruction? fallbackInstruction = null)
	{
		if (ActiveGameId is not { } gameId)
		{
			CurrentSession = null;
			CurrentInstruction = null;
			return;
		}

		CurrentSession = _gameService.GetGameStateView(gameId);
		CurrentInstruction = _gameService.GetCurrentInstruction(gameId) ?? fallbackInstruction;
	}

	private void OnStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
