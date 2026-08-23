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
	private readonly IRecentSetupStore _recentSetupStore;
	private readonly TimeProvider _timeProvider;
	private readonly LobbySetupState? _lobbySetupState;
	private StagedLobbyRecoveryPayload? _stagedLobby;
	private StagedLobbyRecoveryPayload? _activeSessionLobbyPayload;
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
		LobbySetupState? lobbySetupState = null,
		IRecentSetupStore? recentSetupStore = null)
	{
		_gameService = gameService;
		_audioPlayback = audioPlayback ?? DisabledInstructionAudioPlayback.Instance;
		_saveStore = saveStore ?? DisabledGameSessionSaveStore.Instance;
		_recentSetupStore = recentSetupStore ?? DisabledRecentSetupStore.Instance;
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
		return TryAcceptLobbyChange(
			lobby,
			new LobbyChange.ReplaceRoleLockIn(
				expectedCurrentVersion,
				replacement));
	}

	private bool TryAcceptLobbyChange(
		LobbySetupState lobby,
		LobbyChange change) =>
		TryAcceptLobbyChange(lobby, change, out _);

	private bool TryAcceptLobbyChange(
		LobbySetupState lobby,
		LobbyChange change,
		out bool persistenceAttempted,
		Action? afterPersistenceAccepted = null)
	{
		persistenceAttempted = false;
		if (HasActiveSession && afterPersistenceAccepted is null)
		{
			return false;
		}

		var decision = lobby.Decide(change);
		if (decision is null)
		{
			return false;
		}

		try
		{
			persistenceAttempted = true;
			LobbyPersistenceExecutor.Execute(_saveStore, decision.Persistence);
		}
		catch (Exception)
		{
			return false;
		}

		afterPersistenceAccepted?.Invoke();
		lobby.Publish(decision.Commit);
		ReconcilePublishedLobbyDecision(lobby, decision);
		return true;
	}

	private void ReconcilePublishedLobbyDecision(
		LobbySetupState lobby,
		LobbyDecision decision)
	{
		switch (decision.Persistence)
		{
			case LobbyPersistenceInstruction.Keep
				when !decision.PublishesStateChange:
				return;
			case LobbyPersistenceInstruction.Keep:
				break;
			case LobbyPersistenceInstruction.Clear:
				_stagedLobby = null;
				break;
			case LobbyPersistenceInstruction.Replace replace:
				var aggregate = replace.Aggregate;
				_stagedLobby = new StagedLobbyRecoveryPayload(
					aggregate.PlayerRoster,
					aggregate.AcceptedRoleLockIn!,
					aggregate.AcceptedActorSetupCards,
					aggregate.AcceptedPublicGroupPartition);
				break;
		}

		try
		{
			lobby.NotifySimulationScenarioChanged();
		}
		catch (Exception)
		{
		}

		try
		{
			OnStateChanged();
		}
		catch (Exception)
		{
		}
	}

	public bool TryReplaceStagedActorSetupCards(
		LobbySetupState lobby,
		long expectedCurrentVersion,
		IReadOnlyList<MainRoleType> printedRoles)
	{
		ArgumentNullException.ThrowIfNull(lobby);
		ArgumentNullException.ThrowIfNull(printedRoles);
		if (expectedCurrentVersion == long.MaxValue)
		{
			return false;
		}

		ActorSetupCards replacement;
		try
		{
			replacement = ActorSetupCards.CreateFromPrintedRoles(
				expectedCurrentVersion + 1,
				printedRoles);
		}
		catch (ArgumentException)
		{
			return false;
		}

		return TryReplaceStagedActorSetupCards(
			lobby,
			expectedCurrentVersion,
			replacement);
	}

	internal bool TryReplaceStagedActorSetupCards(
		LobbySetupState lobby,
		long expectedCurrentVersion,
		ActorSetupCards replacement)
	{
		ArgumentNullException.ThrowIfNull(lobby);
		ArgumentNullException.ThrowIfNull(replacement);
		return TryAcceptLobbyChange(
			lobby,
			new LobbyChange.ReplaceActorSetupCards(
				expectedCurrentVersion,
				replacement));
	}

	public bool TryReplaceStagedPublicGroupPartition(
		LobbySetupState lobby,
		PublicGroupPartition replacement)
	{
		ArgumentNullException.ThrowIfNull(lobby);
		ArgumentNullException.ThrowIfNull(replacement);
		return TryAcceptLobbyChange(
			lobby,
			new LobbyChange.ReplacePublicGroupPartition(replacement));
	}

	public bool TryMoveStagedPlayerDown(LobbySetupState lobby, int index)
	{
		ArgumentNullException.ThrowIfNull(lobby);
		return TryAcceptLobbyChange(
			lobby,
			new LobbyChange.MovePlayer(index, index + 1));
	}

	public bool TryMoveStagedPlayerUp(LobbySetupState lobby, int index)
	{
		ArgumentNullException.ThrowIfNull(lobby);
		return TryAcceptLobbyChange(
			lobby,
			new LobbyChange.MovePlayer(index, index - 1));
	}

	public bool TryAddStagedPlayer(
		LobbySetupState lobby,
		string playerName,
		out AddPlayerResult result)
	{
		ArgumentNullException.ThrowIfNull(lobby);
		var normalizedName = playerName.Trim();
		if (normalizedName.Length == 0)
		{
			result = AddPlayerResult.EmptyName;
			return false;
		}
		if (lobby.PlayerRoster.Any(player => string.Equals(
			player.Name,
			normalizedName,
			StringComparison.OrdinalIgnoreCase)))
		{
			result = AddPlayerResult.DuplicateName;
			return false;
		}

		result = AddPlayerResult.Success;
		if (HasActiveSession)
		{
			return false;
		}

		var player = lobby.CreatePlayerRosterEntry(normalizedName);
		return TryAcceptLobbyChange(
			lobby,
			new LobbyChange.AddPlayer(player));
	}

	public bool TryRemoveStagedPlayer(LobbySetupState lobby, int index)
	{
		ArgumentNullException.ThrowIfNull(lobby);
		return TryAcceptLobbyChange(
			lobby,
			new LobbyChange.RemovePlayer(index));
	}

	public bool TryResetStagedPlayerRoster(LobbySetupState lobby)
	{
		ArgumentNullException.ThrowIfNull(lobby);
		return TryAcceptLobbyChange(lobby, new LobbyChange.ResetPlayerRoster());
	}

	public bool TryResetStagedRoleCounts(LobbySetupState lobby)
	{
		ArgumentNullException.ThrowIfNull(lobby);
		return TryAcceptLobbyChange(lobby, new LobbyChange.ResetRoleCounts());
	}

	public bool TryApplyRecentSetup(
		LobbySetupState lobby,
		RecentSetup setup)
	{
		ArgumentNullException.ThrowIfNull(lobby);
		ArgumentNullException.ThrowIfNull(setup);
		return TryAcceptLobbyChange(
			lobby,
			new LobbyChange.ApplyRecentSetup(setup));
	}

	public bool TryAbandonSessionAndApplyRecentSetup(
		LobbySetupState lobby,
		RecentSetup setup)
	{
		ArgumentNullException.ThrowIfNull(lobby);
		ArgumentNullException.ThrowIfNull(setup);
		if (!HasActiveSession)
		{
			return TryApplyRecentSetup(lobby, setup);
		}

		return TryAcceptLobbyChange(
			lobby,
			new LobbyChange.ApplyRecentSetup(setup, ClearsRecovery: true),
			out _,
			() =>
			{
				DiscardCurrentSession();
				_activeSessionLobbyPayload = null;
			});
	}

	public StartGameConfirmationInstruction StartGame(
		IReadOnlyList<string> playerNamesInOrder,
		IReadOnlyList<MainRoleType> rolesInPlay)
	{
		var config = new GameSessionConfig(playerNamesInOrder.ToList(), rolesInPlay.ToList());
		return StartGame(config);
	}

	public bool TryEnsureStagedRoleLockIn(LobbySetupState lobby)
	{
		ArgumentNullException.ThrowIfNull(lobby);
		if (HasActiveSession)
		{
			return false;
		}
		if (lobby.AcceptedRoleLockIn is not null &&
			!lobby.AcceptedRoleLockInRequiresReplacement)
		{
			return true;
		}

		var selectedRoles = lobby.GetSelectedRoles();
		if (selectedRoles.Contains(MainRoleType.Thief))
		{
			return false;
		}
		var expectedCurrentVersion = lobby.AcceptedRoleLockIn?.Version ?? 0;
		if (expectedCurrentVersion == long.MaxValue)
		{
			return false;
		}

		RoleLockIn replacement;
		try
		{
			replacement = RoleLockIn.CreateFromPrintedRoles(
				expectedCurrentVersion + 1,
				lobby.PlayerRoster.Count,
				selectedRoles);
		}
		catch (ArgumentException)
		{
			return false;
		}

		return TryAcceptLobbyChange(
			lobby,
			new LobbyChange.AcceptImplicitRoleLockIn(
				expectedCurrentVersion,
				replacement));
	}

	public StartGameConfirmationInstruction StartGame(LobbySetupState lobby)
	{
		ArgumentNullException.ThrowIfNull(lobby);
		if (!TryEnsureStagedRoleLockIn(lobby))
		{
			throw new InvalidOperationException(
				"Lobby Exit requires a fresh accepted Role Lock-In after Lobby edits.");
		}
		if (!lobby.TryCreateSimulationScenario(out _))
		{
			throw new InvalidOperationException(
				"Lobby Exit requires a complete accepted Simulation Scenario.");
		}
		var acceptedRoleLockIn = lobby.AcceptedRoleLockIn!;
		var config = new GameSessionConfig(
			lobby.PlayerRoster,
			acceptedRoleLockIn,
			lobby.AcceptedActorSetupCards,
			lobby.AcceptedPublicGroupPartition);
		return StartGame(config, lobby);
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
		_activeSessionLobbyPayload = new StagedLobbyRecoveryPayload(
			config.PlayerRoster,
			config.RoleLockIn,
			config.ActorSetupCards,
			config.PublicGroupPartition);
		_stagedLobby = null;
		UpdateDebateTimer();
		QueueAudioReconciliation();
		OnStateChanged();
		if (lobby is not null)
		{
			try
			{
				_recentSetupStore.Capture(
					config.PlayerRoster.Select(player => player.Name).ToArray(),
					config.RoleLockIn.RoleComposition
						.GroupBy(card => card.PrintedRole)
						.ToDictionary(group => group.Key, group => group.Count()));
			}
			catch
			{
			}
		}
		return instruction;
	}

	public void ClearSession()
	{
		DiscardCurrentSession();
		if (!PrefillLobbyAfterSession(out var persistenceAttempted))
		{
			if (!persistenceAttempted)
			{
				ClearSavedGame();
			}
			OnStateChanged();
		}
	}

	private void DiscardCurrentSession()
	{
		if (ActiveGameId is { } gameId)
		{
			_gameService.DiscardSession(gameId);
		}

		ActiveGameId = null;
		CurrentSession = null;
		CurrentInstruction = null;
	}

	private bool PrefillLobbyAfterSession(out bool persistenceAttempted)
	{
		persistenceAttempted = false;
		var lobby = _lobbySetupState;
		var payload = _activeSessionLobbyPayload;
		_activeSessionLobbyPayload = null;
		if (lobby is null || payload is null)
		{
			return false;
		}

		if (TryAcceptLobbyChange(
			lobby,
			new LobbyChange.RecoverPostGameLobby(
				payload.PlayerRoster,
				payload.RoleLockIn,
				payload.ActorSetupCards,
				payload.PublicGroupPartition),
			out persistenceAttempted))
		{
			return true;
		}

		return TryAcceptLobbyChange(
			lobby,
			new LobbyChange.WipePostGameLobby());
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
					var lobbySetupState = _lobbySetupState ??
						throw new InvalidOperationException(
							"Staged Lobby recovery requires a Lobby setup target.");
					var commit = lobbySetupState.CreateRecoveryCommit(
						stagedLobby.PlayerRoster,
						stagedLobby.RoleLockIn,
						stagedLobby.ActorSetupCards,
						stagedLobby.PublicGroupPartition);
					lobbySetupState.Publish(commit);
					_stagedLobby = stagedLobby;
					try
					{
						lobbySetupState.NotifySimulationScenarioChanged();
					}
					catch (Exception)
					{
					}
					break;
				case ActiveGameRecoveryPayload activeGame:
					ActiveGameId = _gameService.RehydrateSession(activeGame.SerializedSession);
					RefreshCurrentState();
					var resumedSession = CurrentSession ??
						throw new InvalidOperationException(
							"Core did not publish the rehydrated Game Session.");
					_activeSessionLobbyPayload = new StagedLobbyRecoveryPayload(
						resumedSession.GetPlayers()
							.Select(player => new GameSessionPlayerConfig(player.Id, player.Name))
							.ToArray(),
						resumedSession.RoleLockIn,
						resumedSession.GetModeratorActorSetupCards(),
						resumedSession.PublicGroupPartition);
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
