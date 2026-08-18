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
		if (HasActiveSession)
		{
			return false;
		}

		var decision = lobby.Decide(
			new LobbyChange.ReplaceRoleLockIn(
				expectedCurrentVersion,
				replacement));
		if (decision is null)
		{
			return false;
		}

		try
		{
			LobbyPersistenceExecutor.Execute(_saveStore, decision.Persistence);
		}
		catch (Exception)
		{
			return false;
		}

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
		if (HasActiveSession ||
			!lobby.CanReplaceActorSetupCards(
				expectedCurrentVersion,
				replacement))
		{
			return false;
		}

		var roleLockIn = lobby.AcceptedRoleLockIn!;
		try
		{
			PersistStagedLobbyBeforeApply(
				lobby.PlayerRoster,
				roleLockIn,
				replacement,
				lobby.AcceptedPublicGroupPartition,
				() => lobby.ApplyAcceptedActorSetupCards(replacement));
		}
		catch (Exception)
		{
			return false;
		}

		return true;
	}

	public bool TryReplaceStagedPublicGroupPartition(
		LobbySetupState lobby,
		PublicGroupPartition replacement)
	{
		ArgumentNullException.ThrowIfNull(lobby);
		ArgumentNullException.ThrowIfNull(replacement);
		if (HasActiveSession ||
			!lobby.CanReplacePublicGroupPartition(replacement))
		{
			return false;
		}
		if (lobby.AcceptedPublicGroupPartition?.Equals(replacement) == true)
		{
			return true;
		}

		var roleLockIn = lobby.AcceptedRoleLockIn!;
		try
		{
			PersistStagedLobbyBeforeApply(
				lobby.PlayerRoster,
				roleLockIn,
				lobby.AcceptedActorSetupCards,
				replacement,
				() => lobby.ApplyAcceptedPublicGroupPartition(replacement));
		}
		catch (Exception)
		{
			return false;
		}
		return true;
	}

	public bool TryMoveStagedPlayerDown(LobbySetupState lobby, int index)
	{
		ArgumentNullException.ThrowIfNull(lobby);
		if (HasActiveSession || !lobby.CanMovePlayerDown(index))
		{
			return false;
		}
		if (lobby.AcceptedRoleLockIn is not { } roleLockIn ||
			lobby.AcceptedRoleLockInRequiresReplacement)
		{
			return lobby.MovePlayerDown(index);
		}

		var proposedRoster = lobby.PlayerRoster.ToArray();
		(proposedRoster[index], proposedRoster[index + 1]) =
			(proposedRoster[index + 1], proposedRoster[index]);
		try
		{
			PersistStagedLobbyBeforeApply(
				proposedRoster,
				roleLockIn,
				lobby.AcceptedActorSetupCards,
				lobby.AcceptedPublicGroupPartition,
				() =>
				{
					if (!lobby.MovePlayerDown(index))
					{
						throw new InvalidOperationException(
							"The staged Seating Order changed before it could be applied.");
					}
				});
		}
		catch (Exception)
		{
			return false;
		}
		return true;
	}

	public bool TryMoveStagedPlayerUp(LobbySetupState lobby, int index)
	{
		ArgumentNullException.ThrowIfNull(lobby);
		if (HasActiveSession || !lobby.CanMovePlayerUp(index))
		{
			return false;
		}
		if (lobby.AcceptedRoleLockIn is not { } roleLockIn ||
			lobby.AcceptedRoleLockInRequiresReplacement)
		{
			return lobby.MovePlayerUp(index);
		}

		var proposedRoster = lobby.PlayerRoster.ToArray();
		(proposedRoster[index - 1], proposedRoster[index]) =
			(proposedRoster[index], proposedRoster[index - 1]);
		try
		{
			PersistStagedLobbyBeforeApply(
				proposedRoster,
				roleLockIn,
				lobby.AcceptedActorSetupCards,
				lobby.AcceptedPublicGroupPartition,
				() =>
				{
					if (!lobby.MovePlayerUp(index))
					{
						throw new InvalidOperationException(
							"The staged Seating Order changed before it could be applied.");
					}
				});
		}
		catch (Exception)
		{
			return false;
		}
		return true;
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
		if (HasActiveSession || !TryClearStagedRecoveryBeforeMembershipEdit(lobby))
		{
			return false;
		}
		if (lobby.AddPlayer(normalizedName) != AddPlayerResult.Success)
		{
			return false;
		}

		OnStateChanged();
		return true;
	}

	public bool TryRemoveStagedPlayer(LobbySetupState lobby, int index)
	{
		ArgumentNullException.ThrowIfNull(lobby);
		if (HasActiveSession || index < 0 || index >= lobby.PlayerRoster.Count)
		{
			return false;
		}
		if (!TryClearStagedRecoveryBeforeMembershipEdit(lobby))
		{
			return false;
		}
		if (!lobby.RemovePlayerAt(index))
		{
			return false;
		}

		OnStateChanged();
		return true;
	}

	private bool TryClearStagedRecoveryBeforeMembershipEdit(
		LobbySetupState lobby)
	{
		if (_stagedLobby is null && lobby.AcceptedRoleLockIn is null)
		{
			return true;
		}

		try
		{
			_saveStore.Clear();
		}
		catch (Exception)
		{
			return false;
		}
		_stagedLobby = null;
		return true;
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

		return TryReplaceStagedRoleLockIn(
			lobby,
			expectedCurrentVersion,
			replacement);
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

	private void PersistStagedLobbyBeforeApply(
		IReadOnlyList<GameSessionPlayerConfig> proposedPlayerRoster,
		RoleLockIn roleLockIn,
		ActorSetupCards actorSetupCards,
		PublicGroupPartition? publicGroupPartition,
		Action apply)
	{
		var playerRoster = proposedPlayerRoster.ToArray();
		var payload = LocalRecoveryPayloadCodec.SerializeStagedLobby(
			playerRoster,
			roleLockIn,
			actorSetupCards,
			publicGroupPartition);
		_saveStore.Save(payload);
		apply();
		_stagedLobby = new StagedLobbyRecoveryPayload(
			playerRoster,
			roleLockIn,
			actorSetupCards,
			publicGroupPartition);
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
					_lobbySetupState?.RestoreAcceptedRoleLockIn(
						stagedLobby.PlayerRoster,
						stagedLobby.RoleLockIn,
						stagedLobby.ActorSetupCards,
						stagedLobby.PublicGroupPartition);
					_stagedLobby = stagedLobby;
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
