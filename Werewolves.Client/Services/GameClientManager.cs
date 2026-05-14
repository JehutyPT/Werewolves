using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;

namespace Werewolves.Client.Services;

public sealed class GameClientManager
{
	private readonly GameService _gameService;
	private readonly IInstructionAudioPlayback _audioPlayback;
	private readonly IGameSessionSaveStore _saveStore;

	public GameClientManager()
		: this(new GameService(), DisabledInstructionAudioPlayback.Instance, FileGameSessionSaveStore.CreateDefault())
	{
	}

	public GameClientManager(GameService gameService)
		: this(gameService, DisabledInstructionAudioPlayback.Instance, FileGameSessionSaveStore.CreateDefault())
	{
	}

	public GameClientManager(GameService gameService, IInstructionAudioPlayback audioPlayback)
		: this(gameService, audioPlayback, FileGameSessionSaveStore.CreateDefault())
	{
	}

	public GameClientManager(GameService gameService, IGameSessionSaveStore saveStore)
		: this(gameService, DisabledInstructionAudioPlayback.Instance, saveStore)
	{
	}

	public GameClientManager(
		GameService gameService,
		IInstructionAudioPlayback audioPlayback,
		IGameSessionSaveStore saveStore)
	{
		_gameService = gameService;
		_audioPlayback = audioPlayback;
		_saveStore = saveStore;
		TryResumeSavedGame();
	}

	public event EventHandler? StateChanged;

	public Guid? ActiveGameId { get; private set; }
	public IGameSession? CurrentSession { get; private set; }
	public ModeratorInstruction? CurrentInstruction { get; private set; }
	public bool HasActiveSession => ActiveGameId.HasValue && CurrentSession is not null;
	public GamePhase? CurrentPhase => CurrentSession?.GetCurrentPhase();
	public int? TurnNumber => CurrentSession?.TurnNumber;
	public IReadOnlyList<DashboardRosterEntry> CurrentRoster => DashboardRoster.FromSession(CurrentSession);
	public bool IsAudioMuted => _audioPlayback.IsMuted;
	public Task PendingAudioReconciliation { get; private set; } = Task.CompletedTask;

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
		ClearSavedGame();
		ActiveGameId = instruction.GameGuid;
		RefreshCurrentState(instruction);
		QueueAudioReconciliation();
		OnStateChanged();
		return instruction;
	}

	public ProcessResult ProcessInput(ModeratorResponse response)
	{
		if (ActiveGameId is not { } gameId)
		{
			// Developer-facing guard, not rendered UI copy.
			throw new InvalidOperationException("Cannot process moderator response without an active game session.");
		}

		var result = _gameService.ProcessInstruction(gameId, response);
		if (result.IsSuccess)
		{
			RefreshCurrentState(result.ModeratorInstruction);
			if (ShouldClearSaveAfterSuccessfulInput(result))
			{
				ClearSavedGame();
				if (CurrentSession is null)
				{
					ActiveGameId = null;
				}
			}
			else
			{
				SaveCurrentSession();
			}

			QueueAudioReconciliation();
			OnStateChanged();
		}

		return result;
	}

	public Task ToggleAudioMuteAsync()
	{
		PendingAudioReconciliation = _audioPlayback.SetMutedAsync(!IsAudioMuted, CurrentInstruction);
		OnStateChanged();
		return PendingAudioReconciliation;
	}

	public Task ReconcileAudioAfterResumeAsync()
	{
		QueueAudioReconciliation();
		return PendingAudioReconciliation;
	}

	private bool ShouldClearSaveAfterSuccessfulInput(ProcessResult result) =>
		result.ModeratorInstruction is FinishedGameConfirmationInstruction ||
		CurrentInstruction is FinishedGameConfirmationInstruction ||
		CurrentSession is null;

	private void SaveCurrentSession()
	{
		if (CurrentSession is null)
		{
			return;
		}

		try
		{
			_saveStore.Save(CurrentSession.Serialize());
		}
		catch (Exception)
		{
		}
	}

	private void TryResumeSavedGame()
	{
		try
		{
			var serializedSession = _saveStore.Load();
			if (string.IsNullOrWhiteSpace(serializedSession))
			{
				return;
			}

			ActiveGameId = _gameService.RehydrateSession(serializedSession);
			RefreshCurrentState();
		}
		catch (Exception)
		{
			ActiveGameId = null;
			CurrentSession = null;
			CurrentInstruction = null;
			ClearSavedGame();
		}
	}

	private void ClearSavedGame()
	{
		try
		{
			_saveStore.Clear();
		}
		catch (Exception)
		{
		}
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

	private void QueueAudioReconciliation()
	{
		PendingAudioReconciliation = _audioPlayback.ReconcileAsync(CurrentInstruction);
	}

	private void OnStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
