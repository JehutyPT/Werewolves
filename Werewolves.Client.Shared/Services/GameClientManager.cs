using Werewolves.Core.GameLogic.Models.InternalMessages;
using Werewolves.Core.GameLogic.Services;
using Werewolves.Core.StateModels.Core;
using Werewolves.Core.StateModels.Enums;
using Werewolves.Core.StateModels.Models;
using Werewolves.Core.StateModels.Models.Instructions;
using Werewolves.Core.StateModels.Resources;

namespace Werewolves.Client.Services;

public sealed class GameClientManager
{
	private readonly GameService _gameService;
	private readonly IInstructionAudioPlayback _audioPlayback;
	private readonly IGameSessionSaveStore _saveStore;
	private readonly TimeProvider _timeProvider;
	private readonly LobbySetupState? _lobbySetupState;
	private StagedLobbyRecoveryPayload? _stagedLobby;
	private DateTimeOffset? _debateStartedAt;

	public GameClientManager()
		: this(new GameService())
	{
	}

	public GameClientManager(
		GameService gameService,
		IInstructionAudioPlayback? audioPlayback = null,
		IGameSessionSaveStore? saveStore = null,
		TimeProvider? timeProvider = null,
		LobbySetupState? lobbySetupState = null)
	{
		_gameService = gameService;
		_audioPlayback = audioPlayback ?? DisabledInstructionAudioPlayback.Instance;
		_saveStore = saveStore ?? DisabledGameSessionSaveStore.Instance;
		_timeProvider = timeProvider ?? TimeProvider.System;
		_lobbySetupState = lobbySetupState;
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

	public TimeSpan? DebateElapsed =>
		_debateStartedAt is { } start ? _timeProvider.GetUtcNow() - start : null;
	public RoleLockIn? StagedRoleLockIn => _stagedLobby?.RoleLockIn;

	public bool TryReplaceStagedRoleLockIn(
		LobbySetupState lobby,
		long expectedCurrentVersion,
		MainRoleType offer1,
		MainRoleType offer2)
	{
		ArgumentNullException.ThrowIfNull(lobby);
		if (expectedCurrentVersion == long.MaxValue)
		{
			return false;
		}

		RoleLockIn replacement;
		try
		{
			replacement = RoleLockIn.CreateFromPrintedRoles(
				expectedCurrentVersion + 1,
				lobby.PlayerNames.Count,
				lobby.GetSelectedRoles(),
				offer1,
				offer2);
		}
		catch (ArgumentException)
		{
			return false;
		}

		return TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion,
			replacement);
	}

	public bool TryReplaceStagedRoleLockIn(
		LobbySetupState lobby,
		long expectedCurrentVersion,
		RoleLockIn replacement)
	{
		ArgumentNullException.ThrowIfNull(lobby);
		ArgumentNullException.ThrowIfNull(replacement);
		if (HasActiveSession ||
			!lobby.CanReplaceRoleLockIn(expectedCurrentVersion, replacement))
		{
			return false;
		}

		try
		{
			PersistStagedRoleLockIn(lobby, replacement);
		}
		catch (Exception)
		{
			return false;
		}
		return true;
	}

	public StartGameConfirmationInstruction StartGame(
		IReadOnlyList<string> playerNamesInOrder,
		IReadOnlyList<MainRoleType> rolesInPlay)
	{
		var config = new GameSessionConfig(playerNamesInOrder.ToList(), rolesInPlay.ToList());
		return StartGame(config);
	}

	public StartGameConfirmationInstruction StartGame(LobbySetupState lobby)
	{
		ArgumentNullException.ThrowIfNull(lobby);
		if (lobby.RequiresRoleLockIn)
		{
			throw new InvalidOperationException(
				"Lobby Exit requires a fresh accepted Role Lock-In after Lobby edits.");
		}
		GameSessionConfig config;
		if (lobby.AcceptedRoleLockIn is { } acceptedRoleLockIn &&
			!lobby.AcceptedRoleLockInRequiresReplacement)
		{
			config = new GameSessionConfig(
				lobby.PlayerNames.ToList(),
				acceptedRoleLockIn);
		}
		else
		{
			var expectedCurrentVersion = lobby.AcceptedRoleLockIn?.Version ?? 0;
			if (expectedCurrentVersion == long.MaxValue)
			{
				throw new InvalidOperationException(
					"Lobby Exit could not finalize the current Role Lock-In.");
			}
			var replacement = RoleLockIn.CreateFromPrintedRoles(
				expectedCurrentVersion + 1,
				lobby.PlayerNames.Count,
				lobby.GetSelectedRoles());
			config = new GameSessionConfig(
				lobby.PlayerNames.ToList(),
				replacement);
			if (!lobby.CanReplaceRoleLockIn(
				expectedCurrentVersion,
				config.RoleLockIn))
			{
				throw new InvalidOperationException(
					"Lobby Exit could not finalize the current Role Lock-In.");
			}
			PersistStagedRoleLockIn(lobby, config.RoleLockIn);
		}
		return StartGame(config, lobby);
	}

	private void PersistStagedRoleLockIn(
		LobbySetupState lobby,
		RoleLockIn roleLockIn)
	{
		var payload = LocalRecoveryPayloadCodec.SerializeStagedLobby(
			lobby.PlayerNames,
			roleLockIn);
		_saveStore.Save(payload);
		lobby.ApplyAcceptedRoleLockIn(roleLockIn);
		_stagedLobby = new StagedLobbyRecoveryPayload(
			lobby.PlayerNames.ToArray(),
			roleLockIn);
		OnStateChanged();
	}

	public StartGameConfirmationInstruction StartGame(GameSessionConfig config)
		=> StartGame(config, lobby: null);

	private StartGameConfirmationInstruction StartGame(
		GameSessionConfig config,
		LobbySetupState? lobby)
	{
		var instruction = _gameService.StartNewGame(config);
		var startedSession = _gameService.GetGameStateView(instruction.GameGuid)
			?? throw new InvalidOperationException("Core did not publish the stable initial Game Session.");
		if (lobby is not null)
		{
			try
			{
				_saveStore.Save(LocalRecoveryPayloadCodec.SerializeActiveGame(
					startedSession.Serialize()));
			}
			catch
			{
				_gameService.DiscardSession(instruction.GameGuid);
				throw;
			}
			lobby.FinalizeRoleLockIn(config.RoleLockIn);
		}

		ActiveGameId = instruction.GameGuid;
		CurrentSession = startedSession;
		CurrentInstruction = _gameService.GetCurrentInstruction(instruction.GameGuid)
			?? instruction;
		if (lobby is null)
		{
			SaveCurrentSession();
		}
		_stagedLobby = null;
		UpdateDebateTimer();
		QueueAudioReconciliation();
		OnStateChanged();
		return instruction;
	}

	public void ClearSession()
	{
		if (ActiveGameId is { } gameId)
		{
			_gameService.DiscardSession(gameId);
		}

		ActiveGameId = null;
		CurrentSession = null;
		CurrentInstruction = null;
		ClearSavedGame();
		OnStateChanged();
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
			UpdateDebateTimer();
			if (ShouldClearSaveAfterSuccessfulInput())
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

	private bool ShouldClearSaveAfterSuccessfulInput() =>
		CurrentSession is null;

	private void UpdateDebateTimer()
	{
		if (IsDebateInstruction(CurrentInstruction))
		{
			_debateStartedAt ??= _timeProvider.GetUtcNow();
		}
		else
		{
			_debateStartedAt = null;
		}
	}

	private static bool IsDebateInstruction(ModeratorInstruction? instruction) =>
		instruction is ConfirmationInstruction &&
		instruction.PublicAnnouncement == GameStrings.DebateStartsPrompt;

	private void SaveCurrentSession()
	{
		if (CurrentSession is null)
		{
			return;
		}

		try
		{
			_saveStore.Save(LocalRecoveryPayloadCodec.SerializeActiveGame(
				CurrentSession.Serialize()));
		}
		catch (Exception)
		{
		}
	}

	private void TryResumeSavedGame()
	{
		try
		{
			var serializedPayload = _saveStore.Load();
			if (string.IsNullOrWhiteSpace(serializedPayload))
			{
				return;
			}

			switch (LocalRecoveryPayloadCodec.Deserialize(serializedPayload))
			{
				case StagedLobbyRecoveryPayload stagedLobby:
					_stagedLobby = stagedLobby;
					_lobbySetupState?.RestoreAcceptedRoleLockIn(
						stagedLobby.PlayerNames,
						stagedLobby.RoleLockIn);
					break;
				case ActiveGameRecoveryPayload activeGame:
					ActiveGameId = _gameService.RehydrateSession(activeGame.SerializedSession);
					RefreshCurrentState();
					UpdateDebateTimer();
					break;
			}
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
